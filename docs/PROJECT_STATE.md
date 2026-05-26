# Project State

## Current Phase

Phase 3C - Schematic Lite Layout

## Current Goal

Add an optional render-time schematic-lite SVG layout mode that keeps the original `MetroExportDocument` positions unchanged while making rendered routes more grid-aligned and octilinear.

## Completed

- Phase 3A passed with visual caveats.
- Phase 3B passed:
  - real CS2 metro data can be exported,
  - real network SVGs can be rendered,
  - real line colors and station names are preserved,
  - basic label placement and legend sorting are implemented,
  - output is valid SVG/XML.
- Preserved the Phase 2 `Export Test Metro JSON` button.
- Preserved the Phase 2.5 `Export Transport Debug Dump` button.
- Preserved the Phase 3A `Export Real Metro JSON` button and exporter behavior.
- Kept Core, Rendering, CLI, and Tests independent from CS2 game assemblies.
- Added render layout modes:
  - `geographic`: default Phase 3B behavior,
  - `schematic-lite`: optional grid/octilinear render-time layout.
- Added `SvgRenderOptions.LayoutMode`.
- Added configurable `SvgRenderOptions.GridSize`, defaulting to 32px.
- Added CLI parameters:
  - `--layout geographic`,
  - `--layout schematic-lite`,
  - `--grid-size <number>`.
- `schematic-lite` uses normalized station coordinates as its starting point, snaps station render positions to the grid, and tries to align consecutive stops to horizontal, vertical, or 45-degree segments.
- Added layout tests for grid snapping, route direction snapping, and SVG layout metadata.
- Generated geographic and schematic-lite SVGs for all sample JSONs.
- Generated real export SVGs when local `metro-export.json` is present:
  - `samples/generated-svg/metro-export.geographic.svg`,
  - `samples/generated-svg/metro-export.schematic-lite.svg`.

## Current Capability

- Can export real metro data from CS2.
- Can render real network SVG.
- Real line colors and station names are preserved.
- Basic label placement and legend sorting are implemented.
- Output is valid SVG/XML.
- Can render either geographic or schematic-lite SVG from the same JSON.

## Export Location Note

- Current real exported JSON files are expected under `D:\CS2MetroDiagram`.
- Local development copies may also exist in the repository root for CLI testing.

## In Progress

- Manual visual review of `metro-export.schematic-lite.svg`.

## Blocked

- None in code. Higher-quality schematic maps require later decisions about topology optimization, label hiding, and style presets.

## Known Issues

- Lines still use direct geographic/reference-coordinate connections in `geographic` mode.
- Dense urban center can still be crowded.
- `schematic-lite` is heuristic and does not do complex topology optimization or collision avoidance.
- No full schematic 0/45/90-degree layout engine yet.
- No style presets yet.
- No user-facing viewer app yet.
- No in-game SVG preview or mod-launched external process has been implemented.

## Next Actions

1. Visually inspect `samples/generated-svg/metro-export.geographic.svg`.
2. Visually inspect `samples/generated-svg/metro-export.schematic-lite.svg`.
3. Decide whether the next phase should improve schematic topology, add label hiding, or start a viewer app.

## Verification

- `dotnet build CS2MetroDiagram.slnx --no-restore`
- `dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore`
- Generated geographic and schematic-lite SVGs for all samples.
- Generated geographic and schematic-lite SVGs from the available real `metro-export.json`.
- Parsed all generated SVGs as XML.

## Design Decisions

- Phase 3C changes stay in Rendering/CLI/tests/docs; CS2 exporter behavior is unchanged.
- `MetroExportDocument` schema is unchanged. Original `station.position` remains game/reference coordinates.
- `schematic-lite` layout coordinates are calculated only during rendering.
- `geographic` remains the default layout mode.
