# Known Issues - v0.1.0-alpha.1

This is an alpha build. It is intended for testing and feedback, not production use.

## Current Limitations

- Only metro/subway networks are supported.
- Offline save parsing is not supported. You must load a city and export from inside Cities: Skylines II.
- `schematic-lite` is not a professional-grade automatic schematic layout.
- Station labels can still be crowded, especially in dense city centers.
- Interchange/station grouping may be imperfect when CS2 data uses unexpected station ownership or access-restriction structures.
- PNG and PDF export are not supported.
- The game mod does not launch the Viewer.
- The Viewer is a local Windows app, not an in-game preview.
- No Hong Kong, Guangzhou, or other style presets are included yet.
- No drag editing or manual label placement exists yet.

## Troubleshooting Notes

- If `Open Default Export` is disabled, confirm that `metro-export.json` exists under `D:\CS2MetroDiagram` or `Documents\CS2MetroDiagram`.
- If the diagram is too crowded, try `schematic-lite`, larger width/height, smaller label font size, `Hide generic station labels`, and `Hide crowded labels`.
- If export fails in-game, attach `metro-export-diagnostics.txt` and the game/mod log when reporting the issue.
