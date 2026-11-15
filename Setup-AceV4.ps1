# Setup-AceV4.ps1
# Downloads Ace V4's "meaning net" (the MiniLM sentence model, about 90 MB) so Ace understands questions
# by what they mean, not only by matching words. Run it once. Nothing else is installed.
#
# How to run: right-click this file > "Run with PowerShell".
# Or in a terminal in this folder:  powershell -ExecutionPolicy Bypass -File .\Setup-AceV4.ps1

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$folder = Join-Path $env:LOCALAPPDATA "Nimbus\ace-v4\minilm"
New-Item -ItemType Directory -Force -Path $folder | Out-Null

$files = @(
    @{ Name = "model.onnx"; Url = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx"; MinBytes = 80000000 },
    @{ Name = "vocab.txt";  Url = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";       MinBytes = 200000 }
)

Write-Host ""
Write-Host "Ace V4 setup" -ForegroundColor Cyan
Write-Host "Saving to $folder"
Write-Host ""

foreach ($f in $files) {
    $path = Join-Path $folder $f.Name
    if ((Test-Path $path) -and ((Get-Item $path).Length -ge $f.MinBytes)) {
        Write-Host "  $($f.Name) is already here." -ForegroundColor Green
        continue
    }
    Write-Host "  Downloading $($f.Name)..."
    $tmp = "$path.download"
    $ok = $false
    for ($try = 1; $try -le 3 -and -not $ok; $try++) {
        try {
            $old = $ProgressPreference
            $ProgressPreference = "SilentlyContinue"   # makes Invoke-WebRequest much faster
            Invoke-WebRequest -Uri $f.Url -OutFile $tmp -UseBasicParsing
            $ProgressPreference = $old
            if ((Get-Item $tmp).Length -lt $f.MinBytes) { throw "The download was too small." }
            Move-Item -Force $tmp $path
            $ok = $true
            Write-Host "  Got $($f.Name) ($([math]::Round((Get-Item $path).Length / 1MB, 1)) MB)." -ForegroundColor Green
        }
        catch {
            Write-Host "  Try $try didn't work: $($_.Exception.Message)" -ForegroundColor Yellow
            if (Test-Path $tmp) { Remove-Item -Force $tmp }
            Start-Sleep -Seconds 2
        }
    }
    if (-not $ok) {
        Write-Host ""
        Write-Host "Couldn't download $($f.Name). Check your internet and run this again." -ForegroundColor Red
        Write-Host "Ace still works without it, using word matching only."
        Read-Host "Press Enter to close"
        exit 1
    }
}

# Speech check: "Hey Ace" and Windows dictation need the English speech recognizer.
try {
    Add-Type -AssemblyName System.Speech
    $recognizers = [System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers()
    $english = $recognizers | Where-Object { $_.Culture.Name -like "en-*" }
    if ($english) { Write-Host "  English speech recognition is installed." -ForegroundColor Green }
    else {
        Write-Host "  English speech recognition isn't installed, so ""Hey Ace"" won't work yet." -ForegroundColor Yellow
        Write-Host "  Fix: Settings > Time & language > Speech > add English (United States)."
    }
}
catch { Write-Host "  Couldn't check speech recognition (that's okay)." -ForegroundColor Yellow }

Write-Host ""
Write-Host "All set! Start Ace again and it will use the meaning net." -ForegroundColor Cyan
Read-Host "Press Enter to close"
