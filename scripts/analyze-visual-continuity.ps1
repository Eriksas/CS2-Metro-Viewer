param(
    [string]$InputSvg = "artifacts\path-geometry-comparison\14-geographic-corridor-express-on.svg",

    [string]$OutputReport = "artifacts\path-geometry-comparison\visual-continuity-summary.txt",

    [string]$OutputDebugSvg = "artifacts\path-geometry-comparison\visual-continuity-debug.svg",

    [double]$NearEndpointThreshold = 10,

    [double]$ShortRunThreshold = 32
)

$ErrorActionPreference = "Stop"

$inputPath = Resolve-Path -LiteralPath $InputSvg -ErrorAction Stop
$reportPath = [System.IO.Path]::GetFullPath($OutputReport)
$debugSvgPath = [System.IO.Path]::GetFullPath($OutputDebugSvg)

foreach ($path in @($reportPath, $debugSvgPath)) {
    $directory = [System.IO.Path]::GetDirectoryName($path)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
}

[xml]$xml = Get-Content -Path $inputPath.Path -Encoding UTF8
$svgText = Get-Content -Path $inputPath.Path -Encoding UTF8 -Raw
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

function Format-Number([double]$Value) {
    return $Value.ToString("0.###", [Globalization.CultureInfo]::InvariantCulture)
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

function Get-PolylineLength($Points) {
    if ($Points.Count -lt 2) {
        return 0
    }

    $length = 0.0
    for ($i = 1; $i -lt $Points.Count; $i++) {
        $length += Get-Distance $Points[$i - 1] $Points[$i]
    }

    return $length
}

function Escape-Xml([string]$Value) {
    return [System.Security.SecurityElement]::Escape($Value)
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
    $layer = Get-AttributeValue $element "data-corridor-run-layer"
    $kind = if ($isExpress) {
        "express"
    } elseif ($isShared) {
        "shared"
    } elseif ($skip) {
        "shared-fallback"
    } elseif ($layer -eq "normal-base" -or [string]::IsNullOrWhiteSpace($layer)) {
        "normal"
    } else {
        $layer
    }

    $length = Get-PolylineLength $points
    $routeInfos += [pscustomobject]@{
        Index = $i
        Kind = $kind
        Layer = $layer
        Family = $family
        RunId = Get-AttributeValue $element "data-shared-corridor-run-id"
        SharedLayer = Get-AttributeValue $element "data-shared-corridor-layer"
        ExpressFamily = Get-AttributeValue $element "data-express-family"
        Stroke = Get-AttributeValue $element "stroke"
        StrokeWidth = Get-StrokeWidth $element
        PointCount = $points.Count
        Length = $length
        Start = if ($points.Count -gt 0) { $points[0] } else { $null }
        End = if ($points.Count -gt 0) { $points[$points.Count - 1] } else { $null }
        Points = $points
        PointsText = Get-AttributeValue $element "points"
    }
}

$stationInfos = @($xml.SelectNodes('//*[local-name()="circle" and contains(@class, "station")]') | ForEach-Object {
    [pscustomobject]@{
        StationId = Get-AttributeValue $_ "data-station-id"
        Class = Get-AttributeValue $_ "class"
        X = [double]::Parse((Get-AttributeValue $_ "cx"), [Globalization.CultureInfo]::InvariantCulture)
        Y = [double]::Parse((Get-AttributeValue $_ "cy"), [Globalization.CultureInfo]::InvariantCulture)
        Radius = [double]::Parse((Get-AttributeValue $_ "r"), [Globalization.CultureInfo]::InvariantCulture)
    }
})

$nearGaps = New-Object System.Collections.Generic.List[object]
$visualBaseRuns = $routeInfos | Where-Object {
    $_.Kind -ne "express" -and $_.Start -and $_.End -and ($_.Kind -ne "shared" -or $_.SharedLayer -eq "corridor-base")
}
$nonExpressFamilies = $visualBaseRuns | Group-Object Family
foreach ($familyGroup in $nonExpressFamilies) {
    $items = @($familyGroup.Group)
    for ($i = 0; $i -lt $items.Count; $i++) {
        for ($j = $i + 1; $j -lt $items.Count; $j++) {
            $candidates = @(
                [pscustomobject]@{ Left = "end"; Right = "start"; Distance = Get-Distance $items[$i].End $items[$j].Start; A = $items[$i].End; B = $items[$j].Start },
                [pscustomobject]@{ Left = "start"; Right = "end"; Distance = Get-Distance $items[$i].Start $items[$j].End; A = $items[$i].Start; B = $items[$j].End },
                [pscustomobject]@{ Left = "start"; Right = "start"; Distance = Get-Distance $items[$i].Start $items[$j].Start; A = $items[$i].Start; B = $items[$j].Start },
                [pscustomobject]@{ Left = "end"; Right = "end"; Distance = Get-Distance $items[$i].End $items[$j].End; A = $items[$i].End; B = $items[$j].End }
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
                    A = $near.A
                    B = $near.B
                })
            }
        }
    }
}

