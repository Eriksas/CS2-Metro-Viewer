[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Get-FullDirectoryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    return [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Get-FullDirectoryPath (Join-Path $scriptRoot '..')
$sourcePath = Join-Path $repoRoot 'artifacts\cs2-local-mods\CS2 Metro'

if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    throw "Source local mod output was not found: $sourcePath. Build the CS2 mod first so artifacts\cs2-local-mods\CS2 Metro exists."
}

$sourcePath = (Resolve-Path -LiteralPath $sourcePath).Path
$userProfile = [Environment]::GetFolderPath('UserProfile')
if ([string]::IsNullOrWhiteSpace($userProfile)) {
    throw 'Unable to resolve the current user profile path.'
}

$modsRoot = Get-FullDirectoryPath 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\ModsData\cs2-local-mods'
$destinationPath = Get-FullDirectoryPath (Join-Path $modsRoot 'CS2 Metro')
$expectedDestinationPath = Get-FullDirectoryPath (Join-Path $modsRoot 'CS2 Metro')
$modsRootPrefix = $modsRoot + [System.IO.Path]::DirectorySeparatorChar

if (-not $destinationPath.Equals($expectedDestinationPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to deploy because the destination path is unexpected: $destinationPath"
}

if (-not $destinationPath.StartsWith($modsRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to deploy outside the CS2 local Mods directory: $destinationPath"
}

Write-Host "Source: $sourcePath"
Write-Host "Destination: $destinationPath"

New-Item -ItemType Directory -Path $modsRoot -Force | Out-Null

if (Test-Path -LiteralPath $destinationPath) {
    $resolvedDestination = Get-FullDirectoryPath (Resolve-Path -LiteralPath $destinationPath).Path

    if (-not $resolvedDestination.Equals($destinationPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected resolved destination: $resolvedDestination"
    }

    if (-not $resolvedDestination.StartsWith($modsRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a directory outside the CS2 local Mods directory: $resolvedDestination"
    }

    Remove-Item -LiteralPath $resolvedDestination -Recurse -Force
}

Copy-Item -LiteralPath $sourcePath -Destination $modsRoot -Recurse -Force

if (-not (Test-Path -LiteralPath $destinationPath -PathType Container)) {
    throw "Deploy failed: destination directory was not created: $destinationPath"
}

Write-Host "Copy succeeded."
Write-Host "Please restart Cities: Skylines II before testing."
