# Metro Track Geometry Discovery

Phase 5A.3 is diagnostic-only. It investigates whether CS2 subway `TransportLine` route segments expose the lower-level `Game.Net` track, lane, edge, or curve data needed to repair sparse geographic `line.pathPoints` later.

This phase does not change:

- `metro-export.json` schema,
- `line.stops`,
- existing `line.pathPoints` behavior,
- Viewer UI,
- CLI behavior,
- renderer behavior.

## Output Files

Use the existing in-game debug button:

```text
Options > CS2 Metro Diagram > Main > Debug > Export Transport Debug Dump
```

The button still writes the original transport debug files:

```text
D:\CS2MetroDiagram\debug-dump.json
D:\CS2MetroDiagram\debug-dump.txt
```

It now also writes:

```text
D:\CS2MetroDiagram\metro-track-geometry-debug.json
D:\CS2MetroDiagram\metro-track-geometry-debug.txt
```

## What Is Collected

For each recognized subway `TransportLine`, the track geometry debug output records:

- line entity id,
- line name,
- route number,
- waypoint count,
- route segment buffer presence,
- route segment count,
- sampled segment count,
- referenced `Game.Net` entity count,
- geometry-like field count,
- likely curve source candidates.

For each sampled `RouteSegment`, up to 10 per line, it records:

- segment entity id,
- component type names on the segment entity,
- entity reference fields from the RouteSegment item and relevant segment components,
- referenced `Game.Net` entities and their component type names,
- fields that look like geometry, including names containing `Bezier`, `Curve`, `position`, `start`, `end`, `node`, `edge`, `lane`, `track`, or `path`,
- per-segment warnings and exceptions.

The JSON file is intended for structured inspection. The TXT file is intended for fast manual reading.

## Manual Workflow

1. Build the CS2 mod.
2. Deploy the latest local mod output:

```powershell
scripts\deploy-local-mod.ps1
```

3. Restart Cities: Skylines II.
4. Load a city with subway lines.
5. Click `Export Transport Debug Dump`.
6. Open:

```text
D:\CS2MetroDiagram\metro-track-geometry-debug.txt
```

7. If needed, inspect the structured JSON:

```text
D:\CS2MetroDiagram\metro-track-geometry-debug.json
```

## What To Look For

Strong signs that real track geometry is recoverable:

- sampled segments reference entities with `Game.Net.*` components,
- likely candidates include `Game.Net.Edge`, `Game.Net.Lane`, `Game.Net.Track`, or curve-related components,
- geometry-like fields expose Bezier or curve values,
- referenced `Game.Net` entities expose start/end/node/edge/lane/track fields.

Weak or inconclusive signs:

- route segments only expose `Game.Routes.PathTargets`,
- no referenced `Game.Net` entities appear,
- geometry-like fields only contain sparse ready start/end positions,
- exceptions prevent reading segment components.

## Next Step After Discovery

If the debug output identifies a stable `Game.Net` curve source, the next phase should update only the CS2 exporter path point extraction logic. It should keep the existing optional `line.pathPoints` schema and continue preserving `line.stops` as the station sequence.
