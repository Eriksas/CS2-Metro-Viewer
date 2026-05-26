# Path Geometry Validation Results

Phase: 5A.3c - CurveElement PathPoints Validation

Validated export:

```text
D:\CS2MetroDiagram\metro-export.json
D:\CS2MetroDiagram\metro-export-diagnostics.txt
```

File timestamp:

```text
2026-05-28 10:03:36
```

## Summary

The latest real export contains 11 subway lines, 48 stations, 157 stops, 157 route segments, and 9739 exported `line.pathPoints`.

All exported path points in `metro-export.json` use:

```text
RouteSegment.CurveElement
```

No exported path points use:

```text
RouteSegment.PathTargets
```

Conclusion: `RouteSegment.CurveElement` is now the primary path geometry source for this real export.

## Per-Line Statistics

| Line | Stops | Route segments | PathPoints | Before cleanup | Cleaned pathPoints | CurveElements | CurveElement source count | PathTargets fallback count | CurveElement read failures |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 4号线 | 22 | 22 | 1169 | 1460 | 1169 | 292 | 1169 | 0 | 0 |
| 1号线 | 30 | 30 | 1409 | 1760 | 1409 | 352 | 1409 | 0 | 0 |
| 5号线 | 8 | 8 | 353 | 440 | 353 | 88 | 353 | 0 | 0 |
| 3号线 | 18 | 18 | 881 | 1100 | 881 | 220 | 881 | 0 | 0 |
| 10号线（机场快线-大站快车） | 8 | 8 | 1065 | 1330 | 1065 | 266 | 1065 | 0 | 0 |
| 2号线 | 23 | 23 | 1337 | 1670 | 1337 | 334 | 1337 | 0 | 0 |
| 6号线 | 6 | 6 | 257 | 320 | 257 | 64 | 257 | 0 | 0 |
| 10号线（机场快线-特快） | 4 | 4 | 1025 | 1280 | 1025 | 256 | 1025 | 0 | 0 |
| 7号线 | 8 | 8 | 297 | 370 | 297 | 74 | 297 | 0 | 0 |
| 10号线（机场快线-站站停） | 14 | 14 | 1081 | 1350 | 1081 | 270 | 1081 | 0 | 0 |
| 8号线 | 16 | 16 | 865 | 1080 | 865 | 216 | 865 | 0 | 0 |

Totals:

```text
lines: 11
stops: 157
route segments: 157
CurveElements: 2432
pathPoints before cleanup: 12160
cleaned pathPoints: 9739
CurveElement source count: 9739
PathTargets fallback count: 0
CurveElement read failures: 0
skipped path segments: 0
```

## Express Line Checks

### 10号线（机场快线-大站快车）

- Stops: 8
- Route segments: 8
- CurveElements: 266
- Exported pathPoints: 1065
- CurveElement source count: 1065
- PathTargets fallback count: 0
- CurveElement read failures: 0

SVG comparison:

```text
01-geographic-stops.svg: 8 route polyline points
02-geographic-pathpoints.svg: 1065 route polyline points
03-geographic-pathpoints-simplified.svg: 320 route polyline points
```

### 10号线（机场快线-特快）

- Stops: 4
- Route segments: 4
- CurveElements: 256
- Exported pathPoints: 1025
- CurveElement source count: 1025
- PathTargets fallback count: 0
- CurveElement read failures: 0

SVG comparison:

```text
01-geographic-stops.svg: 4 route polyline points
02-geographic-pathpoints.svg: 1025 route polyline points
03-geographic-pathpoints-simplified.svg: 308 route polyline points
```

## Generated Comparison Files

Generated with:

```powershell
scripts\generate-path-geometry-comparison.ps1 -InputJson D:\CS2MetroDiagram\metro-export.json -OutputDir artifacts\path-geometry-comparison
```

Outputs:

```text
artifacts\path-geometry-comparison\01-geographic-stops.svg
artifacts\path-geometry-comparison\02-geographic-pathpoints.svg
artifacts\path-geometry-comparison\03-geographic-pathpoints-simplified.svg
artifacts\path-geometry-comparison\04-schematic-lite.svg
```

## Conclusions

CurveElement is the primary source: yes.

Evidence:

- Every exported `line.pathPoints` source is `RouteSegment.CurveElement`.
- Total CurveElement source count is 9739.
- Total PathTargets source count is 0.
- No `curveElement fallback` diagnostics were present.
- No path segments were skipped.

Express-line fly-lines are considered resolved for the validated export.

Evidence:

- `10号线（机场快线-大站快车）` no longer renders from 8 stop-to-stop points when `--use-path-points` is enabled; it renders from 1065 CurveElement path points.
- `10号线（机场快线-特快）` no longer renders from 4 stop-to-stop points when `--use-path-points` is enabled; it renders from 1025 CurveElement path points.
- The simplified geographic output still keeps hundreds of path points for both express services, which is enough to follow the route corridor instead of drawing direct stop jumps.

No line is heavily falling back to PathTargets.

Evidence:

- PathTargets fallback count is 0 for every exported subway line.

## Notes

The analyzed diagnostics file does not include the newer Phase 5A.3b labels `curve sample point count` and `path targets fallback count`. The fallback result above is therefore derived from final `pathPoints.source` metadata in `metro-export.json` and the absence of `curveElement fallback` entries in diagnostics.

The analyzed export appears to use 5 points per CurveElement before cleanup. A re-export with the latest Phase 5A.3b build should include the newer diagnostics fields and may sample 8 points per readable Bezier, but the current validation already confirms CurveElement is the stable route source.
