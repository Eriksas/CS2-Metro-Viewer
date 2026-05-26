# Development Notes

## 2026-05-26

- Started with the existing Cities: Skylines II mod template under `CS2 Metro/`.
- Kept game-specific exporter work out of Phase 0 and Phase 1.
- Created the offline solution under `src/` so Core, Rendering, CLI, and Tests can build without CS2 game assemblies.
- `rg` could not run in this environment because access was denied, so file discovery used PowerShell instead.
- `dotnet new` and `dotnet restore` attempted to read user-level NuGet configuration. A local `NuGet.Config` was added, and restore was completed with explicit configuration.
- The first CLI acceptance command generated `output.svg` from `samples/sample-metro-small.json`.
- The current test project is a simple console test runner instead of xUnit/NUnit/MSTest to avoid external package dependencies during Phase 1.

## Phase 1.5 Notes

- Added samples for branch, loop, missing fields, and a larger five-line network.
- Missing station references are reported with `Missing station reference: ...` and skipped during rendering; this is intentionally non-fatal for now.
- SVG renderer reserves a fixed right-side legend lane by shrinking the coordinate normalization area.
- Generated sample SVGs are written to `samples/generated-svg/` and ignored by `.gitignore`.
- The console tests parse rendered SVG with `System.Xml.Linq.XDocument` to verify the output is legal XML/SVG.

## Phase 1.5 Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

Render every sample:

```powershell
New-Item -ItemType Directory -Force samples\generated-svg | Out-Null
Get-ChildItem samples -Filter *.json | ForEach-Object {
  $out = Join-Path 'samples\generated-svg' ($_.BaseName + '.svg')
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $out
}
```

## Phase 2 Notes

- Used the official local `ColossalOrder.ModTemplate.1.0.0.nupkg` template as the source for settings button patterns.
- The settings UI pattern is `ModSetting`, `RegisterInOptionsUI()`, localization entries, and a bool property with `[SettingsUIButton]`.
- Added `CS2 Metro\Setting.cs` for the options entry and `CS2 Metro\TestMetroJsonExporter.cs` for static JSON export.
- The exporter writes to `Environment.SpecialFolder.MyDocuments\CS2MetroDiagram\test-export.json`; if Documents cannot be resolved, it falls back to the user profile and then temp path.
- The mod logs the export directory on load and logs start/success/failure for every export attempt.
- The local shell did not have `CSII_TOOLPATH` configured, so `CS2 Metro.csproj` now supports `/p:CsiiToolPath=...`.
- The CS2 mod build also needed an explicit `Colossal.Localization` reference once settings localization was added.
- I did not launch CS2 from this environment; in-game load/button verification remains a manual step.

## Phase 2 Build Command Used

```powershell
$tool = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain'
$managed = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
$userData = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II'
$unityProject = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\mods\Cities Skylines II\.cache\Modding\UnityModsProject'
$post = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\.ModdingToolchain\ModPostProcessor\ModPostProcessor.exe'
$mscorlib = 'E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed\mscorlib.dll'
$localMods = 'E:\CS2\CS2 Metro\artifacts\cs2-local-mods'
dotnet build "CS2 Metro.slnx" --no-restore /p:CsiiToolPath="$tool" /p:ManagedPath="$managed" /p:UserDataPath="$userData" /p:UnityModProjectPath="$unityProject" /p:ModPostProcessorPath="$post" /p:EntitiesVersion="1.3.10" /p:MSCORLIBPath="$mscorlib" /p:LocalModsPath="$localMods"
```

## Phase 2 Manual Test

1. Build/deploy the `CS2 Metro` mod from Visual Studio or the CS2 toolchain.
2. Start Cities: Skylines II and enable/load the mod.
3. Open `Options > CS2 Metro Diagram > Main > Export`.
4. Click `Export Test Metro JSON`.
5. Check the mod log for `Export Test Metro JSON succeeded`.
6. Open `Documents\CS2MetroDiagram\test-export.json`.
7. Convert it with `dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- <test-export.json> <output.svg>`.

