namespace MetroDiagram.Viewer;

public static class ViewerResources
{
    private static readonly Dictionary<string, Dictionary<string, string>> Resources = new(StringComparer.Ordinal)
    {
        ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WindowTitle"] = "CS2 Metro Diagram Viewer",
            ["OpenJson"] = "Open JSON",
            ["OpenDefaultExport"] = "Open Default Export",
            ["OpenExportFolder"] = "Open Export Folder",
            ["ResetDefaults"] = "Reset Defaults",
            ["RefreshPreview"] = "Refresh Preview",
            ["SaveSvg"] = "Save SVG",
            ["Layout"] = "Layout",
            ["Language"] = "Language",
            ["Width"] = "Width",
            ["Height"] = "Height",
            ["Legend"] = "Legend",
            ["Padding"] = "Padding",
            ["Line"] = "Line",
            ["Station"] = "Station",
            ["Label"] = "Label",
            ["Grid"] = "Grid",
            ["HideGeneric"] = "Hide generic station labels",
            ["HideCrowded"] = "Hide crowded labels",
            ["AlwaysInterchanges"] = "Always show interchanges",
            ["AlwaysTerminals"] = "Always show terminals",
            ["NoJsonLoaded"] = "No JSON loaded",
            ["JsonLoadedShort"] = "JSON loaded",
            ["UnnamedCity"] = "Unnamed City",
            ["SummaryEmpty"] = "City: -   Lines: -   Stations: -",
            ["Summary"] = "City: {0}   Lines: {1}   Stations: {2}",
            ["Ready"] = "Ready",
            ["InitialPreview"] = "Open a metro JSON file to preview.",
            ["DefaultFound"] = "Default export found: {0}",
            ["DefaultMissing"] = "No default export found.",
            ["OpenMetroJsonTitle"] = "Open metro JSON",
            ["SaveSvgTitle"] = "Save SVG",
            ["JsonLoaded"] = "JSON loaded.",
            ["JsonLoadedWarnings"] = "JSON loaded with warnings: {0}",
            ["JsonLoadFailed"] = "JSON load failed.",
            ["InvalidJson"] = "Invalid JSON: {0}",
            ["RenderFirst"] = "Open a metro JSON file before rendering.",
            ["RenderFailed"] = "Render failed: {0}",
            ["Rendered"] = "Preview rendered using {0}.",
            ["RenderedWarnings"] = "Preview rendered with warnings: {0}",
            ["NoSvgToSave"] = "No SVG preview is available to save.",
            ["SvgSaved"] = "SVG saved to {0}",
            ["SaveFailed"] = "Could not save SVG: {0}",
            ["OpenFolderFailed"] = "Could not open export folder: {0}",
            ["SettingsSaved"] = "Settings saved.",
            ["SettingsSaveFailed"] = "Could not save settings: {0}",
            ["DefaultsReset"] = "Defaults restored.",
            ["PositiveInteger"] = "{0} must be a positive integer.",
            ["PositiveNumber"] = "{0} must be a positive number.",
            ["JsonCouldNotLoad"] = "JSON could not be loaded."
        },
        ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WindowTitle"] = "\u0043\u0053\u0032 \u5730\u94c1\u56fe Viewer",
            ["OpenJson"] = "\u6253\u5f00 JSON",
            ["OpenDefaultExport"] = "\u6253\u5f00\u9ed8\u8ba4\u5bfc\u51fa",
            ["OpenExportFolder"] = "\u6253\u5f00\u5bfc\u51fa\u6587\u4ef6\u5939",
            ["ResetDefaults"] = "\u91cd\u7f6e\u9ed8\u8ba4\u503c",
            ["RefreshPreview"] = "\u5237\u65b0\u9884\u89c8",
            ["SaveSvg"] = "\u4fdd\u5b58 SVG",
            ["Layout"] = "\u5e03\u5c40",
            ["Language"] = "\u8bed\u8a00",
            ["Width"] = "\u5bbd\u5ea6",
            ["Height"] = "\u9ad8\u5ea6",
            ["Legend"] = "\u56fe\u4f8b\u5bbd\u5ea6",
            ["Padding"] = "\u8fb9\u8ddd",
            ["Line"] = "\u7ebf\u5bbd",
            ["Station"] = "\u7ad9\u70b9\u534a\u5f84",
            ["Label"] = "\u6807\u7b7e\u5b57\u53f7",
            ["Grid"] = "\u7f51\u683c",
            ["HideGeneric"] = "\u9690\u85cf\u9ed8\u8ba4\u7ad9\u540d",
            ["HideCrowded"] = "\u9690\u85cf\u62e5\u6324\u6807\u7b7e",
            ["AlwaysInterchanges"] = "\u59cb\u7ec8\u663e\u793a\u6362\u4e58\u7ad9",
            ["AlwaysTerminals"] = "\u59cb\u7ec8\u663e\u793a\u7aef\u70b9\u7ad9",
            ["NoJsonLoaded"] = "\u5c1a\u672a\u6253\u5f00 JSON",
            ["JsonLoadedShort"] = "JSON \u5df2\u52a0\u8f7d",
            ["UnnamedCity"] = "\u672a\u547d\u540d\u57ce\u5e02",
            ["SummaryEmpty"] = "\u57ce\u5e02\uff1a-   \u7ebf\u8def\uff1a-   \u7ad9\u70b9\uff1a-",
            ["Summary"] = "\u57ce\u5e02\uff1a{0}   \u7ebf\u8def\uff1a{1}   \u7ad9\u70b9\uff1a{2}",
            ["Ready"] = "\u5c31\u7eea",
            ["InitialPreview"] = "\u6253\u5f00 metro JSON \u540e\u53ef\u9884\u89c8\u3002",
            ["DefaultFound"] = "\u627e\u5230\u9ed8\u8ba4\u5bfc\u51fa\uff1a{0}",
            ["DefaultMissing"] = "\u672a\u627e\u5230\u9ed8\u8ba4\u5bfc\u51fa\u3002",
            ["OpenMetroJsonTitle"] = "\u6253\u5f00 metro JSON",
            ["SaveSvgTitle"] = "\u4fdd\u5b58 SVG",
            ["JsonLoaded"] = "JSON \u5df2\u52a0\u8f7d\u3002",
            ["JsonLoadedWarnings"] = "JSON \u5df2\u52a0\u8f7d\uff0c\u8b66\u544a\uff1a{0}",
            ["JsonLoadFailed"] = "JSON \u52a0\u8f7d\u5931\u8d25\u3002",
            ["InvalidJson"] = "JSON \u65e0\u6548\uff1a{0}",
            ["RenderFirst"] = "\u8bf7\u5148\u6253\u5f00 metro JSON \u518d\u6e32\u67d3\u3002",
            ["RenderFailed"] = "\u6e32\u67d3\u5931\u8d25\uff1a{0}",
            ["Rendered"] = "\u5df2\u4f7f\u7528 {0} \u6e32\u67d3\u9884\u89c8\u3002",
            ["RenderedWarnings"] = "\u9884\u89c8\u5df2\u6e32\u67d3\uff0c\u8b66\u544a\uff1a{0}",
            ["NoSvgToSave"] = "\u5f53\u524d\u6ca1\u6709\u53ef\u4fdd\u5b58\u7684 SVG \u9884\u89c8\u3002",
            ["SvgSaved"] = "SVG \u5df2\u4fdd\u5b58\u5230 {0}",
            ["SaveFailed"] = "\u65e0\u6cd5\u4fdd\u5b58 SVG\uff1a{0}",
            ["OpenFolderFailed"] = "\u65e0\u6cd5\u6253\u5f00\u5bfc\u51fa\u6587\u4ef6\u5939\uff1a{0}",
            ["SettingsSaved"] = "\u8bbe\u7f6e\u5df2\u4fdd\u5b58\u3002",
            ["SettingsSaveFailed"] = "\u65e0\u6cd5\u4fdd\u5b58\u8bbe\u7f6e\uff1a{0}",
            ["DefaultsReset"] = "\u5df2\u6062\u590d\u9ed8\u8ba4\u503c\u3002",
            ["PositiveInteger"] = "{0} \u5fc5\u987b\u662f\u6b63\u6574\u6570\u3002",
            ["PositiveNumber"] = "{0} \u5fc5\u987b\u662f\u6b63\u6570\u3002",
            ["JsonCouldNotLoad"] = "JSON \u65e0\u6cd5\u52a0\u8f7d\u3002"
        }
    };

    public static string Text(string language, string key)
    {
        string normalizedLanguage = string.Equals(language, "zh", StringComparison.Ordinal) ? "zh" : "en";
        if (Resources.TryGetValue(normalizedLanguage, out Dictionary<string, string>? languageResources)
            && languageResources.TryGetValue(key, out string? text))
        {
            return text;
        }

        return Resources["en"].TryGetValue(key, out string? fallback) ? fallback : key;
    }
}
