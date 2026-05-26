# Project State

## Current Phase

Phase 4C - Alpha Release Preparation

## Current Version

`v0.1.0-alpha.1`

This is an alpha release, not a stable release.

## Current Goal

Prepare the first external-test release package. Focus on versioning, package structure, quick-start docs, known issues, changelog, and feedback template. Do not add new core features, new layout algorithms, style presets, PNG/PDF export, drag editing, or in-game preview.

## Completed

- Phase 3A passed with visual caveats:
  - real CS2 metro data can be exported,
  - real line colors, station names, and line names are preserved,
  - real exports can be converted to SVG.
- Phase 3B passed:
  - basic label placement and legend sorting are implemented,
  - output is valid SVG/XML.
- Phase 3C passed:
  - `geographic` and `schematic-lite` layout modes exist,
  - `geographic` remains available,
  - `schematic-lite` is render-time only and does not change the JSON schema.
- Phase 4A and 4A.1 passed:
  - WPF Viewer exists under `src/MetroDiagram.Viewer`,
  - Viewer opens JSON through `MetroJsonLoader`,
  - Viewer renders SVG through `MetroSvgRenderer`,
  - Viewer can switch layout, adjust basic render settings, refresh preview, and save SVG,
  - self-contained and framework-dependent Viewer publish scripts exist.
- Phase 4B passed manual Viewer validation:
  - Viewer detects `D:\CS2MetroDiagram\metro-export.json` and `Documents\CS2MetroDiagram\metro-export.json`,
  - Viewer adds `Open Default Export`, `Open Export Folder`, and `Reset Defaults`,
  - Viewer saves settings to `Documents\CS2MetroDiagram\viewer-settings.json`,
  - Viewer supports minimal English / Chinese UI switching,
  - Rendering and CLI support generic/crowded label hiding options.
- Phase 4C preparation:
  - unified release version is `v0.1.0-alpha.1`,
  - Viewer window title includes the release version,
  - Core default `generator.version` uses `v0.1.0-alpha.1`,
  - CS2 test and real JSON exporters write `generator.version` as `v0.1.0-alpha.1`,
  - publish scripts include version, build time, and commit in `build-info.txt`,
  - added `docs/ALPHA_QUICK_START.md`,
  - added `docs/KNOWN_ISSUES.md`,
  - added `docs/FEEDBACK_TEMPLATE.md`,
  - added `docs/CHANGELOG.md`,
  - added `scripts/package-alpha-release.ps1`.
- Phase 4C verification completed:
  - offline solution build passed,
  - console tests passed,
  - Viewer self-contained publish passed,
  - CS2 mod build/post-process passed with local toolchain,
  - alpha release folder and zip were generated,
  - required zip entries were checked,
  - release Viewer exe started successfully in a short smoke test.

## Current Capability

- Can export real metro/subway data from CS2.
- Can render real network SVG by CLI.
- Can preview and save SVG through the local Windows Viewer.
- Can publish a self-contained win-x64 single-file Viewer package.
- Can assemble an alpha release folder and zip under `artifacts\releases`.
- Viewer can open the default real export with one button when it exists.
- Viewer can remember user settings between runs.
- Viewer can reduce default/generic station-name clutter.
- Basic label placement, legend sorting, and schematic-lite layout are available.

## Export Location Note

- Current real exported JSON files are expected under `D:\CS2MetroDiagram`.
- The Viewer also checks `Documents\CS2MetroDiagram\metro-export.json`.
- Viewer settings are stored at `Documents\CS2MetroDiagram\viewer-settings.json`.

## In Progress

- None in code. The `v0.1.0-alpha.1` package is ready for manual distribution checks.

## Blocked

- None in code.

## Known Issues

- Only metro/subway networks are supported.
- Offline save parsing is not supported; users must load a city and export from inside CS2.
- `schematic-lite` is intentionally simple and not a professional-grade automatic schematic layout.
- Labels can still be crowded.
- Interchange grouping may not be perfect for every city.
- No PNG/PDF export.
- No in-game SVG preview.
- The game mod does not launch the Viewer.
- See `docs/KNOWN_ISSUES.md` for the release-facing known issue list.

## Next Actions

1. Optionally do one final manual CS2 in-game export using the packaged mod.
2. Share `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.1-win-x64.zip` with alpha testers.
3. Collect feedback using `docs\FEEDBACK_TEMPLATE.md`.

## Verification

- `dotnet build CS2MetroDiagram.slnx --no-restore`
- `dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore`
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package-alpha-release.ps1`
- CS2 mod build/post-process with the local CS2 modding toolchain command documented in `docs\DEV_NOTES.md`.

## Design Decisions

- Phase 4C only synchronizes `generator.version` in the CS2 exporters; it does not change real exporter ECS reading logic.
- Viewer reuses Core loader and Rendering renderer; it does not copy SVG rendering logic.
- Viewer uses WPF built-in `WebBrowser` for embedded preview to avoid a WebView2 package dependency in this release.
- Viewer does not launch external programs from the CS2 mod.
- Alpha releases are packaged as a folder plus zip, not an installer.
- Viewer settings use a small JSON file in `Documents\CS2MetroDiagram`.
- Viewer bilingual UI uses a small in-process resource dictionary instead of a full i18n framework.
