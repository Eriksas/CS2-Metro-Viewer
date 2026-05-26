# Decision Log

## 2026-05-26 - Keep the first milestone offline

Decision: Phase 1 uses a standalone .NET offline solution with Core, Rendering, CLI, and Tests. The existing CS2 mod template remains untouched for a later exporter phase.

Reason: The development plan requires the sample JSON to SVG pipeline to work before any Cities: Skylines II exporter implementation begins.

## 2026-05-26 - Use raw coordinate normalization for v0.1 rendering

Decision: The Minimal SVG renderer normalizes station `x/z` coordinates into a fixed SVG canvas with margins and connects stations in each line's stop order.

Reason: Phase 1 explicitly avoids complex schematic layout and only needs a readable first SVG output.

## 2026-05-26 - Use a dependency-free console test runner for Phase 1

Decision: `MetroDiagram.Tests` is a console project that runs focused assertions and exits non-zero on failure.

Reason: The first milestone should stay offline and avoid NuGet test package dependencies while the repository foundation is still being established.

## 2026-05-26 - Keep Phase 1.5 entirely offline

Decision: Phase 1.5 adds more samples, fallback tests, and renderer checks without creating or modifying a CS2 exporter.

Reason: The offline JSON-to-SVG path should be reliable before any game API or real city data can complicate debugging.

## 2026-05-26 - Treat missing station references as non-fatal validation issues

Decision: Missing station references are reported with clear warnings and skipped by the renderer instead of failing the whole document.

Reason: Partially valid exported data should still produce a useful diagram, while the CLI and tests expose the data issue clearly.

## 2026-05-26 - Reserve fixed SVG space for the legend

Decision: The renderer reserves a right-side legend lane during coordinate normalization.

Reason: This keeps the legend from covering route geometry without introducing complex layout or collision avoidance in Phase 1.5.

## 2026-05-26 - Phase 2 exports static JSON only

Decision: The CS2 mod shell writes a static v0.1-compatible `test-export.json` from a settings button.

Reason: The phase goal is to verify the game mod pipeline, options entry, file writing, and logging before any real CS2 data discovery.

## 2026-05-26 - Use Documents as the first export location

Decision: Phase 2 writes `test-export.json` under `Documents\CS2MetroDiagram`.

Reason: The user can find the file without knowing CS2 internal paths, and the exact path is still logged for troubleshooting.

## 2026-05-26 - Do not read ECS or real city data in Phase 2

Decision: The exporter does not query transport lines, stations, ECS components, save files, or loaded city state.

Reason: Real data discovery belongs to a later phase after the static exporter shell is proven in-game.

## 2026-05-26 - Keep command-line CS2 toolchain path configurable

Decision: The mod csproj supports a `CsiiToolPath` MSBuild property while preserving the existing `CSII_TOOLPATH` environment variable behavior.

Reason: This allows local command-line verification in environments where the user-level toolchain variable is missing, without changing the offline solution.

## 2026-05-26 - Add debug dump before real metro export

Decision: Phase 2.5 adds a transport debug dump button and files, but still does not export real `metro.json`.

Reason: We need evidence of CS2's transport-related ECS components before choosing a stable real exporter design.

## 2026-05-26 - Keep debug scanning inside the CS2 mod project

Decision: `TransportDebugDumpExporter.cs` and `TransportDebugDumpModels.cs` live in the existing `CS2 Metro` mod project.

Reason: Core, Rendering, CLI, and tests must stay free of game assembly dependencies.

## 2026-05-26 - Use bounded best-effort reflection for ECS data

Decision: The dump scans candidate entities by component type keywords and reads component data reflectively with sample limits and exception capture.

Reason: This lets the project collect useful discovery data without hardcoding guessed ECS component names or risking a full dump failure from one unreadable component.

## 2026-05-26 - Emit both JSON and TXT debug dumps

Decision: The debug export writes compact structured JSON and a human-readable TXT report.

Reason: JSON is easier to inspect programmatically, while TXT is faster for a first manual pass.

## 2026-05-26 - Implement Phase 3A as a narrow real exporter

Decision: The first real exporter only reads confirmed `Game.Routes.TransportLine` data and writes `metro-export.json` plus `metro-export-diagnostics.txt`.

Reason: Phase 3A needs to prove the real CS2 data path while avoiding layout work, previews, style presets, and broader ECS exploration.

## 2026-05-26 - Keep real CS2 reads inside the mod project

Decision: `RealMetroJsonExporter.cs` lives in the `CS2 Metro` project, and Core/Rendering/CLI continue to consume only schema-compatible JSON.

Reason: The offline pipeline should remain buildable and testable without CS2 game assemblies.

## 2026-05-26 - Prefer diagnostics over schema expansion for exporter uncertainty

Decision: Station id sources, fallback names, fallback coordinates, skipped waypoints, and subway detection reasons are written to a sidecar diagnostics file instead of extending the JSON schema.

Reason: The existing CLI needs stable input, while Phase 3A still needs rich evidence for manual review and Phase 3 follow-up.

## 2026-05-26 - Keep Phase 3B out of the CS2 exporter

