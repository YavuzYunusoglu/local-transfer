using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LocalTransfer;

internal sealed class LocalTransferServer : IAsyncDisposable
{
    private const long MaximumFileSize = 10L * 1024 * 1024 * 1024;
    private const int MaximumTextLength = 100_000;
    private const int MaximumTextBytes = MaximumTextLength * 4;
    private readonly object _stateLock = new();
    private string _uploadFolder;
    private IReadOnlyList<OutgoingFile> _outgoingFiles = [];
    private string _outgoingText = string.Empty;
    private long _outgoingTextVersion;
    private WebApplication? _application;
    private string _pageTemplate = string.Empty;

    public LocalTransferServer(string uploadFolder)
    {
        _uploadFolder = uploadFolder;
        Token = CreateToken();
        Port = FindAvailablePort();
        Addresses = FindLanAddresses();
    }

    public event Action<TransferRecord>? TransferCompleted;
    public event Action<string>? DeviceConnected;
    public event Action<string>? TextReceived;

    public int Port { get; private set; }
    public string Token { get; private set; }
    public IReadOnlyList<IPAddress> Addresses { get; private set; }
    public bool IsRunning => _application is not null;
    public string? PrimaryUrl => Addresses.Count == 0 ? null : BuildUrl(Addresses[0]);

    public string UploadFolder
    {
        get { lock (_stateLock) return _uploadFolder; }
    }

    public IReadOnlyList<OutgoingFile> OutgoingFiles
    {
        get { lock (_stateLock) return _outgoingFiles.ToArray(); }
    }

    public OutgoingText OutgoingText
    {
        get
        {
            lock (_stateLock)
                return new OutgoingText(_outgoingText, _outgoingTextVersion);
        }
    }

    public string BuildUrl(IPAddress address) =>
        $"http://{address}:{Port}/?token={Uri.EscapeDataString(Token)}";

    public void RefreshConnection()
    {
        Token = CreateToken();
        Addresses = FindLanAddresses();
    }

    public void SetUploadFolder(string folder)
    {
        var fullPath = Path.GetFullPath(folder);
        Directory.CreateDirectory(fullPath);
        lock (_stateLock) _uploadFolder = fullPath;
    }

