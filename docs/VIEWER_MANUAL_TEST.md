# Viewer Manual Test

Use this checklist for Viewer manual validation. Phase 4B adds default-export discovery, saved settings, label filters, and minimal English/Chinese UI.

## Test Data

- Sample JSON: `samples\sample-metro-small.json`
- Real export JSON: `D:\CS2MetroDiagram\metro-export.json`
- Optional invalid JSON: create a temporary `.json` file containing `{ invalid json`
- Optional empty network JSON:

```json
{
  "schemaVersion": 1,
  "city": { "name": "Empty Manual Test" },
  "network": {
    "type": "metro",
    "stations": [],
    "lines": []
  }
}
```

## Checklist

- [ ] Start Viewer.
  - Development: `dotnet run --project src\MetroDiagram.Viewer\MetroDiagram.Viewer.csproj --no-restore`
  - Release package: double-click `MetroDiagram.Viewer.exe`.
- [ ] If a default export exists, confirm `Open Default Export` is enabled.
- [ ] Click `Open Export Folder`.
- [ ] Confirm the export folder opens.
- [ ] Switch `Language` to `中文`.
- [ ] Confirm main buttons, labels, status text, and checkbox text are translated.
- [ ] Switch `Language` back to `English`.
- [ ] Click `Open JSON`.
- [ ] Open `samples\sample-metro-small.json`.
- [ ] Confirm city name, line count, and station count are displayed.
- [ ] Confirm SVG preview appears.
- [ ] Open `D:\CS2MetroDiagram\metro-export.json`.
- [ ] Confirm the real city/network preview appears.
- [ ] Close and restart Viewer.
- [ ] Confirm the previous render settings and language were restored.
- [ ] If `Open Default Export` is enabled, click it.
- [ ] Confirm the real export opens without using the file picker.
- [ ] Switch layout from `geographic` to `schematic-lite`.
- [ ] Confirm preview updates.
- [ ] Switch layout back to `geographic`.
- [ ] Change width.
- [ ] Change height.
- [ ] Change label font size.
- [ ] Change grid size.
- [ ] Toggle `Hide generic station labels`.
- [ ] Confirm generic/default station names are reduced on dense real exports.
- [ ] Toggle `Hide crowded labels`.
- [ ] Confirm crowded low-priority labels are reduced while station circles remain visible.
- [ ] Confirm interchange and terminal labels remain visible when `Always show interchanges` and `Always show terminals` are enabled.
- [ ] Click `Refresh Preview`.
- [ ] Confirm the preview updates without crashing.
- [ ] Click `Save SVG`.
- [ ] Choose a save path.
- [ ] Confirm the saved SVG file exists.
- [ ] Open the saved SVG in a browser.
- [ ] Confirm the browser displays the diagram.
- [ ] Open an invalid JSON file.
- [ ] Confirm the Viewer shows a clear error and does not crash.
- [ ] Open an empty network JSON.
- [ ] Confirm the Viewer shows an empty network preview and does not crash.

## Pass Criteria

- Viewer starts normally.
- Sample JSON opens and previews.
- Real `metro-export.json` opens and previews.
- Layout switching works.
- Basic render parameters can be changed and refreshed.
- Settings persist across restart.
- Basic English/Chinese UI switching works.
- Generic/crowded label filters reduce label clutter without hiding station circles.
- Current SVG can be saved.
- Saved SVG opens in a browser.
- Invalid JSON and empty network cases do not crash the UI.
