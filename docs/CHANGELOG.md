# Changelog

## v0.1.0-alpha.1 - 2026-05-26

First alpha release package for external testing.

### Added

- Real Cities: Skylines II metro/subway JSON export.
- `metro-export-diagnostics.txt` for real export troubleshooting.
- Offline JSON loader, validation, and fallback handling.
- SVG rendering for metro networks.
- Natural numeric legend sorting.
- Basic label placement and label-priority handling.
- `geographic` and `schematic-lite` renderer layout modes.
- CLI conversion from metro JSON to SVG.
- Local Windows Viewer for opening JSON, previewing SVG, changing render settings, and saving SVG.
- Viewer support for default export detection.
- Viewer settings persistence.
- Label strategy options for hiding generic and crowded station labels.
- Minimal English/Chinese Viewer UI.
- Self-contained Windows x64 Viewer packaging.
- Alpha release package structure with mod artifacts, Viewer, docs, samples, known issues, and feedback template.

### Known Limitations

- Metro/subway only.
- No offline save parsing.
- `schematic-lite` is intentionally simple and not a full schematic layout engine.
- Dense station labels can still overlap or feel crowded.
- Interchange grouping may not be perfect for every city.
- No PNG/PDF export.
- No in-game SVG preview.
- No game-to-Viewer launch integration.