Decision: Basic readability improvements are implemented in Rendering/CLI, with no changes to real CS2 export behavior.

Reason: Phase 3A already proved the real data chain; Phase 3B should make the generated SVG easier to read without increasing game-side risk.

## 2026-05-26 - Sort legends by line names, not entity ids

Decision: Legend ordering extracts the first number from `line.name` and leaves non-numbered lines last.

Reason: Real exporter entity ids contain unrelated numbers, while displayed line names carry the user-facing route order.

## 2026-05-26 - Use heuristic label placement before schematic layout

Decision: Label placement tries eight positions and chooses the lowest-overlap candidate using approximate text bounds and station-circle obstacles.

Reason: This reduces dense center collisions without introducing advanced schematic layout or label hiding yet.

## 2026-05-26 - Keep center expansion opt-in

Decision: A conservative center expansion transform exists but is disabled by default.

Reason: It may help dense cores, but it distorts real coordinates and needs more visual review before becoming default behavior.

## 2026-05-26 - Add schematic-lite as a render-only layout mode

Decision: Phase 3C adds `geographic` and `schematic-lite` renderer modes without changing `MetroExportDocument` or the CS2 exporter.

Reason: The exported game coordinates should remain source data; schematic coordinates are presentation-only and can be recalculated with different options.

## 2026-05-26 - Keep geographic as the default layout

Decision: `geographic` remains the default CLI/rendering behavior.

Reason: Phase 3B outputs must stay usable and predictable unless the user explicitly asks for schematic-lite.

## 2026-05-26 - Use a simple grid walk for schematic-lite

Decision: Schematic-lite starts from normalized geographic coordinates, snaps to a configurable grid, and places newly seen stops using the nearest horizontal, vertical, or 45-degree segment candidate.

Reason: This produces visibly more regular route geometry without introducing a large graph-layout dependency or a complex topology optimizer.

## 2026-05-26 - Build Phase 4A as a WPF viewer

Decision: Add `MetroDiagram.Viewer` as a WPF desktop app that references Core and Rendering.

Reason: The target user needs a simple local Windows UI, while the existing offline libraries already contain the JSON loading and SVG rendering logic.

## 2026-05-26 - Use built-in WPF WebBrowser for the first embedded preview

Decision: Use WPF `WebBrowser.NavigateToString` instead of adding WebView2 in Phase 4A.

Reason: WebView2 would introduce an additional package dependency; the built-in control is sufficient for a local SVG preview milestone.

## 2026-05-26 - Package Viewer as folder outputs, not an installer

Decision: Phase 4A.1 adds PowerShell publish scripts that create framework-dependent and self-contained folder packages under `artifacts/`.

Reason: The phase needs a user-runnable exe package without taking on installer design, signing, update flows, or distribution infrastructure.

## 2026-05-26 - Keep self-contained package single-file

Decision: The self-contained win-x64 Viewer package uses `PublishSingleFile=true` and `IncludeNativeLibrariesForSelfExtract=true`.

Reason: A single exe is easier for normal users to run while still allowing package docs, sample JSON, and build metadata to sit next to it.

## 2026-05-26 - Keep Phase 4B label filtering render-only

Decision: Add generic and crowded label hiding as `SvgRenderOptions`, CLI flags, and Viewer controls without changing `MetroExportDocument` or the CS2 real exporter.

Reason: Label visibility is presentation policy. The exported JSON should keep the full network data so users can rerender with different settings.

## 2026-05-26 - Store Viewer settings in Documents

Decision: Save Viewer preferences to `Documents\CS2MetroDiagram\viewer-settings.json`.

Reason: The project already uses `CS2MetroDiagram` as a user-visible export folder, and a small JSON settings file is enough for the alpha Viewer without adding a configuration framework.

## 2026-05-26 - Use a small Viewer resource dictionary for bilingual UI

Decision: Implement English/Chinese Viewer UI text in `ViewerResources.cs`.

Reason: Phase 4B only needs a minimal bilingual UI, so a full i18n framework would be unnecessary overhead.

## 2026-05-26 - Package v0.1.0-alpha.1 as folder plus zip

Decision: Phase 4C creates `artifacts\releases\CS2MetroDiagram-v0.1.0-alpha.1` and `CS2MetroDiagram-v0.1.0-alpha.1-win-x64.zip`.

Reason: Alpha testers need one predictable folder structure with Mod, Viewer, docs, samples, and build metadata, without taking on installer work yet.

## 2026-05-26 - Synchronize exporter version without changing exporter behavior

Decision: Update CS2 exporter `generator.version` values to `v0.1.0-alpha.1` through a small mod-side version constant.

Reason: Release metadata should be consistent, but Phase 4C must not change the real exporter ECS reading logic.

## 2026-05-26 - Keep release docs separate from development notes

Decision: Add `ALPHA_QUICK_START.md`, `KNOWN_ISSUES.md`, `FEEDBACK_TEMPLATE.md`, and `CHANGELOG.md` as release-facing documents.

Reason: External testers need concise instructions and feedback guidance without reading the full development plan or internal project state.
