param(
    [Parameter(Mandatory = $true)]
    [string]$InputSvg,

    [string]$OutputPng,

    [int]$Width = 3200,

    [int]$Height = 2000
)

$ErrorActionPreference = "Stop"

$resolvedInput = Resolve-Path -LiteralPath $InputSvg -ErrorAction Stop
if ([string]::IsNullOrWhiteSpace($OutputPng)) {
    $OutputPng = [System.IO.Path]::ChangeExtension($resolvedInput.Path, ".png")
}

$outputPath = [System.IO.Path]::GetFullPath($OutputPng)
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$edgeCandidates = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
)

$edgePath = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($edgePath)) {
    throw "Microsoft Edge was not found. Install Edge or update this script with a browser path."
}

$inputUri = (New-Object System.Uri($resolvedInput.Path)).AbsoluteUri

Write-Host "Input SVG: $($resolvedInput.Path)"
Write-Host "Output PNG: $outputPath"
Write-Host "Browser: $edgePath"

if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
    Remove-Item -LiteralPath $outputPath -Force
}

$edgeOutput = & $edgePath `
    --headless `
    --disable-gpu `
    --no-first-run `
    --disable-extensions `
    "--window-size=$Width,$Height" `
    "--screenshot=$outputPath" `
    $inputUri 2>&1

if ($edgeOutput) {
    $edgeOutput | ForEach-Object { Write-Host $_ }
}

for ($attempt = 0; $attempt -lt 20 -and -not (Test-Path -LiteralPath $outputPath); $attempt++) {
    Start-Sleep -Milliseconds 250
}

if (-not (Test-Path -LiteralPath $outputPath)) {
    throw "Screenshot was not created: $outputPath"
}

Write-Host "Screenshot written to $outputPath"
