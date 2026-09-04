using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LocalTransfer.Setup;

internal sealed class InstallerForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(248, 250, 252);
    private static readonly Color TextColor = Color.FromArgb(15, 23, 42);
    private static readonly Color MutedColor = Color.FromArgb(71, 85, 105);
    private static readonly Color PrimaryColor = Color.FromArgb(37, 99, 235);
    private readonly CheckBox _desktopShortcut = new();
    private readonly Button _installButton = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();

    public InstallerForm()
    {
        Text = "local-transfer Setup";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 500);
        MinimumSize = MaximumSize = Size;
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        BuildInterface();
    }

    private void BuildInterface()
    {
        var card = new Panel
        {
            BackColor = Color.White,
            Location = new Point(38, 32),
            Size = new Size(544, 430),
            Padding = new Padding(34)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var mark = new BrandMark { Location = new Point(34, 32), Size = new Size(52, 52) };
        card.Controls.Add(mark);
        card.Controls.Add(new Label
        {
            Text = "local-transfer",
            AutoSize = true,
            Location = new Point(102, 28),
            Font = new Font("Segoe UI Semibold", 23F, FontStyle.Bold),
            ForeColor = TextColor
        });
        card.Controls.Add(new Label
        {
            Text = "Setup",
            AutoSize = true,
            Location = new Point(104, 66),
            Font = new Font("Segoe UI", 10F),
            ForeColor = MutedColor
        });

        card.Controls.Add(new Label
        {
            Text = "The simplest way to transfer files locally\nbetween iPhone and Windows.",
            AutoSize = true,
            Location = new Point(34, 116),
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold),
            ForeColor = TextColor
        });
        card.Controls.Add(new Label
        {
            Text = "No internet, cloud account, or mobile app is required.\nSetup configures the firewall for local-network access only.",
            AutoSize = true,
            Location = new Point(36, 178),
            Font = new Font("Segoe UI", 10F),
            ForeColor = MutedColor
        });

        var locationPanel = new Panel
        {
            Location = new Point(34, 240),
            Size = new Size(476, 54),
            BackColor = Color.FromArgb(241, 245, 249)
        };
        locationPanel.Controls.Add(new Label
        {
            Text = "Install location",
            AutoSize = true,
            Location = new Point(14, 8),
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
            ForeColor = MutedColor
        });
        locationPanel.Controls.Add(new Label
        {
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "local-transfer"),
            AutoSize = true,
            Location = new Point(14, 27),
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextColor
        });
        card.Controls.Add(locationPanel);

        _desktopShortcut.Text = "Create a desktop shortcut";
        _desktopShortcut.Checked = true;
        _desktopShortcut.AutoSize = true;
        _desktopShortcut.Location = new Point(34, 310);
        _desktopShortcut.MinimumSize = new Size(44, 28);
        _desktopShortcut.AccessibleName = "Create a desktop shortcut";
        card.Controls.Add(_desktopShortcut);

        _status.Text = "Ready to install";
        _status.AutoSize = true;
        _status.Location = new Point(34, 355);
        _status.ForeColor = MutedColor;
        _status.AccessibleName = "Installation status";
        card.Controls.Add(_status);

        _progress.Location = new Point(34, 382);
        _progress.Size = new Size(298, 12);
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Visible = false;
        card.Controls.Add(_progress);

        _installButton.Text = IsInstalled() ? "Update" : "Install";
        _installButton.Location = new Point(358, 346);
        _installButton.Size = new Size(152, 50);
        _installButton.BackColor = PrimaryColor;
        _installButton.ForeColor = Color.White;
        _installButton.FlatStyle = FlatStyle.Flat;
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 78, 216);
        _installButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 64, 175);
        _installButton.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        _installButton.Cursor = Cursors.Hand;
        _installButton.AccessibleName = "Install the local-transfer application";
        _installButton.Click += async (_, _) => await InstallAsync();
        card.Controls.Add(_installButton);

        Controls.Add(card);
        AcceptButton = _installButton;
    }

    private async Task InstallAsync()
    {
        _installButton.Enabled = false;
        _desktopShortcut.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;
        _status.Text = "Installing application…";

        try
        {
            var progress = new Progress<int>(value => _progress.Value = Math.Clamp(value, 0, 100));
            var createDesktopShortcut = _desktopShortcut.Checked;
            var installedPath = await Task.Run(() => InstallFilesAsync(progress, createDesktopShortcut));
            _progress.Value = 100;
            _status.Text = "Installation complete";
            await Task.Delay(350);
            Process.Start(new ProcessStartInfo(installedPath) { UseShellExecute = true });
            MessageBox.Show(
                "local-transfer is installed and ready to use.",
                "Installation complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "Installation failed";
            _progress.Visible = false;
            _installButton.Enabled = true;
            _desktopShortcut.Enabled = true;
            MessageBox.Show(
                $"Setup could not be completed.\n\n{ex.Message}",
                "local-transfer Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static async Task<string> InstallFilesAsync(IProgress<int> progress, bool createDesktopShortcut)
    {
        var programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var installDirectory = Path.GetFullPath(Path.Combine(programFiles, "local-transfer"));
        if (!installDirectory.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(installDirectory), "local-transfer", StringComparison.Ordinal))
            throw new InvalidOperationException("The installation target could not be validated.");

        foreach (var process in Process.GetProcessesByName("local-transfer"))
        {
            try { process.Kill(entireProcessTree: true); process.WaitForExit(5000); } catch { }
            process.Dispose();
        }

        Directory.CreateDirectory(installDirectory);
        var appPath = Path.Combine(installDirectory, "local-transfer.exe");
        var newAppPath = Path.Combine(installDirectory, "local-transfer.exe.new");
        await ExtractResourceAsync("Payload.local-transfer.exe", newAppPath, progress);
        File.Move(newAppPath, appPath, overwrite: true);
        ExtractTextResource("Payload.Uninstall-local-transfer.ps1", Path.Combine(installDirectory, "Uninstall-local-transfer.ps1"));
        ExtractTextResource("Payload.THIRD-PARTY-NOTICES.txt", Path.Combine(installDirectory, "THIRD-PARTY-NOTICES.txt"));

        CreateShortcuts(appPath, installDirectory, createDesktopShortcut);
        await ConfigureFirewallAsync(appPath);
        RegisterUninstaller(appPath, installDirectory);
        return appPath;
    }

    private static async Task ExtractResourceAsync(string resourceName, string targetPath, IProgress<int> progress)
    {
        await using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Installation resource not found: {resourceName}");
        await using var destination = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous);
        var buffer = new byte[1024 * 1024];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read));
            copied += read;
            progress.Report((int)(copied * 82 / source.Length));
        }
        await destination.FlushAsync();
    }

    private static void ExtractTextResource(string resourceName, string targetPath)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Installation resource not found: {resourceName}");
        using var destination = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }

    private static void CreateShortcuts(string appPath, string installDirectory, bool createDesktopShortcut)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("The Windows shortcut service is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            if (createDesktopShortcut)
            {
                var desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "local-transfer.lnk");
                dynamic shortcut = shell.CreateShortcut(desktopPath);
                shortcut.TargetPath = appPath;
                shortcut.WorkingDirectory = installDirectory;
                shortcut.Description = "Local file transfer between iPhone and Windows";
                shortcut.IconLocation = appPath;
                shortcut.Save();
                Marshal.FinalReleaseComObject(shortcut);
            }

            var startMenuDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "local-transfer");
            Directory.CreateDirectory(startMenuDirectory);
            dynamic startShortcut = shell.CreateShortcut(Path.Combine(startMenuDirectory, "local-transfer.lnk"));
            startShortcut.TargetPath = appPath;
            startShortcut.WorkingDirectory = installDirectory;
            startShortcut.Description = "Local file transfer between iPhone and Windows";
            startShortcut.IconLocation = appPath;
            startShortcut.Save();
            Marshal.FinalReleaseComObject(startShortcut);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static async Task ConfigureFirewallAsync(string appPath)
    {
        const string ruleName = "local-transfer - Local File Transfer";
        await RunNetshAsync(["advfirewall", "firewall", "delete", "rule", $"name={ruleName}"]);
        await RunNetshAsync([
            "advfirewall", "firewall", "add", "rule",
            $"name={ruleName}", "dir=in", "action=allow", $"program={appPath}",
            "enable=yes", "profile=any", "remoteip=LocalSubnet", "protocol=TCP", "localport=47831-47850"
        ], requireSuccess: true);
    }

    private static async Task RunNetshAsync(IEnumerable<string> arguments, bool requireSuccess = false)
    {
        var startInfo = new ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The firewall configuration tool could not start.");
        await process.WaitForExitAsync();
        if (requireSuccess && process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException("Windows Firewall could not be configured. " + error.Trim());
        }
    }

    private static void RegisterUninstaller(string appPath, string installDirectory)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\local-transfer";
        using var key = Registry.LocalMachine.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException("The uninstall registration could not be created.");
        var uninstallScript = Path.Combine(installDirectory, "Uninstall-local-transfer.ps1");
        key.SetValue("DisplayName", "local-transfer");
        key.SetValue("DisplayVersion", "1.2.1");
        key.SetValue("Publisher", "local-transfer");
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", appPath);
        key.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{uninstallScript}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(appPath).Length / 1024), RegistryValueKind.DWord);
    }

    private static bool IsInstalled() => File.Exists(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "local-transfer",
        "local-transfer.exe"));

    private sealed class BrandMark : Control
    {
        public BrandMark() { DoubleBuffered = true; AccessibleName = "local-transfer logo"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var background = new SolidBrush(PrimaryColor);
            using var pen = new Pen(Color.White, 3F)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            e.Graphics.FillRectangle(background, 0, 0, Width, Height);
            e.Graphics.DrawLine(pen, 14, 19, 38, 19);
            e.Graphics.DrawLine(pen, 14, 33, 38, 33);
            e.Graphics.DrawLine(pen, 14, 19, 14, 28);
            e.Graphics.DrawLine(pen, 38, 24, 38, 33);
        }
    }
}
