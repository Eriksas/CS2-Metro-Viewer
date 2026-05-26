# CS2 Metro Diagram Viewer Quick Start

Version: `v0.1.0-alpha.2-candidate`

This is an alpha build, not a stable release.

## Run

Double-click `MetroDiagram.Viewer.exe`.

## Open JSON

Click `Open Default Export` if it is enabled, or click `Open JSON` and choose a metro JSON file.

Real CS2 exports are checked in this order:

```text
D:\CS2MetroDiagram\metro-export.json
Documents\CS2MetroDiagram\metro-export.json
```

A sample file is included in the `samples` folder.

`Open Export Folder` opens the export folder so you can find JSON and saved SVG files.

Latest real exports remain `metro-export.json`. Timestamped snapshots are written under the `exports` subdirectory and can be opened manually with `Open JSON`.

## Preview

The Viewer renders the SVG preview using the same renderer as the CLI.

Layout modes:

- `geographic`: keeps normalized source coordinate geometry.
- `schematic-lite`: snaps rendered stations to a grid and makes route segments more regular.

## Adjust

Change width, height, legend width, padding, line width, station radius, label font size, or grid size, then click `Refresh Preview`.

Use the label checkboxes to reduce clutter:

- `Hide generic station labels`
- `Hide crowded labels`
- `Always show interchanges`
- `Always show terminals`

Use `Language` to switch the Viewer UI between English and Chinese. Viewer settings are saved to:

```text
Documents\CS2MetroDiagram\viewer-settings.json
```

`Reset Defaults` restores render and label defaults while keeping the current language.

## Save

Click `Save SVG` and choose a destination path. The saved SVG can be opened in a browser.
