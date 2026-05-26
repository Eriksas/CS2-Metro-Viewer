param(
    [string]$InputSvg = "artifacts\path-geometry-comparison\10-geographic-shared-corridor-express-stripe.svg",

    [string]$OutputReport = "artifacts\path-geometry-comparison\svg-render-debug-summary.txt",

    [double]$NearEndpointThreshold = 8
)

$ErrorActionPreference = "Stop"

$inputPath = Resolve-Path -LiteralPath $InputSvg -ErrorAction Stop
$outputPath = [System.IO.Path]::GetFullPath($OutputReport)
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

[xml]$xml = Get-Content -Path $inputPath.Path -Encoding UTF8
$routeStyle = ($xml.SelectNodes('//*[local-name()="style"]') | ForEach-Object { $_.'#text' }) -join "`n"
$defaultRouteWidth = $null
if ($routeStyle -match '\.route\s*\{[^}]*stroke-width:\s*([0-9.]+)') {
    $defaultRouteWidth = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
}

function Get-AttributeValue($Element, [string]$Name) {
    $attribute = $Element.Attributes[$Name]
    if ($null -eq $attribute) {
        return $null
    }

    return $attribute.Value
}

function Read-Points([string]$Value) {
    $points = New-Object System.Collections.Generic.List[object]
    foreach ($pair in ($Value -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $parts = $pair -split ','
        if ($parts.Count -eq 2) {
            $points.Add([pscustomobject]@{
                X = [double]::Parse($parts[0], [Globalization.CultureInfo]::InvariantCulture)
                Y = [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
            })
        }
    }

    return $points
}

function Get-StrokeWidth($Element) {
    $style = Get-AttributeValue $Element "style"
    if ($style -and $style -match 'stroke-width:\s*([0-9.]+)') {
        return [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
    }

    $width = Get-AttributeValue $Element "stroke-width"
    if ($width) {
        return [double]::Parse($width, [Globalization.CultureInfo]::InvariantCulture)
    }

    return $defaultRouteWidth
}

function Get-Distance($A, $B) {
    $dx = $A.X - $B.X
    $dy = $A.Y - $B.Y
    return [Math]::Sqrt($dx * $dx + $dy * $dy)
}

$routes = @($xml.SelectNodes('//*[local-name()="polyline" and @class="route"]'))
$routeInfos = @()
for ($i = 0; $i -lt $routes.Count; $i++) {
    $element = $routes[$i]
    $points = Read-Points (Get-AttributeValue $element "points")
    $family = Get-AttributeValue $element "data-display-family-key"
    if ([string]::IsNullOrWhiteSpace($family)) {
        $family = "(none)"
    }

    $isShared = (Get-AttributeValue $element "data-shared-corridor") -eq "true"
    $isExpress = (Get-AttributeValue $element "data-express-marker") -eq "white-center-stripe"
    $skip = Get-AttributeValue $element "data-shared-corridor-skipped"
    $kind = if ($isExpress) {
        "express"
    } elseif ($isShared) {
        "shared"
    } elseif ($skip) {
        "shared-fallback"
    } else {
        "normal"
    }

    $routeInfos += [pscustomobject]@{
        Index = $i
        Kind = $kind
        Family = $family
        RunId = Get-AttributeValue $element "data-shared-corridor-run-id"
        SharedLayer = Get-AttributeValue $element "data-shared-corridor-layer"
        ExpressFamily = Get-AttributeValue $element "data-express-family"
        Stroke = Get-AttributeValue $element "stroke"
        StrokeWidth = Get-StrokeWidth $element
        PointCount = $points.Count
        Start = if ($points.Count -gt 0) { $points[0] } else { $null }
        End = if ($points.Count -gt 0) { $points[$points.Count - 1] } else { $null }
        PointsText = Get-AttributeValue $element "points"
    }
}

$duplicateGroups = $routeInfos |
    Group-Object Family,PointsText |
    Where-Object { $_.Count -gt 1 }

$nearGaps = New-Object System.Collections.Generic.List[object]
$families = $routeInfos | Where-Object { $_.Kind -ne "express" -and $_.Start -and $_.End } | Group-Object Family
foreach ($familyGroup in $families) {
    $items = @($familyGroup.Group)
    for ($i = 0; $i -lt $items.Count; $i++) {
        for ($j = $i + 1; $j -lt $items.Count; $j++) {
            $candidates = @(
                [pscustomobject]@{ Left = "end"; Right = "start"; Distance = Get-Distance $items[$i].End $items[$j].Start },
                [pscustomobject]@{ Left = "start"; Right = "end"; Distance = Get-Distance $items[$i].Start $items[$j].End },
                [pscustomobject]@{ Left = "start"; Right = "start"; Distance = Get-Distance $items[$i].Start $items[$j].Start },
                [pscustomobject]@{ Left = "end"; Right = "end"; Distance = Get-Distance $items[$i].End $items[$j].End }
            )
            $near = $candidates | Where-Object { $_.Distance -gt 0.001 -and $_.Distance -le $NearEndpointThreshold } | Sort-Object Distance | Select-Object -First 1
            if ($near) {
                $nearGaps.Add([pscustomobject]@{
                    Family = $familyGroup.Name
                    LeftIndex = $items[$i].Index
                    RightIndex = $items[$j].Index
                    Distance = $near.Distance
                    LeftEndpoint = $near.Left
                    RightEndpoint = $near.Right
                })
            }
        }
    }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# SVG Render Debug Summary")
$lines.Add("")
$lines.Add("Input: $($inputPath.Path)")
$lines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$lines.Add("")
$lines.Add("## Counts")
$lines.Add("- route path elements: $($routeInfos.Count)")
$lines.Add("- normal display family path elements: $(($routeInfos | Where-Object Kind -eq 'normal').Count)")
$lines.Add("- shared corridor path elements: $(($routeInfos | Where-Object Kind -eq 'shared').Count)")
$lines.Add("- express stripe path elements: $(($routeInfos | Where-Object Kind -eq 'express').Count)")
$lines.Add("- shared fallback path elements: $(($routeInfos | Where-Object Kind -eq 'shared-fallback').Count)")
$lines.Add("")
$lines.Add("## Stroke Widths")
$routeInfos |
    Group-Object Kind,StrokeWidth |
    Sort-Object Name |
    ForEach-Object { $lines.Add("- $($_.Name): $($_.Count)") }
$lines.Add("")
$lines.Add("## Family Fragment Counts")
$routeInfos |
    Where-Object { $_.Kind -ne "express" } |
    Group-Object Family |
    Sort-Object Name |
    ForEach-Object { $lines.Add("- $($_.Name): $($_.Count)") }
$lines.Add("")
$lines.Add("## Shared Corridor Runs")
$routeInfos |
    Where-Object { $_.Kind -eq "shared" } |
    Group-Object RunId |
    Sort-Object Name |
    ForEach-Object {
        $pointCounts = ($_.Group | ForEach-Object PointCount | Sort-Object -Unique) -join ", "
        $layers = ($_.Group | ForEach-Object SharedLayer | Sort-Object -Unique) -join ", "
        $families = ($_.Group | ForEach-Object Family | Sort-Object -Unique) -join ", "
        $lines.Add("- $($_.Name): elements=$($_.Count), pointCounts=[$pointCounts], layers=[$layers], emittingFamilies=[$families]")
    }
$lines.Add("")
$lines.Add("## Duplicate Geometry")
if ($duplicateGroups.Count -eq 0) {
    $lines.Add("- none detected by exact family+points match")
} else {
    foreach ($group in $duplicateGroups | Select-Object -First 40) {
        $indexes = ($group.Group | ForEach-Object Index) -join ", "
        $kinds = ($group.Group | ForEach-Object Kind | Sort-Object -Unique) -join ", "
        $lines.Add("- $($group.Name): count=$($group.Count), kinds=[$kinds], indexes=[$indexes]")
    }
}
$lines.Add("")
$lines.Add("## Near Endpoint Gaps")
if ($nearGaps.Count -eq 0) {
    $lines.Add("- none detected within $NearEndpointThreshold px")
} else {
    foreach ($gap in $nearGaps | Sort-Object Distance | Select-Object -First 60) {
        $lines.Add("- family=$($gap.Family), route $($gap.LeftIndex) $($gap.LeftEndpoint) to route $($gap.RightIndex) $($gap.RightEndpoint): $($gap.Distance.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)) px")
    }
}

Set-Content -Path $outputPath -Value $lines -Encoding UTF8
Write-Host "SVG render debug summary written to $outputPath"
