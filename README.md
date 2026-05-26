# CS2 Metro Diagram

CS2 Metro Diagram is a Cities: Skylines II metro diagram tool in early development.

Current release version: `v0.1.0-alpha.1`.

This is an alpha release, not a stable release. It is intended for early testing and feedback.

The current milestone is an alpha local Windows viewer that opens `metro-export.json`, previews the generated SVG, switches layout mode, adjusts render and label settings, supports basic English/Chinese UI text, and saves SVG output. The CS2 mod exports JSON only.

## Current Focus

- Phase 4C: alpha release preparation for `v0.1.0-alpha.1`.
- Core, Rendering, CLI, and Tests must remain independent from Cities: Skylines II game assemblies.
- Style presets, in-game SVG preview, PNG/PDF export, drag editing, and mod-launched external processes are intentionally postponed.

## Alpha Release Package

Build the alpha release folder and zip with:

```text
scripts\package-alpha-release.ps1
```

The script writes:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.1
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.1-win-x64.zip
```

Release package contents:

- `Mod`: current local CS2 mod artifacts, when available.
- `Viewer`: self-contained Windows x64 Viewer package.
- `docs`: project and release documentation.
- `samples`: sample metro JSON files.
- `README.md`, `QUICK_START.md`, `KNOWN_ISSUES.md`, `CHANGELOG.md`, `build-info.txt`.

Start with `QUICK_START.md` for tester instructions and `KNOWN_ISSUES.md` before reporting bugs.

## Viewer Usage

Development run:

```text
dotnet run --project src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj --no-restore
```

Publish a self-contained Windows x64 package:

```text
scripts\publish-viewer-self-contained.ps1
```

The self-contained package is written to:

```text
artifacts\viewer-win-x64-self-contained
```

Framework-dependent package:

```text
scripts\publish-viewer-framework-dependent.ps1
```

The framework-dependent package is written to:

```text
artifacts\viewer-win-x64-framework-dependent
```

For normal users:

1. Open the published package folder.
2. Double-click `MetroDiagram.Viewer.exe`.
3. Click `Open Default Export` if `D:\CS2MetroDiagram\metro-export.json` or `Documents\CS2MetroDiagram\metro-export.json` exists.
4. Otherwise click `Open JSON` and choose a sample JSON or real export.

Inside the viewer:

1. Click `Open JSON` or `Open Default Export`.
2. Switch `Layout` between `geographic` and `schematic-lite`.
3. Adjust width, height, legend width, padding, line width, station radius, label font size, or grid size.
4. Use label options to hide generic station names or crowded low-priority labels while keeping interchanges and terminals visible.
5. Switch `Language` between English and Chinese when needed.
6. Click `Refresh Preview`.
7. Click `Save SVG`.

Current real exports are expected under `D:\CS2MetroDiagram`, with `Documents\CS2MetroDiagram` also supported as a fallback. Viewer settings are saved to `Documents\CS2MetroDiagram\viewer-settings.json`.

## Offline Usage

After building the offline projects, convert one sample file with:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- samples\sample-metro-small.json output.svg
```

The generated SVG should include a city title, colored route lines, station dots, station labels, interchange markers, and a legend.

Optional render parameters:

```text
--layout geographic|schematic-lite --grid-size N --width N --height N --legend-width N --padding N --line-width N --station-radius N --label-font-size N --center-expansion --hide-generic-labels --hide-crowded-labels --always-show-interchanges --always-show-terminals
```

For a dense real export, a larger canvas can help:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.svg --width 1600 --height 1100 --label-font-size 11 --hide-generic-labels --hide-crowded-labels
```

Layout modes:

- `geographic`: default Phase 3B behavior using normalized source coordinates.
- `schematic-lite`: render-time layout that snaps stations to a grid and tries to make route segments horizontal, vertical, or 45-degree diagonal. It does not change the JSON data.

Render a real export both ways:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.geographic.svg --layout geographic
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.schematic-lite.svg --layout schematic-lite --grid-size 32
```

Label options:

- `--hide-generic-labels`: hides ordinary generic/default station labels such as `Station 1`, `Metro Station`, and common CS2 default subway station names.
- `--hide-crowded-labels`: hides low-priority labels when they overlap already placed higher-priority labels.
- `--always-show-interchanges`: keeps interchange labels visible even if other label filters are enabled.
- `--always-show-terminals`: keeps terminal labels visible even if other label filters are enabled.

To render every sample JSON into `samples/generated-svg/`:

```powershell
New-Item -ItemType Directory -Force samples\generated-svg | Out-Null
Get-ChildItem samples -Filter *.json | ForEach-Object {
  $out = Join-Path 'samples\generated-svg' ($_.BaseName + '.svg')
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $out
}
```

## Samples

- `sample-metro-small.json`: one simple line.
- `sample-metro-interchange.json`: two lines sharing one station.
- `sample-metro-branch.json`: a trunk and branch service.
- `sample-metro-loop.json`: a loop line plus a spur.
- `sample-metro-missing-fields.json`: missing names, missing color, blank city name, and one missing stop reference.
- `sample-metro-large-network.json`: five-line network with multiple interchanges.

## Verification

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj --no-restore
scripts\publish-viewer-self-contained.ps1
scripts\package-alpha-release.ps1
```
