# Path Geometry Validation

Phase 5A.2c keeps the existing `line.pathPoints` JSON shape but changes how the CS2 exporter tries to produce those points. This page is the manual validation checklist for comparing stop-based lines against exported path geometry.

## Why pathPoints Exist

`line.stops` is the station sequence. It is good for station circles, labels, terminals, and interchange logic, but it is too sparse for drawing real track paths. Express or cross-stop services can jump from one served station to another and create fly-lines.

`line.pathPoints` is optional route geometry. It should contain intermediate points from CS2 route segments so geographic rendering can draw a closer approximation of the actual line corridor.

## Current Extraction Order

The exporter currently tries:

1. `RouteSegment.CurveElement`
2. `RouteSegment.PathElement`
3. `RouteSegment.PathTargets`

`CurveElement` is expected to expose `m_Curve=Colossal.Mathematics.Bezier4x3`; Phase 5A.3b samples that Bezier first. `PathTargets` is the fallback and may only provide start/end points.

## Deploy Latest Mod

Build the CS2 mod, then deploy the local mod output:

```powershell
scripts\deploy-local-mod.ps1
```

Restart Cities: Skylines II after deploying. CS2 can keep old mod DLLs loaded until the game restarts.

## Export Real Metro JSON

1. Launch Cities: Skylines II.
2. Load a city with metro/subway lines.
3. Open `Options > CS2 Metro Diagram > Main > Export`.
4. Click `Export Real Metro JSON`.
5. Confirm these files exist:

```text
D:\CS2MetroDiagram\metro-export.json
D:\CS2MetroDiagram\metro-export-diagnostics.txt
```

## Analyze the Export

Run:

```powershell
scripts\analyze-metro-export-json.ps1
```

Or pass an explicit file:

```powershell
scripts\analyze-metro-export-json.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json
```

Look for:

- `total pathPoints` greater than zero.
- Most lines having `pathPoints > stops: True`.
- Source summary containing `RouteSegment.CurveElement` or `RouteSegment.PathElement`.
- Warnings about no pathPoints or pathPoints count <= stops count.

Also open `metro-export-diagnostics.txt` and check the `Line PathPoints` section for:

- `curve element count`
- `curve sample point count`
- `path element count`
- `path targets fallback count`
- `pathPoints count before cleanup`
- `pathPoints count after cleanup`
- `path source summary`
- `first CurveElement read failures`
- `CurveElement m_Curve deep field dump`

## Generate Comparison SVGs

Run:

```powershell
scripts\generate-path-geometry-comparison.ps1
```

Or choose input/output paths:

```powershell
scripts\generate-path-geometry-comparison.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json -OutputDir artifacts\path-geometry-comparison
```

The script writes:

```text
artifacts\path-geometry-comparison\01-geographic-stops.svg
artifacts\path-geometry-comparison\02-geographic-pathpoints.svg
artifacts\path-geometry-comparison\03-geographic-pathpoints-simplified.svg
artifacts\path-geometry-comparison\04-schematic-lite.svg
```

## What to Compare

`01-geographic-stops.svg`: baseline stop-to-stop rendering. This is expected to show fly-lines for express/cross-stop service.

`02-geographic-pathpoints.svg`: raw exported path geometry without renderer simplification. Use this to see whether the exporter produced intermediate route points.

`03-geographic-pathpoints-simplified.svg`: path geometry with renderer cleanup enabled. It should be smoother than raw pathPoints without losing the route shape.

`04-schematic-lite.svg`: current schematic-lite baseline. It still uses stops by default and is included for overall readability comparison, not route geometry validation.

## Signs of Success

- `pathPoints` exist in the export.
- `pathPoints` count is greater than `stops` count for most real metro lines.
- `geographic + pathPoints` has fewer fly-lines than `geographic + stops`.
- Simplified pathPoints look smoother but not overly distorted.
- Diagnostics show `RouteSegment.CurveElement` or `RouteSegment.PathElement` contributing points.

## Signs More Investigation Is Needed

- All pathPoints still come from `RouteSegment.PathTargets`.
- pathPoints count is equal to or lower than stops count for most lines.
- Raw and simplified pathPoints look nearly identical to stops.
- Diagnostics show CurveElement fields but no sampled points.
