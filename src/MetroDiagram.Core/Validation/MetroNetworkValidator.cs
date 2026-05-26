using MetroDiagram.Core.Loading;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Core.Validation;

public static class MetroNetworkValidator
{
    private const double PathPointDeduplicationEpsilon = 0.001;

    private static readonly string[] Palette =
    [
        "#D71920",
        "#0072BC",
        "#00A651",
        "#F7941D",
        "#92278F",
        "#00AEEF",
        "#8DC63F",
        "#EC008C"
    ];

    public static MetroLoadResult ValidateAndNormalize(MetroExportDocument? document)
    {
        List<string> warnings = [];
        List<string> errors = [];

        if (document is null)
        {
            return new MetroLoadResult(null, warnings, ["Metro JSON was empty or did not match the expected document shape."]);
        }

        if (document.SchemaVersion != 1)
        {
            errors.Add($"Unsupported schemaVersion '{document.SchemaVersion}'. Expected schemaVersion 1.");
        }

        document.Generator ??= new GeneratorInfo();
        document.Game ??= new GameInfo();
        document.City ??= new CityInfo();
        document.Network ??= new MetroNetwork();
        document.Network.Stations ??= [];
        document.Network.Lines ??= [];

        if (string.IsNullOrWhiteSpace(document.City.Name))
        {
            warnings.Add("City name was missing; using 'Unnamed City'.");
            document.City.Name = "Unnamed City";
        }

        NormalizeStations(document.Network.Stations, warnings);
        NormalizeLines(document.Network.Lines, document.Network.Stations, warnings);
        ValidateLineReferences(document.Network, warnings);
        DeriveStationLineMembership(document.Network);

        return new MetroLoadResult(document, warnings, errors);
    }

    private static void NormalizeStations(List<MetroStation> stations, List<string> warnings)
    {
        HashSet<string> stationIds = new(StringComparer.Ordinal);

        for (int i = 0; i < stations.Count; i++)
        {
            MetroStation station = stations[i];
            station.Lines ??= [];

            if (string.IsNullOrWhiteSpace(station.Id))
            {
                station.Id = $"station_auto_{i + 1:000}";
                warnings.Add($"Station at index {i} was missing an id; generated '{station.Id}'.");
            }

            if (!stationIds.Add(station.Id))
            {
                warnings.Add($"Duplicate station id '{station.Id}' found; the first station will be used for rendering references.");
            }

            if (string.IsNullOrWhiteSpace(station.Name))
            {
                station.Name = $"Station {i + 1}";
                warnings.Add($"Station '{station.Id}' was missing a name; using '{station.Name}'.");
            }

            if (station.Position is null)
            {
                warnings.Add($"Station '{station.Id}' has no position and may not be rendered.");
            }
        }
    }

    private static void NormalizeLines(List<MetroLine> lines, List<MetroStation> stations, List<string> warnings)
    {
        HashSet<string> lineIds = new(StringComparer.Ordinal);
        HashSet<string> stationIds = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id))
            .Select(station => station.Id!)
            .ToHashSet(StringComparer.Ordinal);

        for (int i = 0; i < lines.Count; i++)
        {
            MetroLine line = lines[i];
            line.Stops ??= [];
            line.PathPoints = NormalizePathPoints(line.PathPoints);

            if (string.IsNullOrWhiteSpace(line.Id))
            {
                line.Id = $"line_auto_{i + 1:000}";
                warnings.Add($"Line at index {i} was missing an id; generated '{line.Id}'.");
            }

            if (!lineIds.Add(line.Id))
            {
                warnings.Add($"Duplicate line id '{line.Id}' found.");
            }

            if (string.IsNullOrWhiteSpace(line.Name))
            {
                line.Name = $"Line {i + 1}";
                warnings.Add($"Line '{line.Id}' was missing a name; using '{line.Name}'.");
            }

            if (string.IsNullOrWhiteSpace(line.Mode))
            {
                line.Mode = "metro";
            }

            if (!IsHexColor(line.Color))
            {
                string fallbackColor = Palette[i % Palette.Length];
                warnings.Add($"Line '{line.Id}' was missing a valid color; using '{fallbackColor}'.");
                line.Color = fallbackColor;
            }

            int validStops = line.Stops.Count(stopId => stationIds.Contains(stopId));
            if (validStops < 2)
            {
                warnings.Add($"Line '{line.Id}' has fewer than two valid stops and may not render as a route.");
            }
        }
    }

    private static void ValidateLineReferences(MetroNetwork network, List<string> warnings)
    {
        HashSet<string> stationIds = network.Stations!
            .Where(station => !string.IsNullOrWhiteSpace(station.Id))
            .Select(station => station.Id!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (MetroLine line in network.Lines!)
        {
            foreach (string stopId in line.Stops!)
            {
                if (!stationIds.Contains(stopId))
                {
                    warnings.Add($"Missing station reference: line '{line.Id}' stop '{stopId}' does not match any station id; that stop will be skipped.");
                }
            }
        }
    }

    private static List<MetroPathPoint> NormalizePathPoints(List<MetroPathPoint>? pathPoints)
    {
        if (pathPoints is null || pathPoints.Count == 0)
        {
            return [];
        }

        List<MetroPathPoint> normalized = [];
        foreach (MetroPathPoint point in pathPoints)
        {
            if (normalized.Count > 0 && AreClose(normalized[^1], point))
            {
                continue;
            }

            normalized.Add(point);
        }

        return normalized;
    }

    private static bool AreClose(MetroPathPoint a, MetroPathPoint b)
    {
        return Math.Abs(a.X - b.X) <= PathPointDeduplicationEpsilon
            && Math.Abs(a.Z - b.Z) <= PathPointDeduplicationEpsilon;
    }

    private static void DeriveStationLineMembership(MetroNetwork network)
    {
        Dictionary<string, MetroStation> stationsById = network.Stations!
            .Where(station => !string.IsNullOrWhiteSpace(station.Id))
            .GroupBy(station => station.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (MetroLine line in network.Lines!)
        {
            foreach (string stopId in line.Stops!)
            {
                if (!stationsById.TryGetValue(stopId, out MetroStation? station))
                {
                    continue;
                }

                station.Lines ??= [];
                if (!station.Lines.Contains(line.Id!, StringComparer.Ordinal))
                {
                    station.Lines.Add(line.Id!);
                }
            }
        }

        foreach (MetroStation station in stationsById.Values)
        {
            station.IsInterchange = station.IsInterchange || (station.Lines?.Distinct(StringComparer.Ordinal).Count() ?? 0) > 1;
        }
    }

    private static bool IsHexColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color) || color.Length != 7 || color[0] != '#')
        {
            return false;
        }

        for (int i = 1; i < color.Length; i++)
        {
            char c = color[i];
            bool isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