## Phase 2.5 Notes

- Added a second settings group: `Main > Debug`.
- Added `Export Transport Debug Dump` as a button using `[SettingsUIButton]`.
- `TransportDebugDumpExporter` stores all ECS/game-specific scanning logic inside the CS2 mod project.
- Dump output paths:
  - `Documents\CS2MetroDiagram\debug-dump.json`
  - `Documents\CS2MetroDiagram\debug-dump.txt`
- The dump scans all entities through `UpdateSystem.EntityManager.GetAllEntities(Allocator.Temp)`.
- Candidate entities are selected when one or more component type names contain keywords such as `transport`, `line`, `route`, `stop`, `station`, `metro`, or `subway`.
- For each candidate component type, the dump keeps the total entity count but caps detailed samples at 20.
- Component value reads are best-effort:
  - zero-sized components are recorded as tags,
  - buffers include length and up to 5 sample elements,
  - fields/properties with names like `name`, `color`, `position`, `route`, `stop`, `station`, `type`, and `mode` are recorded when readable,
  - exceptions are captured into the dump and logged where appropriate.
- The dump also records game/world status through `GameManager.instance` and `UpdateSystem.World`.
- No real metro export or `MetroExportDocument` mapping was added.

## Phase 2.5 Verification

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

CS2 mod build/post-process was also run with the same local toolchain property command documented in Phase 2.

## Phase 3A Notes

- Added `Export Real Metro JSON` under `Options > CS2 Metro Diagram > Main > Export`.
- Output paths:
  - `Documents\CS2MetroDiagram\metro-export.json`
  - `Documents\CS2MetroDiagram\metro-export-diagnostics.txt`
- `RealMetroJsonExporter` keeps all CS2 ECS reads inside the `CS2 Metro` mod project.
- The exporter queries entities with `Game.Routes.TransportLine`.
- Subway line detection uses:
  - `PrefabRef -> Game.Prefabs.TransportLineData.m_TransportType == Subway`,
  - `Game.Routes.VehicleModel[0].m_PrimaryPrefab -> Game.Prefabs.PublicTransportVehicleData.m_TransportType == Subway`,
  - or any route waypoint connected to `Game.Routes.SubwayStop`.
- Line data:
  - `line.id` comes from the transport line entity id,
  - `line.name` uses the CS2 name system when possible and falls back to `Metro Line {RouteNumber}`,
  - `line.color` uses `Game.Routes.Color.m_Color` and otherwise a fixed palette,
  - `line.mode` is always `metro`,
  - `line.stops` follows the `Game.Routes.RouteWaypoint` buffer order.
- Station data:
  - route waypoint entity comes from `RouteWaypoint.m_Waypoint`,
  - connected stop comes from `Game.Routes.Connected.m_Connected`,
  - stop validity checks `SubwayStop` or `Game.Routes.TransportStop`,
  - station id source priority is `TransportStop.m_AccessRestriction`, `Game.Common.Owner`, `Game.Objects.Attached.m_Parent`, connected stop, then waypoint,
  - position source priority is station group transform, stop transform, stop route position, waypoint transform, waypoint route position, then zero fallback,
  - station names are best-effort through the CS2 name system and fall back to `Station {n}`.
- The diagnostics file records transport line count, subway line count, route number, color source/value, waypoint counts, skipped waypoint reasons, station id sources, fallback names, and fallback coordinates.
- No-world and no-metro cases intentionally emit an empty `network.stations` and `network.lines` document.
- Existing `Export Test Metro JSON` and `Export Transport Debug Dump` entries are unchanged.
- The exporter does not launch the CLI or any external executable.

## Phase 3A Verification

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

CS2 mod build/post-process was also run with the same local toolchain property command documented in Phase 2 and succeeded with 0 warnings and 0 errors.

