namespace MetroDiagram.Rendering;

public static class StationLabelClassifier
{
    private static readonly string[] GenericStationNames =
    [
        "小型地铁广场",
        "现代地铁站",
        "地下地铁站",
        "地铁站",
        "Subway Station",
        "Metro Station"
    ];

    public static bool IsGenericOrFallbackName(string? name, string? stationId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        string trimmed = name.Trim();
        if (!string.IsNullOrWhiteSpace(stationId) && string.Equals(trimmed, stationId.Trim(), StringComparison.Ordinal))
        {
            return true;
        }

        if (IsStationNumberFallback(trimmed))
        {
            return true;
        }

        return GenericStationNames.Any(generic => string.Equals(trimmed, generic, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStationNumberFallback(string value)
    {
        const string stationPrefix = "Station ";
        if (!value.StartsWith(stationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string suffix = value[stationPrefix.Length..];
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }
}