$shortRuns = @($visualBaseRuns | Where-Object { $_.Length -gt 0 -and $_.Length -lt $ShortRunThreshold })
$styleOverlapRisks = @()
$baseWidths = @($routeInfos | Where-Object Kind -eq "normal" | ForEach-Object StrokeWidth | Sort-Object -Unique)
$sharedWidths = @($routeInfos | Where-Object Kind -eq "shared" | ForEach-Object StrokeWidth | Sort-Object -Unique)
$expressWidths = @($routeInfos | Where-Object Kind -eq "express" | ForEach-Object StrokeWidth | Sort-Object -Unique)
if ($baseWidths.Count -gt 1) {
    $styleOverlapRisks += "normal route widths are not uniform: $($baseWidths -join ', ')"
}

if ($sharedWidths.Count -gt 0 -and $baseWidths.Count -gt 0) {
    $maxSharedWidth = ($sharedWidths | Measure-Object -Maximum).Maximum
    $maxBaseWidth = ($baseWidths | Measure-Object -Maximum).Maximum
    if ($maxSharedWidth -gt $maxBaseWidth * 1.25) {
        $styleOverlapRisks += "shared corridor total width $maxSharedWidth is much larger than base route width $maxBaseWidth"
    }
}

if ($stationInfos.Count -gt 0 -and $baseWidths.Count -gt 0) {
    $baseWidth = ($baseWidths | Measure-Object -Maximum).Maximum
    $largeMarkers = @($stationInfos | Where-Object { $_.Radius -gt $baseWidth * 0.72 })
    if ($largeMarkers.Count -gt 0) {
        $styleOverlapRisks += "$($largeMarkers.Count) station markers have radius larger than 72% of base line width"
    }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Visual Continuity Debug Summary")
$lines.Add("")
$lines.Add("Input: $($inputPath.Path)")
$lines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$lines.Add("")
$lines.Add("## Stroke Counts")
$lines.Add("- route base stroke count: $(($routeInfos | Where-Object Kind -eq 'normal').Count)")
$lines.Add("- shared corridor stroke count: $(($routeInfos | Where-Object Kind -eq 'shared').Count)")
$lines.Add("- express stripe stroke count: $(($routeInfos | Where-Object Kind -eq 'express').Count)")
$lines.Add("- shared fallback stroke count: $(($routeInfos | Where-Object Kind -eq 'shared-fallback').Count)")
$lines.Add("")
$lines.Add("## Stroke Widths")
$routeInfos | Group-Object Kind,StrokeWidth | Sort-Object Name | ForEach-Object {
    $lines.Add("- $($_.Name): $($_.Count)")
}
$lines.Add("")
$lines.Add("## Display Family Runs")
$routeInfos |
    Where-Object { $_.Kind -ne "express" } |
    Group-Object Family |
    Sort-Object Name |
    ForEach-Object {
        $length = ($_.Group | Measure-Object Length -Sum).Sum
        $lines.Add("- $($_.Name): runs=$($_.Count), totalLength=$(Format-Number $length)")
    }
$lines.Add("")
$lines.Add("## Shared Corridor Runs")
$routeInfos |
    Where-Object { $_.Kind -eq "shared" } |
    Group-Object RunId |
    Sort-Object Name |
    ForEach-Object {
        $sample = $_.Group | Select-Object -First 1
        $length = ($_.Group | Measure-Object Length -Maximum).Maximum
        $pointCounts = ($_.Group | ForEach-Object PointCount | Sort-Object -Unique) -join ", "
        $layers = ($_.Group | ForEach-Object SharedLayer | Sort-Object -Unique) -join ", "
        $start = if ($sample.Start) { "$(Format-Number $sample.Start.X),$(Format-Number $sample.Start.Y)" } else { "(none)" }
        $end = if ($sample.End) { "$(Format-Number $sample.End.X),$(Format-Number $sample.End.Y)" } else { "(none)" }
        $lines.Add("- $($_.Name): elements=$($_.Count), pointCounts=[$pointCounts], length=$(Format-Number $length), layers=[$layers], start=$start, end=$end")
    }
$lines.Add("")
$lines.Add("## Station Marker Sizes")
if ($stationInfos.Count -eq 0) {
    $lines.Add("- none")
} else {
    $stationInfos | Group-Object Class,Radius | Sort-Object Name | ForEach-Object {
        $lines.Add("- $($_.Name): $($_.Count)")
    }
}
$lines.Add("")
$lines.Add("## Visual Continuity Risks")
if ($shortRuns.Count -eq 0 -and $nearGaps.Count -eq 0 -and $styleOverlapRisks.Count -eq 0) {
    $lines.Add("- none detected by current thresholds")
} else {
    foreach ($risk in $styleOverlapRisks) {
        $lines.Add("- style-layer overlap risk: $risk")
    }

    foreach ($run in $shortRuns | Sort-Object Length | Select-Object -First 60) {
        $lines.Add("- suspicious short fragment: index=$($run.Index), family=$($run.Family), kind=$($run.Kind), length=$(Format-Number $run.Length), points=$($run.PointCount)")
    }

    foreach ($gap in $nearGaps | Sort-Object Distance | Select-Object -First 80) {
        $lines.Add("- near-touching but not merged: family=$($gap.Family), route $($gap.LeftIndex) $($gap.LeftEndpoint) to route $($gap.RightIndex) $($gap.RightEndpoint), distance=$(Format-Number $gap.Distance)")
    }
}

Set-Content -Path $reportPath -Value $lines -Encoding UTF8

$overlay = New-Object System.Collections.Generic.List[string]
$overlay.Add('<g id="visual-continuity-debug" pointer-events="none">')
$overlay.Add('<style>.visual-debug-start{fill:#00a3ff;stroke:#ffffff;stroke-width:2}.visual-debug-end{fill:#ff7a00;stroke:#ffffff;stroke-width:2}.visual-debug-short{fill:none;stroke:#ff1744;stroke-width:5;stroke-linecap:round;stroke-linejoin:round;opacity:.72}.visual-debug-gap{stroke:#8a2be2;stroke-width:3;stroke-dasharray:8 5;opacity:.85}.visual-debug-label{font:700 18px Arial,sans-serif;fill:#111827;stroke:#ffffff;stroke-width:4;paint-order:stroke}</style>')
foreach ($route in $routeInfos | Where-Object { $_.Kind -ne "express" -and $_.Start -and $_.End }) {
    $id = if ([string]::IsNullOrWhiteSpace($route.RunId)) { "route-$($route.Index)" } else { $route.RunId }
    $label = "$id $($route.Family)"
    $overlay.Add("<circle class=`"visual-debug-start`" cx=`"$(Format-Number $route.Start.X)`" cy=`"$(Format-Number $route.Start.Y)`" r=`"7`" />")
    $overlay.Add("<circle class=`"visual-debug-end`" cx=`"$(Format-Number $route.End.X)`" cy=`"$(Format-Number $route.End.Y)`" r=`"7`" />")
    $overlay.Add("<text class=`"visual-debug-label`" x=`"$(Format-Number ($route.Start.X + 8))`" y=`"$(Format-Number ($route.Start.Y - 8))`">$(Escape-Xml $label)</text>")
}

foreach ($run in $shortRuns) {
    if ($run.PointsText) {
        $overlay.Add("<polyline class=`"visual-debug-short`" points=`"$(Escape-Xml $run.PointsText)`" />")
    }
}

foreach ($gap in $nearGaps) {
    $overlay.Add("<line class=`"visual-debug-gap`" x1=`"$(Format-Number $gap.A.X)`" y1=`"$(Format-Number $gap.A.Y)`" x2=`"$(Format-Number $gap.B.X)`" y2=`"$(Format-Number $gap.B.Y)`" />")
    $midX = ($gap.A.X + $gap.B.X) / 2
    $midY = ($gap.A.Y + $gap.B.Y) / 2
    $overlay.Add("<text class=`"visual-debug-label`" x=`"$(Format-Number ($midX + 6))`" y=`"$(Format-Number ($midY - 6))`">gap $(Format-Number $gap.Distance)</text>")
}

$overlay.Add('</g>')
$overlayText = ($overlay -join "`n")
if ($svgText -notmatch '</svg>\s*$') {
    throw "Input SVG does not end with </svg>."
}

$debugText = [regex]::Replace($svgText, '</svg>\s*$', "$overlayText`n</svg>")
Set-Content -Path $debugSvgPath -Value $debugText -Encoding UTF8

Write-Host "Visual continuity report written to $reportPath"
Write-Host "Visual continuity debug SVG written to $debugSvgPath"
