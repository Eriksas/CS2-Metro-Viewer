[CmdletBinding()]
param(
    [string] $InputJson = 'D:\CS2MetroDiagram\metro-export.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Count {
    param(
        $Value
    )

    if ($null -eq $Value) {
        return 0
    }

    return @($Value).Count
}

function Get-JsonProperty {
    param(
        $Object,
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Format-SourceSummary {
    param(
        $PathPoints
    )

    $points = @($PathPoints)
    $withSource = @($points | Where-Object {
        $source = Get-JsonProperty $_ 'source'
        $null -ne $source -and -not [string]::IsNullOrWhiteSpace([string] $source)
    })
    if ($withSource.Count -eq 0) {
        return 'none'
    }

    $sourceCounts = @{}
    foreach ($point in $withSource) {
        $source = [string] (Get-JsonProperty $point 'source')
        if (-not $sourceCounts.ContainsKey($source)) {
            $sourceCounts[$source] = 0
        }

        $sourceCounts[$source]++
    }

    return (($sourceCounts.GetEnumerator() |
        Sort-Object -Property Name |
        ForEach-Object { '{0}: {1}' -f $_.Name, $_.Value }) -join '; ')
}

$inputPath = [System.IO.Path]::GetFullPath($InputJson)

if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
    Write-Host "ERROR: Input metro export JSON was not found: $inputPath. Export Real Metro JSON in-game first, or pass -InputJson <path>." -ForegroundColor Red
    exit 1
}

try {
    $document = Get-Content -LiteralPath $inputPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
catch {
    Write-Host "ERROR: Failed to parse metro export JSON: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$lines = @()
$network = Get-JsonProperty $document 'network'
if ($null -ne $network -and $null -ne (Get-JsonProperty $network 'lines')) {
    $lines = @(Get-JsonProperty $network 'lines')
}

$stations = @()
if ($null -ne $network -and $null -ne (Get-JsonProperty $network 'stations')) {
    $stations = @(Get-JsonProperty $network 'stations')
}

$schemaVersionValue = Get-JsonProperty $document 'schemaVersion'
$generator = Get-JsonProperty $document 'generator'
$city = Get-JsonProperty $document 'city'
$generatorVersionValue = Get-JsonProperty $generator 'version'
$cityNameValue = Get-JsonProperty $city 'name'

$schemaVersion = if ($null -ne $schemaVersionValue) { $schemaVersionValue } else { 'missing' }
$generatorVersion = if ($null -ne $generatorVersionValue) { $generatorVersionValue } else { 'missing' }
$cityName = if ($null -ne $cityNameValue) { $cityNameValue } else { 'missing' }

Write-Host "Input: $inputPath"
Write-Host "schemaVersion: $schemaVersion"
Write-Host "generator.version: $generatorVersion"
Write-Host "city.name: $cityName"
Write-Host "lines: $($lines.Count)"
Write-Host "stations: $($stations.Count)"
Write-Host ''
Write-Host 'Line pathPoints summary:'

$totalPathPoints = 0
$linesWithPathPoints = 0
$linesWithMorePathPointsThanStops = 0

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    $lineNameValue = Get-JsonProperty $line 'name'
    $lineName = if ($null -ne $lineNameValue -and -not [string]::IsNullOrWhiteSpace([string] $lineNameValue)) { [string] $lineNameValue } else { "Line $($i + 1)" }
    $stopsCount = Get-Count (Get-JsonProperty $line 'stops')
    $pathPointsValue = Get-JsonProperty $line 'pathPoints'
    $pathPoints = if ($null -ne $pathPointsValue) { @($pathPointsValue) } else { @() }
    $pathPointsCount = $pathPoints.Count
    $totalPathPoints += $pathPointsCount
    if ($pathPointsCount -gt 0) {
        $linesWithPathPoints++
    }

    $hasMorePathPointsThanStops = $pathPointsCount -gt $stopsCount
    if ($hasMorePathPointsThanStops) {
        $linesWithMorePathPointsThanStops++
    }

    Write-Host ("- {0}" -f $lineName)
    Write-Host ("  stops: {0}" -f $stopsCount)
    Write-Host ("  pathPoints: {0}" -f $pathPointsCount)
    Write-Host ("  pathPoints > stops: {0}" -f $hasMorePathPointsThanStops)
    Write-Host ("  source summary: {0}" -f (Format-SourceSummary $pathPoints))
}

Write-Host ''
Write-Host "total pathPoints: $totalPathPoints"
Write-Host "lines with pathPoints: $linesWithPathPoints / $($lines.Count)"
Write-Host "lines with pathPoints > stops: $linesWithMorePathPointsThanStops / $($lines.Count)"

if ($totalPathPoints -eq 0) {
    Write-Warning 'No pathPoints were found in this export.'
}

if ($lines.Count -gt 0) {
    $linesNotGreater = $lines.Count - $linesWithMorePathPointsThanStops
    if ($linesNotGreater -ge [Math]::Ceiling($lines.Count / 2.0)) {
        Write-Warning 'Most lines have pathPoints count <= stops count. CurveElement-enhanced geometry may not have been exported or may need more investigation.'
    }
}
