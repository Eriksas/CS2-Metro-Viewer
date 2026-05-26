# Phase 3C Memo - Schematic Lite Layout

Phase 3C is complete and validated. It is independent from the CS2 exporter and does not change the JSON schema.

## What Exists

- Renderer supports `geographic` and `schematic-lite`.
- `geographic` is the default.
- `schematic-lite` is render-time only:
  - starts from normalized station positions,
  - snaps station render points to a configurable grid,
  - places newly seen stops along horizontal, vertical, or 45-degree candidate segments.
- Default grid size is 32px.
- CLI supports:
  - `--layout geographic`
  - `--layout schematic-lite`
  - `--grid-size <number>`

## What Did Not Change

- `MetroExportDocument` schema did not change.
- `station.position` remains game/reference coordinates.
- `CS2 Metro\RealMetroJsonExporter.cs` was not changed.
- No style presets, in-game SVG preview, viewer app, or label hiding were added in Phase 3C.

## Validation

- `dotnet build CS2MetroDiagram.slnx --no-restore`
- `dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore`
- Sample and real export SVGs were generated in both layout modes.
- Generated SVG files parsed as valid XML.
