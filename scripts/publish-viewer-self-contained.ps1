Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$version = 'v0.1.0-alpha.2-candidate'
$projectPath = Join-Path $repoRoot 'src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj'
$outputPath = Join-Path $repoRoot 'artifacts\viewer-win-x64-self-contained'
$tempOutputPath = Join-Path $repoRoot 'artifacts\viewer-win-x64-self-contained.tmp'
$sampleOutputPath = Join-Path $tempOutputPath 'samples'

if (Test-Path $tempOutputPath) {
    Remove-Item -LiteralPath $tempOutputPath -Recurse -Force
}

dotnet restore $projectPath `
    -r win-x64 `
    --source 'https://api.nuget.org/v3/index.json'
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --no-restore `
    -o $tempOutputPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force $sampleOutputPath | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\VIEWER_QUICK_START.md') -Destination (Join-Path $tempOutputPath 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'samples\sample-metro-small.json') -Destination (Join-Path $sampleOutputPath 'sample-metro-small.json') -Force

$commit = 'unknown'
try {
    $commit = (git -C $repoRoot rev-parse --short HEAD).Trim()
}
catch {
}

@(
    'CS2 Metro Diagram Viewer'
    "Version: $version"
    'Package: win-x64 self-contained single-file'
    "BuiltAtUtc: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    "Commit: $commit"
    'Requires: Windows x64'
) | Set-Content -LiteralPath (Join-Path $tempOutputPath 'build-info.txt') -Encoding UTF8

if (Test-Path $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

Move-Item -LiteralPath $tempOutputPath -Destination $outputPath

Write-Host "Viewer self-contained package written to $outputPath"