    public IReadOnlyList<OutgoingFile> SetOutgoingFiles(IEnumerable<string> filePaths)
    {
        var files = filePaths
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new OutgoingFile(Guid.NewGuid().ToString("N"), info.Name, info.Length, info.FullName);
            })
            .ToArray();
        lock (_stateLock) _outgoingFiles = files;
        return files;
    }

    public void ClearOutgoingFiles()
    {
        lock (_stateLock) _outgoingFiles = [];
    }

    public void SetOutgoingText(string text)
    {
        if (text.Length > MaximumTextLength)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text cannot exceed {MaximumTextLength:N0} characters.");

        lock (_stateLock)
        {
            _outgoingText = text;
            _outgoingTextVersion++;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
            return;

        Directory.CreateDirectory(_uploadFolder);
        _pageTemplate = ReadEmbeddedPage();

        var options = new WebApplicationOptions
        {
            ApplicationName = typeof(LocalTransferServer).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            Args = []
        };

        var builder = WebApplication.CreateSlimBuilder(options);
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.ListenAnyIP(Port);
            serverOptions.Limits.MaxRequestBodySize = MaximumFileSize;
            serverOptions.AddServerHeader = false;
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; img-src 'self' data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; form-action 'self'";
            await next();
        });

        app.MapGet("/", (HttpContext context) =>
        {
            if (!HasValidToken(context.Request))
                return Results.Content(AccessDeniedPage(), "text/html; charset=utf-8", Encoding.UTF8, 403);

            var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown device";
            DeviceConnected?.Invoke(remoteAddress);

            var page = _pageTemplate
                .Replace("{{TOKEN_JSON}}", JsonSerializer.Serialize(Token), StringComparison.Ordinal)
                .Replace("{{COMPUTER_JSON}}", JsonSerializer.Serialize(Environment.MachineName), StringComparison.Ordinal)
                .Replace("{{MAX_SIZE_JSON}}", MaximumFileSize.ToString(), StringComparison.Ordinal)
                .Replace("{{I18N_JSON}}", Localizer.ClientCatalogJson, StringComparison.Ordinal);
            return Results.Content(page, "text/html; charset=utf-8", Encoding.UTF8);
        });

        app.MapGet("/api/status", (HttpContext context) =>
        {
            if (!HasValidToken(context.Request))
                return Results.Unauthorized();

            return Results.Json(new
            {
                ok = true,
                computer = Environment.MachineName,
                maxFileSize = MaximumFileSize
            });
        });

        app.MapGet("/api/outgoing", (HttpContext context) =>
        {
            if (!HasValidToken(context.Request))
                return Results.Unauthorized();

            var files = OutgoingFiles
                .Where(file => File.Exists(file.FullPath))
                .Select(file => new { id = file.Id, fileName = file.FileName, size = file.Size })
                .ToArray();
            return Results.Json(new { files });
        });

        app.MapGet("/api/text", (HttpContext context) =>
        {
            if (!HasValidToken(context.Request))
                return Results.Unauthorized();

            var outgoingText = OutgoingText;
            return Results.Json(new { text = outgoingText.Text, version = outgoingText.Version });
        });

        app.MapGet("/api/download/{id}", (HttpContext context, string id) =>
        {
            if (!HasValidToken(context.Request))
                return Results.Unauthorized();

            var file = OutgoingFiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.Ordinal) && File.Exists(candidate.FullPath));
            if (file is null)
                return Results.NotFound(new { error = "This file is no longer shared." });

            return Results.File(
                file.FullPath,
                contentType: "application/octet-stream",
                fileDownloadName: file.FileName,
                enableRangeProcessing: true);
        });

        app.MapPost("/api/upload", (Delegate)UploadAsync);
        app.MapPost("/api/text", (Delegate)ReceiveTextAsync);

        try
        {
            await app.StartAsync(cancellationToken);
            _application = app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private async Task<IResult> UploadAsync(HttpContext context)
    {
        if (!HasValidToken(context.Request))
            return Results.Unauthorized();

        var contentLength = context.Request.ContentLength;
        if (contentLength is > MaximumFileSize)
            return Results.Json(new { error = "The file exceeds the 10 GB limit." }, statusCode: 413);

        var requestedName = context.Request.Query["name"].ToString();
        var safeName = SanitizeFileName(requestedName);
        if (string.IsNullOrWhiteSpace(safeName))
            return Results.BadRequest(new { error = "The file name is invalid." });

        var uploadFolder = UploadFolder;
        Directory.CreateDirectory(uploadFolder);
        var temporaryPath = Path.Combine(uploadFolder, $".{Guid.NewGuid():N}.part");

        try
        {
            long total = 0;
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 1024];
                while (true)
                {
                    var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
                    if (read == 0)
                        break;

                    total += read;
                    if (total > MaximumFileSize)
                        throw new FileTooLargeException();

                    await target.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                }
                await target.FlushAsync(context.RequestAborted);
            }

            var finalPath = MoveToUniqueDestination(temporaryPath, safeName, uploadFolder);
            var record = new TransferRecord(Path.GetFileName(finalPath), total, DateTime.Now, finalPath);
            TransferCompleted?.Invoke(record);
            return Results.Json(new { ok = true, fileName = record.FileName, size = record.Size });
        }
        catch (FileTooLargeException)
        {
            TryDelete(temporaryPath);
            return Results.Json(new { error = "The file exceeds the 10 GB limit." }, statusCode: 413);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            return Results.Json(new { error = "The transfer was cancelled." }, statusCode: 499);
        }
        catch (IOException)
        {
            TryDelete(temporaryPath);
            return Results.Json(new { error = "The file could not be saved. Check available disk space." }, statusCode: 507);
        }
        catch
        {
            TryDelete(temporaryPath);
            return Results.Json(new { error = "An unexpected transfer error occurred." }, statusCode: 500);
        }
    }

    private async Task<IResult> ReceiveTextAsync(HttpContext context)
    {
        if (!HasValidToken(context.Request))
            return Results.Unauthorized();
        if (context.Request.ContentLength is > MaximumTextBytes)
            return Results.Json(new { error = "The text exceeds the 100,000 character limit." }, statusCode: 413);

        try
        {
            await using var content = new MemoryStream();
            var buffer = new byte[8192];
            var total = 0;
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
                if (read == 0)
                    break;
                total += read;
                if (total > MaximumTextBytes)
                    return Results.Json(new { error = "The text exceeds the 100,000 character limit." }, statusCode: 413);
                await content.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
            }

            var text = new UTF8Encoding(false, true).GetString(content.ToArray());
            if (string.IsNullOrWhiteSpace(text))
                return Results.BadRequest(new { error = "Text cannot be empty." });
            if (text.Length > MaximumTextLength)
                return Results.Json(new { error = "The text exceeds the 100,000 character limit." }, statusCode: 413);

            TextReceived?.Invoke(text);
            return Results.Json(new { ok = true, length = text.Length });
        }
        catch (DecoderFallbackException)
        {
            return Results.BadRequest(new { error = "The text must use UTF-8 encoding." });
        }
        catch (OperationCanceledException)
        {
            return Results.Json(new { error = "The text transfer was cancelled." }, statusCode: 499);
        }
    }

    private bool HasValidToken(HttpRequest request)
    {
        var candidate = request.Query["token"].ToString();
        if (candidate.Length != Token.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(Token));
    }

    private static string MoveToUniqueDestination(string temporaryPath, string safeName, string uploadFolder)
    {
        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);

        for (var copy = 0; copy < 10_000; copy++)
        {
            var candidateName = copy == 0 ? safeName : $"{baseName} ({copy}){extension}";
            var candidatePath = Path.Combine(uploadFolder, candidateName);
            try
            {
                File.Move(temporaryPath, candidatePath, overwrite: false);
                return candidatePath;
            }
            catch (IOException) when (File.Exists(candidatePath))
            {
                // Try the next collision-safe name.
            }
        }

        throw new IOException("A unique file name could not be generated.");
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName).Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        name = name.TrimEnd('.', ' ');
        if (name.Length > 180)
        {
            var extension = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            name = stem[..Math.Min(stem.Length, 180 - extension.Length)] + extension;
        }
        return name;
    }

    private static int FindAvailablePort()
    {
        for (var port = 47831; port <= 47850; port++)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException) { }
        }
        throw new InvalidOperationException("No available port was found for local transfer.");
    }

    private static IReadOnlyList<IPAddress> FindLanAddresses()
    {
        static bool IsUsable(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                return false;
            var bytes = address.GetAddressBytes();
            return !(bytes[0] == 169 && bytes[1] == 254) && bytes[0] != 0;
        }

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Where(network => network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses
                .Where(address => IsUsable(address.Address))
                .Select(address => new
                {
                    address.Address,
                    Score = (network.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork) ? 100 : 0)
                            + (network.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 20 : 0)
                            + (network.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 10 : 0)
                            - (network.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase) ? 50 : 0)
                }))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Address.ToString(), StringComparer.Ordinal)
            .Select(item => item.Address)
            .Distinct()
            .ToArray();
    }

    private static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(18);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string ReadEmbeddedPage()
    {
        var assembly = typeof(LocalTransferServer).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Web.index.html", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded mobile interface was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string AccessDeniedPage() => """
        <!doctype html><html lang="tr"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Invalid connection</title><body style="font:17px system-ui;padding:32px;color:#0f172a;background:#f8fafc">
        <h1>This connection has expired</h1><p>Scan the current QR code shown in the local-transfer desktop app.</p></body></html>
        """;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is null)
            return;

        try { await _application.StopAsync(TimeSpan.FromSeconds(3)); } catch { }
        await _application.DisposeAsync();
        _application = null;
    }

    private sealed class FileTooLargeException : Exception;
}
