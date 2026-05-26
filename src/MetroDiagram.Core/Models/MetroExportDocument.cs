using System.Text.Json.Serialization;

namespace MetroDiagram.Core.Models;

public sealed class MetroExportDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("generator")]
    public GeneratorInfo? Generator { get; set; } = new();

    [JsonPropertyName("game")]
    public GameInfo? Game { get; set; } = new();

    [JsonPropertyName("city")]
    public CityInfo? City { get; set; } = new();

    [JsonPropertyName("network")]
    public MetroNetwork? Network { get; set; } = new();
}

public sealed class GeneratorInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; } = "CS2 Metro Diagram";

    [JsonPropertyName("version")]
    public string? Version { get; set; } = "0.1.0";
}

public sealed class GameInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; } = "Cities: Skylines II";

    [JsonPropertyName("version")]
    public string? Version { get; set; } = "unknown";
}

public sealed class CityInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; } = "Unnamed City";

    [JsonPropertyName("exportedAtUtc")]
    public string? ExportedAtUtc { get; set; }
}

public sealed class MetroNetwork
{
    [JsonPropertyName("type")]
    public string? Type { get; set; } = "metro";

    [JsonPropertyName("stations")]
    public List<MetroStation>? Stations { get; set; } = [];

    [JsonPropertyName("lines")]
    public List<MetroLine>? Lines { get; set; } = [];
}

public sealed class MetroStation
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("position")]
    public MetroPosition? Position { get; set; }

    [JsonPropertyName("lines")]
    public List<string>? Lines { get; set; } = [];

    [JsonPropertyName("isInterchange")]
    public bool IsInterchange { get; set; }
}

public sealed class MetroLine
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; } = "metro";

    [JsonPropertyName("stops")]
    public List<string>? Stops { get; set; } = [];
}

public sealed class MetroPosition
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

