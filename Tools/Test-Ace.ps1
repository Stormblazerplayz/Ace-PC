# Checks a fresh build of Ace on a real Windows machine (the GitHub build check runs this after Build-Release.ps1).
#  1. Runs publish\Ace.exe --smoke-test: one normal window with a taskbar button, no pop-up, brain loads, 12 x 12 = 144.
#  2. Installs Ace-Setup.exe silently, checks Ace.exe and the Start menu shortcut are there, runs the installed Ace's
#     smoke test too, then uninstalls it and checks it's gone.
param([switch]$SkipInstaller)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

function Invoke-Smoke([string]$exe, [string]$name) {
    $log = Join-Path (Split-Path $exe) "smoke-test.log"
    if (Test-Path $log) { Remove-Item $log -Force }
    Write-Host "== Smoke test: $name"
    $p = Start-Process -FilePath $exe -ArgumentList "--smoke-test" -PassThru
    if (-not $p.WaitForExit(180000)) { $p.Kill(); if (Test-Path $log) { Get-Content $log }; throw "$name didn't finish its smoke test in 3 minutes" }
    if (Test-Path $log) { Get-Content $log } else { Write-Host "(no smoke-test.log)" }
    if ($p.ExitCode -ne 0) { throw "$name smoke test failed (exit code $($p.ExitCode))" }
    Write-Host "== $name passed"
}

# 1. The built copy (run from a copy so the zip stays clean).
$copy = Join-Path $env:RUNNER_TEMP "ace-smoke"
if (-not $env:RUNNER_TEMP) { $copy = Join-Path ([IO.Path]::GetTempPath()) "ace-smoke" }
if (Test-Path $copy) { Remove-Item $copy -Recurse -Force }
Copy-Item publish $copy -Recurse
Invoke-Smoke (Join-Path $copy "Ace.exe") "Built Ace"
Get-Process Ace -ErrorAction SilentlyContinue | Stop-Process -Force

if ($SkipInstaller) { return }

# 2. The installer.
$installDir = Join-Path $env:LOCALAPPDATA "Programs\Ace"
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Ace.lnk"
Write-Host "== Installing Ace-Setup.exe"
$setup = Start-Process -FilePath (Resolve-Path Ace-Setup.exe) -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=$env:TEMP\ace-setup.log" -PassThru -Wait
if ($setup.ExitCode -ne 0) { Get-Content "$env:TEMP\ace-setup.log" -ErrorAction SilentlyContinue; throw "Ace-Setup.exe failed (exit code $($setup.ExitCode))" }
foreach ($f in "Ace.exe", "NimbusUpdater.exe", "Ace.ico", "Data\qa-bank.json", "unins000.exe") {
    if (-not (Test-Path (Join-Path $installDir $f))) { throw "The installer didn't put $f in $installDir" }
}
if (-not (Test-Path $startMenu)) { throw "The installer didn't add Ace to the Start menu" }
$uninstallKey = Get-ChildItem "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall" | Where-Object { (Get-ItemProperty $_.PSPath).DisplayName -eq "Ace" }
if (-not $uninstallKey) { throw "Ace isn't listed in Settings > Apps" }
Write-Host "== Installed to $installDir, Start menu shortcut and Apps entry are there"

Invoke-Smoke (Join-Path $installDir "Ace.exe") "Installed Ace"
Get-Process Ace -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "== Uninstalling"
$un = Start-Process -FilePath (Join-Path $installDir "unins000.exe") -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru -Wait
if ($un.ExitCode -ne 0) { throw "The uninstaller failed (exit code $($un.ExitCode))" }
# The uninstaller hands off to a copy of itself, so wait for it to finish (up to a minute).
for ($i = 0; $i -lt 60 -and (Test-Path (Join-Path $installDir "Ace.exe")); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 2
if (Test-Path (Join-Path $installDir "Ace.exe")) { throw "Uninstalling left Ace.exe behind" }
if (Test-Path $startMenu) { throw "Uninstalling left the Start menu shortcut behind" }
$run = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -ErrorAction SilentlyContinue).Ace
if ($run) { throw "Uninstalling left Ace in 'start with Windows'" }
Write-Host "== All Ace checks passed"
