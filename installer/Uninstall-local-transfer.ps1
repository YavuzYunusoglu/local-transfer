param([switch]$Elevated)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Show-Message([string]$Message, [string]$Title, [string]$Icon) {
    Add-Type -AssemblyName System.Windows.Forms
    $messageIcon = [System.Windows.Forms.MessageBoxIcon]([Enum]::Parse([System.Windows.Forms.MessageBoxIcon], $Icon))
    [System.Windows.Forms.MessageBox]::Show(
        $Message,
        $Title,
        [System.Windows.Forms.MessageBoxButtons]::OK,
        $messageIcon
    ) | Out-Null
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath), '-Elevated')
        $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru -ArgumentList $arguments
        exit $process.ExitCode
    }

    $installDirectory = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'local-transfer'))
    $programFilesRoot = [IO.Path]::GetFullPath($env:ProgramFiles)
    if (-not $installDirectory.StartsWith($programFilesRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $installDirectory -Leaf) -ne 'local-transfer') {
        throw 'The uninstall target could not be validated.'
    }

    Get-Process -Name 'local-transfer' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    & netsh.exe advfirewall firewall delete rule name='local-transfer - Local File Transfer' | Out-Null

    Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'local-transfer.lnk') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'local-transfer') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\local-transfer' -Recurse -Force -ErrorAction SilentlyContinue

    $cleanupScript = Join-Path $env:TEMP ('local-transfer-cleanup-' + [Guid]::NewGuid().ToString('N') + '.cmd')
    $escapedDirectory = $installDirectory.Replace('%', '%%')
    [IO.File]::WriteAllText($cleanupScript, "@echo off`r`ntimeout /t 2 /nobreak >nul`r`nrmdir /s /q `"$escapedDirectory`"`r`ndel /q `"%~f0`"`r`n", [Text.Encoding]::ASCII)
    Start-Process -FilePath 'cmd.exe' -WindowStyle Hidden -ArgumentList @('/c', ('"{0}"' -f $cleanupScript))

    Show-Message 'local-transfer was removed from your computer. Your received files were kept.' 'Uninstall complete' 'Information'
    exit 0
}
catch {
    Show-Message ("The application could not be removed.`n`n" + $_.Exception.Message) 'local-transfer' 'Error'
    exit 1
}
