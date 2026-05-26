using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetroDiagram.Viewer;

public sealed class ViewerSettings
{
    [JsonPropertyName("lastJsonPath")]
    public string? LastJsonPath { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("layoutMode")]
    public string LayoutMode { get; set; } = "geographic";

    [JsonPropertyName("width")]
    public int Width { get; set; } = 1200;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 800;

    [JsonPropertyName("legendWidth")]
    public int LegendWidth { get; set; } = 240;

    [JsonPropertyName("padding")]
    public int Padding { get; set; } = 80;

    [JsonPropertyName("lineWidth")]
    public double LineWidth { get; set; } = 14;

    [JsonPropertyName("stationRadius")]
    public double StationRadius { get; set; } = 5.5;

    [JsonPropertyName("labelFontSize")]
    public double LabelFontSize { get; set; } = 12;

    [JsonPropertyName("gridSize")]
    public double GridSize { get; set; } = 32;

    [JsonPropertyName("hideGenericStationLabels")]
    public bool HideGenericStationLabels { get; set; } = true;

    [JsonPropertyName("hideCrowdedLabels")]
    public bool HideCrowdedLabels { get; set; } = true;

    [JsonPropertyName("alwaysShowInterchanges")]
    public bool AlwaysShowInterchanges { get; set; } = true;

    [JsonPropertyName("alwaysShowTerminals")]
    public bool AlwaysShowTerminals { get; set; } = true;
}

public static class ViewerSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string SettingsPath
    {
        get
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
            {
                documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(documents, "CS2MetroDiagram", "viewer-settings.json");
        }
    }

    public static ViewerSettings Load()
    {
        try
        {
            string path = SettingsPath;
            if (!File.Exists(path))
            {
                return new ViewerSettings();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ViewerSettings>(json, Options) ?? new ViewerSettings();
        }
        catch
        {
            return new ViewerSettings();
        }
    }

    public static void Save(ViewerSettings settings)
    {
        string path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
