# Builds the ready-to-run Ace (no .NET install needed) into the "publish" folder, zips it, and makes the installer.
# Usage:  .\Build-Release.ps1                         (version 4.2.0)
#         .\Build-Release.ps1 -Version 4.3.0-beta.1   (a beta)
#         .\Build-Release.ps1 -NoInstaller            (skip Ace-Setup.exe, if Inno Setup isn't installed)
# The GitHub build check and release workflow run this same script.
# Makes: NimbusAce-win-x64.zip (+ .sha256) for the auto-updater, and Ace-Setup.exe for new installs.
param([string]$Version = "4.2.0", [switch]$NoInstaller)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

foreach ($old in "publish", "NimbusAce-win-x64.zip", "NimbusAce-win-x64.zip.sha256", "Ace-Setup.exe") {
    if (Test-Path $old) { Remove-Item $old -Recurse -Force }
}

# Ace.exe: one self-contained file. Its Data folder stays next to it.
dotnet publish NimbusV3Polish.csproj -c Release -r win-x64 --self-contained true -o publish `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "Building Ace failed" }

# NimbusUpdater.exe: swaps in new versions. Small and self-contained too.
dotnet publish Updater\NimbusUpdater.csproj -c Release -r win-x64 --self-contained true -o publish `
    -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "Building the updater failed" }

Remove-Item publish\*.lib, publish\*.pdb -ErrorAction SilentlyContinue
Copy-Item Setup-AceV4.ps1, "Start here.txt" publish\
foreach ($f in "Ace.exe", "Ace.ico", "NimbusUpdater.exe", "Data\qa-bank.json", "Data\qa-general.json", "Data\recipes.json", "Data\scripts\index.json") {
    if (-not (Test-Path "publish\$f")) { throw "$f is missing from the build" }
}

# The zip the auto-updater downloads.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory("$PWD\publish", "$PWD\NimbusAce-win-x64.zip")
$h = (Get-FileHash NimbusAce-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$PWD\NimbusAce-win-x64.zip.sha256", "$h  NimbusAce-win-x64.zip`n")
Write-Host "Built Ace $Version -> NimbusAce-win-x64.zip ($h)"

# The installer: Start menu shortcut, optional desktop shortcut, uninstaller. Needs Inno Setup 6.
if (-not $NoInstaller) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        foreach ($p in "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
            if ($p -and (Test-Path $p)) { $iscc = $p; break }
        }
    }
    if (-not $iscc) { throw "Inno Setup 6 isn't installed, so Ace-Setup.exe can't be made. Install it from jrsoftware.org, or run with -NoInstaller." }
    # Inno Setup wants a plain number version (4.3.0-beta.1 -> 4.3.0) for the file info.
    $plain = ($Version -split '-')[0]
    & $iscc /Q "/DAppVer=$plain" Installer\Ace.iss
    if ($LASTEXITCODE -ne 0) { throw "Making the installer failed" }
    if (-not (Test-Path Ace-Setup.exe)) { throw "Ace-Setup.exe is missing" }
    Write-Host "Built Ace-Setup.exe"
}
