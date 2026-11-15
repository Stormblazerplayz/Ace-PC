# Builds the ready-to-run Ace (no .NET install needed) into the "publish" folder and zips it.
# Usage:  .\Build-Release.ps1            (version 4.1.0)
#         .\Build-Release.ps1 -Version 4.2.0-beta.1   (a beta)
# The GitHub release workflow runs this same script.
param([string]$Version = "4.1.0")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (Test-Path publish) { Remove-Item publish -Recurse -Force }

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

if (Test-Path NimbusAce-win-x64.zip) { Remove-Item NimbusAce-win-x64.zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory("$PWD\publish", "$PWD\NimbusAce-win-x64.zip")
$h = (Get-FileHash NimbusAce-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$PWD\NimbusAce-win-x64.zip.sha256", "$h  NimbusAce-win-x64.zip`n")
Write-Host "Built Ace $Version -> NimbusAce-win-x64.zip ($h)"
