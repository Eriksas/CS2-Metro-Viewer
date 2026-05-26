using System.Text.Json;
using MetroDiagram.Core.Models;
using MetroDiagram.Core.Validation;

namespace MetroDiagram.Core.Loading;

public static class MetroJsonLoader
{
    public static MetroLoadResult LoadFromFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            return LoadFromJson(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return MetroLoadResult.Failure($"Could not read '{path}': {ex.Message}");
        }
    }

    public static MetroLoadResult LoadFromJson(string json)
    {
        try
        {
            MetroExportDocument? document = JsonSerializer.Deserialize<MetroExportDocument>(
                json,
                MetroJsonSerializer.Options);

            return MetroNetworkValidator.ValidateAndNormalize(document);
        }
        catch (JsonException ex)
        {
            string location = ex.LineNumber.HasValue
                ? $" at line {ex.LineNumber}, byte {ex.BytePositionInLine}"
                : string.Empty;
            return MetroLoadResult.Failure($"Invalid metro JSON{location}: {ex.Message}");
        }
    }
}

