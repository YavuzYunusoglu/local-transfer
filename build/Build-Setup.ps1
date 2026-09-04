param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $artifactsDirectory 'publish'))
$setupPublishDirectory = [IO.Path]::GetFullPath((Join-Path $artifactsDirectory 'setup-publish'))
$projectFile = Join-Path $projectRoot 'src\LocalTransfer\local-transfer.csproj'
$setupProjectFile = Join-Path $projectRoot 'src\LocalTransfer.Setup\local-transfer.Setup.csproj'
$setupFile = Join-Path $artifactsDirectory 'local-transfer-Setup.exe'

& (Join-Path $PSScriptRoot 'Generate-Icon.ps1')
if (-not $?) { throw 'The application icon could not be generated.' }

foreach ($directory in @($publishDirectory, $setupPublishDirectory)) {
    if (-not $directory.StartsWith($artifactsDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe output path: $directory"
    }
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

Write-Host 'Building local-transfer...'
& dotnet restore $projectFile -r $Runtime
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

& dotnet publish $projectFile `
    -c Release `
    -r $Runtime `
    --self-contained true `
    --no-restore `
    -o $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$publishedApp = Join-Path $publishDirectory 'local-transfer.exe'
if (-not (Test-Path -LiteralPath $publishedApp)) { throw 'The published application was not found.' }

Write-Host 'Packaging Setup.exe...'
& dotnet restore $setupProjectFile -r $Runtime
if ($LASTEXITCODE -ne 0) { throw 'The setup project could not be restored.' }
& dotnet publish $setupProjectFile `
    -c Release `
    -r $Runtime `
    --self-contained true `
    --no-restore `
    -o $setupPublishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw 'The setup project could not be published.' }

$publishedSetup = Join-Path $setupPublishDirectory 'local-transfer-Setup.exe'
if (-not (Test-Path -LiteralPath $publishedSetup)) { throw 'The published setup file was not found.' }
Copy-Item -LiteralPath $publishedSetup -Destination $setupFile

$hash = (Get-FileHash -LiteralPath $setupFile -Algorithm SHA256).Hash
Write-Host ''
Write-Host "Ready: $setupFile"
Write-Host "SHA256: $hash"
