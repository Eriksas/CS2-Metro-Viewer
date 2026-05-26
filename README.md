# CS2 Metro Diagram

CS2 Metro Diagram is a Cities: Skylines II metro diagram tool in early development.

The current milestone is a minimal real CS2 metro export plus an offline renderer that turns `metro-export.json` into a readable SVG. The mod exports JSON only; SVG rendering still happens through the CLI.

## Current Focus

- Phase 3C: optional Schematic Lite SVG layout.
- Core, Rendering, CLI, and Tests must remain independent from Cities: Skylines II game assemblies.
- Advanced schematic layout, style presets, in-game SVG preview, and mod-launched external processes are intentionally postponed.

## Offline Usage

After building the offline projects, convert one sample file with:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- samples\sample-metro-small.json output.svg
```

The generated SVG should include a city title, colored route lines, station dots, station labels, interchange markers, and a legend.

Optional render parameters:

```text
--layout geographic|schematic-lite --grid-size N --width N --height N --legend-width N --padding N --line-width N --station-radius N --label-font-size N --center-expansion
```

For a dense real export, a larger canvas can help:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.svg --width 1600 --height 1100 --label-font-size 11
```

Layout modes:

- `geographic`: default Phase 3B behavior using normalized source coordinates.
- `schematic-lite`: render-time layout that snaps stations to a grid and tries to make route segments horizontal, vertical, or 45-degree diagonal. It does not change the JSON data.

Render a real export both ways:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.geographic.svg --layout geographic
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.schematic-lite.svg --layout schematic-lite --grid-size 32
```

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
```
