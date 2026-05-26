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

## Phase 4A Notes

- Added `src/MetroDiagram.Viewer` as a WPF app targeting `net8.0-windows`.
- The viewer references:
  - `MetroDiagram.Core`,
  - `MetroDiagram.Rendering`.
- The viewer does not reference or modify the CS2 mod project.
- The viewer uses:
  - `MetroJsonLoader.LoadFromFile` for JSON loading,
  - `MetroSvgRenderer.Render` for SVG generation,
  - WPF `OpenFileDialog` and `SaveFileDialog`,
  - WPF built-in `WebBrowser` with `NavigateToString` for embedded preview.
- WebView2 was not added because that would require an extra NuGet package. The built-in browser is enough for Phase 4A preview.
- `Open JSON` defaults to `D:\CS2MetroDiagram` when that directory exists.
- Invalid JSON clears the preview, disables save, and displays errors in the window.
- Render option parsing uses invariant culture and reports positive-number validation errors.
- Save writes the current SVG using UTF-8 without BOM.
- Because the WPF project queries local Windows SDK metadata, sandboxed build can fail with access denied for `C:\Users\17865\AppData\Local\Microsoft SDKs`; running the same build under normal permissions succeeds.

## Phase 4A Commands

```text
dotnet restore CS2MetroDiagram.slnx
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
dotnet build src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj --no-restore
```

## Phase 4A.1 Notes

- Added manual validation checklist: `docs/VIEWER_MANUAL_TEST.md`.
- Added package quick start: `docs/VIEWER_QUICK_START.md`.
- Added framework-dependent publish script:
  - `scripts/publish-viewer-framework-dependent.ps1`
  - output: `artifacts\viewer-win-x64-framework-dependent`
- Added self-contained publish script:
  - `scripts/publish-viewer-self-contained.ps1`
  - output: `artifacts\viewer-win-x64-self-contained`
- The self-contained script uses:

```text
dotnet publish src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

- The self-contained script runs an explicit win-x64 restore first because local `NuGet.Config` clears package sources and self-contained publish needs runtime packs.
- Runtime pack restore uses `https://api.nuget.org/v3/index.json`.
- During validation, NuGet package download showed transient EOF/SSL retry messages, but restore eventually succeeded and the package was produced.
- Both package scripts copy:
  - `docs\VIEWER_QUICK_START.md` as package `README.md`,
  - `samples\sample-metro-small.json`,
  - generated `build-info.txt`.
- The self-contained package produced `MetroDiagram.Viewer.exe`.

## Phase 4A.1 Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-framework-dependent.ps1
```

## Phase 4B Notes

- The CS2 real exporter was not modified for Phase 4B.
- The Viewer now checks these default export files on startup:
  - `D:\CS2MetroDiagram\metro-export.json`,
  - `Documents\CS2MetroDiagram\metro-export.json`.
- The Viewer does not auto-open a default export; it enables `Open Default Export` when one exists.
- `Open Export Folder` opens the best available export folder from the default export, current JSON path, `D:\CS2MetroDiagram`, or `Documents\CS2MetroDiagram`.
- Viewer settings are stored as JSON at:

```text
Documents\CS2MetroDiagram\viewer-settings.json
```

- Saved settings include:
  - last opened JSON path,
  - layout mode,
  - width, height, legend width, padding,
  - line width, station radius, label font size, grid size,
  - label strategy options,
  - language.
- The Viewer has a minimal `English` / `中文` language selector implemented through `ViewerResources.cs`.
- No full i18n framework was added.
- `Reset Defaults` restores render and label defaults while keeping the current language and last JSON path.
- Rendering label strategy options added:
  - `HideGenericStationLabels`,
  - `HideCrowdedLabels`,
  - `AlwaysShowInterchanges`,
  - `AlwaysShowTerminals`.
- Generic station names are detected by `StationLabelClassifier`.
- Current generic/fallback detection includes:
  - `小型地铁广场`,
  - `现代地铁站`,
  - `地下地铁站`,
  - `地铁站`,
  - `Subway Station`,
  - `Metro Station`,
  - `Station 1`, `Station 2`, and other `Station <number>` fallback labels.
- Label hiding only affects text labels; station circles are always rendered.
- `HideCrowdedLabels` hides lower-priority labels when their chosen bounding box seriously overlaps already placed higher-priority labels.
- Interchanges and terminals are protected by default.
- CLI options added:

```text
--hide-generic-labels --hide-crowded-labels --always-show-interchanges --always-show-terminals
```

## Phase 4B Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1
```

## Phase 4C Notes

- Release version is `v0.1.0-alpha.1`.
- Added `Directory.Build.props` so SDK project assembly/package metadata is aligned with the alpha version.
- Added `MetroDiagramAppInfo.Version` in Core for shared offline version text.
- Viewer window title now appends `v0.1.0-alpha.1`.
- Core default `GeneratorInfo.Version` now uses `v0.1.0-alpha.1`.
- CS2 mod `VersionInfo.ReleaseVersion` is used for:
  - `RealMetroJsonExporter` `generator.version`,
  - `TestMetroJsonExporter` `generator.version`,
  - `TransportDebugDumpExporter.dumpVersion`.
- The CS2 real exporter's ECS reading logic was not changed.
- Sample JSON `generator.version` values were updated to `v0.1.0-alpha.1`.
- Added release-facing docs:
  - `docs/ALPHA_QUICK_START.md`,
  - `docs/KNOWN_ISSUES.md`,
  - `docs/FEEDBACK_TEMPLATE.md`,
  - `docs/CHANGELOG.md`.
- Publish scripts now include `Version`, `BuiltAtUtc`, and `Commit` in `build-info.txt`.
- Added release package script:

```text
scripts\package-alpha-release.ps1
```

- Release package output:

```text
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.1
artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.1-win-x64.zip
```

- Package script workflow:
  - build `CS2MetroDiagram.slnx`,
  - run `MetroDiagram.Tests`,
  - publish Viewer self-contained,
  - copy Viewer artifacts,
  - copy current `artifacts\cs2-local-mods` into `Mod` when available,
  - copy docs and sample JSON files,
  - generate release `build-info.txt`,
  - zip the release folder.
- For the final Phase 4C package, the CS2 mod artifacts were rebuilt with the local CS2 modding toolchain before rerunning the package script, so the copied `Mod` folder contains the synced `v0.1.0-alpha.1` version string.
- Release package smoke checks performed:
  - `Mod\CS2 Metro\CS2 Metro.dll` contains `v0.1.0-alpha.1`,
  - release `Viewer\MetroDiagram.Viewer.exe` starts and can be closed,
  - zip contains root README, quick start, known issues, changelog, build info, Viewer exe, Mod DLL, and sample JSON.

## Phase 4C Commands

```text
dotnet build CS2MetroDiagram.slnx --no-restore
dotnet run --project src\MetroDiagram.Tests\MetroDiagram.Tests.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-viewer-self-contained.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package-alpha-release.ps1
```

CS2 mod artifact rebuild command used before final packaging:

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