## Phase 3B Notes

- The CS2 real exporter was not modified for Phase 3B.
- `SvgRenderOptions` now exposes:
  - `Width`,
  - `Height`,
  - `Padding`,
  - `LegendWidth`,
  - `LineWidth`,
  - `StationRadius`,
  - `InterchangeStationRadius`,
  - `LabelFontSize`,
  - `EnableCenterExpansion`.
- CLI usage remains compatible with the old two-argument form:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- <input.json> <output.svg>
```

- Optional CLI parameters added:

```text
--width N --height N --legend-width N --padding N --line-width N --station-radius N --label-font-size N --center-expansion
```

- Legend sorting:
  - extracts the first numeric sequence from `line.name`,
  - sorts numbered lines before non-numbered lines,
  - keeps same-number ties stable.
- Label placement v1:
  - places labels in priority order,
  - tries eight candidate positions,
  - estimates width from ASCII/CJK character weights,
  - scores overlap against previously placed labels and station circle boxes,
  - writes `data-label-position` for quick SVG inspection.
- Label priority currently boosts interchange stations and line terminals, then named stations.
- Fallback-style station names like `Station 1` are lower priority, but still rendered.
- Center expansion is implemented as a conservative radial transform around the source coordinate center, but it is disabled by default.
- Real SVG validation in PowerShell needs `Get-Content -Encoding UTF8` because generated SVGs are UTF-8 without BOM.
- XML escaping now filters control characters and unpaired surrogate code units from labels/titles/attributes.

## Phase 3B Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
```

Render every sample:

```powershell
New-Item -ItemType Directory -Force samples\generated-svg | Out-Null
Get-ChildItem samples -Filter *.json | ForEach-Object {
  $out = Join-Path 'samples\generated-svg' ($_.BaseName + '.svg')
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $out
}
```

Render the available real export:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.svg
```

## Phase 3C Notes

- The CS2 real exporter was not modified for Phase 3C.
- `MetroExportDocument` schema was not changed; layout coordinates are render-only.
- Current real exported JSON files are expected under `D:\CS2MetroDiagram`; repository-root `metro-export.json` is only a local CLI test copy when present.
- Added `SvgLayoutMode`:
  - `Geographic`,
  - `SchematicLite`.
- `SvgRenderOptions` now includes:
  - `LayoutMode`,
  - `GridSize`, default `32`.
- `geographic` is the default and preserves Phase 3B coordinate normalization.
- `schematic-lite` flow:
  - starts from normalized geographic canvas coordinates,
  - snaps station render points to the configured grid,
  - walks each line's stop order,
  - places newly encountered stations relative to the previous stop using the nearest horizontal, vertical, or 45-degree endpoint candidate,
  - clamps render positions inside the route drawing area while keeping grid-aligned bounds when possible.
- Shared stations keep their first placed schematic position. This is intentionally simple and avoids topology optimization.
- Route `<g id="routes">` now includes `data-layout="geographic"` or `data-layout="schematic-lite"` for inspection and tests.
- CLI options added:

```text
--layout geographic
--layout schematic-lite
--grid-size <number>
```

## Phase 3C Commands

Render samples in both layout modes:

```powershell
New-Item -ItemType Directory -Force samples\generated-svg | Out-Null
Get-ChildItem samples -Filter *.json | ForEach-Object {
  $geo = Join-Path 'samples\generated-svg' ($_.BaseName + '.geographic.svg')
  $schematic = Join-Path 'samples\generated-svg' ($_.BaseName + '.schematic-lite.svg')
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $geo --layout geographic
  dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- $_.FullName $schematic --layout schematic-lite --grid-size 32
}
```

Render a real export in both layout modes:

```text
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.geographic.svg --layout geographic
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- metro-export.json samples\generated-svg\metro-export.schematic-lite.svg --layout schematic-lite --grid-size 32
```
