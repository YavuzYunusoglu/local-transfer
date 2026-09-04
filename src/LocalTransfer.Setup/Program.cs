namespace LocalTransfer.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            var assembly = typeof(Program).Assembly;
            var names = assembly.GetManifestResourceNames();
            var required = new[]
            {
                "Payload.local-transfer.exe",
                "Payload.Uninstall-local-transfer.ps1",
                "Payload.THIRD-PARTY-NOTICES.txt"
            };
            var valid = required.All(names.Contains);
            using var appPayload = assembly.GetManifestResourceStream("Payload.local-transfer.exe");
            Environment.ExitCode = valid && appPayload is { Length: > 10_000_000 } ? 0 : 1;
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}
