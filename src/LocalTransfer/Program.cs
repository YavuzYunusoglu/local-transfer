using System.Diagnostics;
using System.Security.Cryptography;

namespace LocalTransfer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = SelfTest.RunAsync().GetAwaiter().GetResult();
            return;
        }

        if (args.Contains("--headless", StringComparer.OrdinalIgnoreCase))
        {
            HeadlessHost.RunAsync().GetAwaiter().GetResult();
            return;
        }

        using var mutex = new Mutex(true, "Local\\LocalTransfer.40EFA229", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "local-transfer is already running.",
                "local-transfer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
    }
}

internal static class HeadlessHost
{
    public static async Task RunAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "local-transfer-headless");
        await using var server = new LocalTransferServer(folder);
        Directory.CreateDirectory(folder);
        var samplePath = Path.Combine(folder, "computer-sample.pdf");
        await File.WriteAllTextAsync(samplePath, "local-transfer interface test");
        server.SetOutgoingFiles([samplePath]);
        server.SetOutgoingText("Text shared from the computer for interface testing.");
        await server.StartAsync();
        var url = $"http://127.0.0.1:{server.Port}/?token={Uri.EscapeDataString(server.Token)}";
        Console.WriteLine($"LOCAL_TRANSFER_URL={url}");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}

internal static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        var testFolder = Path.Combine(Path.GetTempPath(), $"local-transfer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testFolder);
        await using var server = new LocalTransferServer(testFolder);

        try
        {
            await server.StartAsync();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var localUrl = $"http://127.0.0.1:{server.Port}/?token={Uri.EscapeDataString(server.Token)}";
            var page = await client.GetStringAsync(localUrl);
            if (!page.Contains("Send files", StringComparison.Ordinal) || !page.Contains("catalog", StringComparison.Ordinal))
                throw new InvalidOperationException("The mobile page could not be loaded.");

            using var denied = await client.GetAsync($"http://127.0.0.1:{server.Port}/?token=invalid");
            if (denied.StatusCode != System.Net.HttpStatusCode.Forbidden)
                throw new InvalidOperationException("An invalid connection token was not rejected.");

            var payload = "local-transfer-self-test";
            var uploadUrl = $"http://127.0.0.1:{server.Port}/api/upload?token={Uri.EscapeDataString(server.Token)}&name=self-test.txt";
            using (var content = new StringContent(payload))
            using (var response = await client.PostAsync(uploadUrl, content))
                response.EnsureSuccessStatusCode();
            using (var duplicateContent = new StringContent(payload))
            using (var duplicateResponse = await client.PostAsync(uploadUrl, duplicateContent))
                duplicateResponse.EnsureSuccessStatusCode();

            var traversalUrl = $"http://127.0.0.1:{server.Port}/api/upload?token={Uri.EscapeDataString(server.Token)}&name={Uri.EscapeDataString("../outside.txt")}";
            using (var traversalContent = new StringContent("safe"))
            using (var traversalResponse = await client.PostAsync(traversalUrl, traversalContent))
                traversalResponse.EnsureSuccessStatusCode();

            var saved = Directory.GetFiles(testFolder, "self-test*.txt");
            if (saved.Length != 2 || saved.Any(file => File.ReadAllText(file) != payload))
                throw new InvalidOperationException("File transfer validation failed.");
            if (!File.Exists(Path.Combine(testFolder, "outside.txt")))
                throw new InvalidOperationException("File name safety could not be validated.");
            if (Directory.GetFiles(testFolder, "*.part").Length != 0)
                throw new InvalidOperationException("A temporary transfer file was not cleaned up.");

            var outgoingPath = Path.Combine(testFolder, "phone-test.bin");
            var outgoingPayload = RandomNumberGenerator.GetBytes(4096);
            await File.WriteAllBytesAsync(outgoingPath, outgoingPayload);
            var outgoing = server.SetOutgoingFiles([outgoingPath]).Single();
            var outgoingListUrl = $"http://127.0.0.1:{server.Port}/api/outgoing?token={Uri.EscapeDataString(server.Token)}";
            var outgoingJson = await client.GetStringAsync(outgoingListUrl);
            if (!outgoingJson.Contains(outgoing.Id, StringComparison.Ordinal) || !outgoingJson.Contains("phone-test.bin", StringComparison.Ordinal))
                throw new InvalidOperationException("The computer-to-phone file list could not be validated.");
            var downloadUrl = $"http://127.0.0.1:{server.Port}/api/download/{outgoing.Id}?token={Uri.EscapeDataString(server.Token)}";
            var downloadedPayload = await client.GetByteArrayAsync(downloadUrl);
            if (!outgoingPayload.SequenceEqual(downloadedPayload))
                throw new InvalidOperationException("The computer-to-phone download could not be validated.");

            var receivedText = string.Empty;
            server.TextReceived += text => receivedText = text;
            const string computerText = "Hello from computer — 你好";
            server.SetOutgoingText(computerText);
            var outgoingTextUrl = $"http://127.0.0.1:{server.Port}/api/text?token={Uri.EscapeDataString(server.Token)}";
            var outgoingTextJson = await client.GetStringAsync(outgoingTextUrl);
            using var outgoingTextDocument = System.Text.Json.JsonDocument.Parse(outgoingTextJson);
            if (outgoingTextDocument.RootElement.GetProperty("text").GetString() != computerText)
                throw new InvalidOperationException("The computer-to-phone text could not be validated.");

            const string phoneText = "Hello from phone — merhaba";
            using (var textContent = new StringContent(phoneText, System.Text.Encoding.UTF8, "text/plain"))
            using (var textResponse = await client.PostAsync(outgoingTextUrl, textContent))
                textResponse.EnsureSuccessStatusCode();
            if (!string.Equals(receivedText, phoneText, StringComparison.Ordinal))
                throw new InvalidOperationException("The phone-to-computer text could not be validated.");

            using var deniedText = await client.GetAsync($"http://127.0.0.1:{server.Port}/api/text?token=invalid");
            if (deniedText.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("An invalid text-transfer token was not rejected.");

            var customFolder = Path.Combine(testFolder, "custom-folder");
            server.SetUploadFolder(customFolder);
            var customUploadUrl = $"http://127.0.0.1:{server.Port}/api/upload?token={Uri.EscapeDataString(server.Token)}&name=custom.txt";
            using (var customContent = new StringContent("custom-folder-test"))
            using (var customResponse = await client.PostAsync(customUploadUrl, customContent))
                customResponse.EnsureSuccessStatusCode();
            if (!File.Exists(Path.Combine(customFolder, "custom.txt")))
                throw new InvalidOperationException("The custom download folder could not be validated.");

            using var qr = QrBitmapFactory.Create(localUrl, 280);
            if (qr.Width < 200 || qr.Width != qr.Height)
                throw new InvalidOperationException("The QR code could not be generated.");

            return 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(testFolder, true); } catch { }
        }
    }
}
