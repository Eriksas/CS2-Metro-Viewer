using System.Globalization;
using System.Text;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed class MetroSvgRenderer
{
    public SvgRenderResult Render(MetroExportDocument document, SvgRenderOptions? options = null)
    {
        options ??= new SvgRenderOptions();
        List<string> warnings = [];

        MetroNetwork network = document.Network ?? new MetroNetwork();
        List<MetroStation> stations = network.Stations ?? [];
        List<MetroLine> lines = network.Lines ?? [];
        Dictionary<string, MetroStation> stationsById = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id))
            .GroupBy(station => station.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        bool hasLegend = lines.Count > 0;
        Dictionary<string, SvgPoint> stationPoints = CreateStationPoints(stations, lines, stationsById, options, hasLegend);
        HashSet<string> terminalStationIds = GetTerminalStationIds(lines, stationsById);
        StringBuilder svg = new();

        AppendHeader(svg, document, options);
        AppendEmptyNotice(svg, stations, lines, options);
        AppendRoutes(svg, lines, stationsById, stationPoints, options, warnings);
        AppendStations(svg, stations, stationPoints, options);
        AppendLabels(svg, stations, stationPoints, terminalStationIds, options, hasLegend);
        AppendLegend(svg, SortLinesForLegend(lines), options, hasLegend);
        AppendFooter(svg);

        return new SvgRenderResult(svg.ToString(), warnings);
    }

    private static Dictionary<string, SvgPoint> CreateStationPoints(
        List<MetroStation> stations,
        List<MetroLine> lines,
        Dictionary<string, MetroStation> stationsById,
        SvgRenderOptions options,
        bool reserveLegendSpace)
    {
        List<SourceStationPoint> sourcePoints = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id) && station.Position is not null)
            .Select(station => new SourceStationPoint(station.Id!, station.Position!.X, station.Position.Z))
            .ToList();

        if (sourcePoints.Count == 0)
        {
            return [];
        }

        if (options.EnableCenterExpansion)
        {
            sourcePoints = ExpandCenter(sourcePoints, options.CenterExpansionStrength);
        }

        double minX = sourcePoints.Min(point => point.X);
        double maxX = sourcePoints.Max(point => point.X);
        double minZ = sourcePoints.Min(point => point.Z);
        double maxZ = sourcePoints.Max(point => point.Z);
        double sourceWidth = Math.Max(maxX - minX, 1);
        double sourceHeight = Math.Max(maxZ - minZ, 1);
        int padding = options.EffectivePadding;
        double rightReserve = padding + (reserveLegendSpace ? options.LegendWidth + options.LegendGap : 0);
        double innerWidth = Math.Max(options.Width - padding - rightReserve, 1);
        double innerHeight = Math.Max(options.Height - padding * 2, 1);
        double scale = Math.Min(innerWidth / sourceWidth, innerHeight / sourceHeight);
        double scaledWidth = sourceWidth * scale;
        double scaledHeight = sourceHeight * scale;
        double offsetX = (innerWidth - scaledWidth) / 2;
        double offsetY = (innerHeight - scaledHeight) / 2;

        Dictionary<string, SvgPoint> points = new(StringComparer.Ordinal);
        foreach (SourceStationPoint station in sourcePoints)
        {
            if (points.ContainsKey(station.Id))
            {
                continue;
            }

            double x = padding + offsetX + (station.X - minX) * scale;
            double y = padding + offsetY + (maxZ - station.Z) * scale;
            points[station.Id] = new SvgPoint(x, y);
        }

        if (options.LayoutMode == SvgLayoutMode.SchematicLite)
        {
            return ApplySchematicLiteLayout(points, lines, stationsById, options, reserveLegendSpace);
        }

        return points;
    }

    private static Dictionary<string, SvgPoint> ApplySchematicLiteLayout(
        Dictionary<string, SvgPoint> geographicPoints,
        List<MetroLine> lines,
        Dictionary<string, MetroStation> stationsById,
        SvgRenderOptions options,
        bool reserveLegendSpace)
    {
        double gridSize = Math.Max(4, options.GridSize);
        SvgRect bounds = CreateGeometryBounds(options, reserveLegendSpace);
        Dictionary<string, SvgPoint> points = geographicPoints
            .ToDictionary(
                pair => pair.Key,
                pair => SnapPointToGrid(pair.Value, gridSize, bounds),
                StringComparer.Ordinal);
        HashSet<string> placed = new(StringComparer.Ordinal);

        foreach (MetroLine line in lines)
        {
            List<string> validStops = (line.Stops ?? [])
                .Where(stopId => !string.IsNullOrWhiteSpace(stopId) && stationsById.ContainsKey(stopId) && geographicPoints.ContainsKey(stopId))
                .ToList();

            if (validStops.Count == 0)
            {
                continue;
            }

            string firstStopId = validStops[0];
            if (placed.Add(firstStopId))
            {
                points[firstStopId] = SnapPointToGrid(geographicPoints[firstStopId], gridSize, bounds);
            }

            for (int i = 1; i < validStops.Count; i++)
            {
                string previousStopId = validStops[i - 1];
                string currentStopId = validStops[i];
                if (string.Equals(previousStopId, currentStopId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!points.TryGetValue(previousStopId, out SvgPoint previousPoint))
                {
                    previousPoint = SnapPointToGrid(geographicPoints[previousStopId], gridSize, bounds);
                    points[previousStopId] = previousPoint;
                }

                if (placed.Contains(currentStopId))
                {
                    continue;
                }

                SvgPoint desiredPoint = SnapPointToGrid(geographicPoints[currentStopId], gridSize, bounds);
                points[currentStopId] = SnapSegmentEndpoint(previousPoint, desiredPoint, gridSize, bounds);
                placed.Add(currentStopId);
            }
        }

        return points;
    }

    private static SvgPoint SnapSegmentEndpoint(SvgPoint from, SvgPoint desired, double gridSize, SvgRect bounds)
    {
        double dx = desired.X - from.X;
        double dy = desired.Y - from.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return SnapPointToGrid(desired, gridSize, bounds);
        }

        double signX = dx < 0 ? -1 : 1;
        double signY = dy < 0 ? -1 : 1;
        double horizontalLength = SnapLength(Math.Abs(dx), gridSize);
        double verticalLength = SnapLength(Math.Abs(dy), gridSize);
        double diagonalLength = SnapLength(Math.Max(Math.Abs(dx), Math.Abs(dy)), gridSize);

        List<SvgPoint> candidates =
        [
            new(from.X + signX * horizontalLength, from.Y),
            new(from.X, from.Y + signY * verticalLength),
            new(from.X + signX * diagonalLength, from.Y + signY * diagonalLength)
        ];

        SvgPoint best = desired;
        double bestDistance = double.MaxValue;
        foreach (SvgPoint candidate in candidates)
        {
            SvgPoint snappedCandidate = SnapPointToGrid(candidate, gridSize, bounds);
            double distance = DistanceSquared(snappedCandidate, desired);
            if (distance < bestDistance)
            {
                best = snappedCandidate;
                bestDistance = distance;
            }
        }

        if (DistanceSquared(best, from) < 0.001)
        {
            return SnapPointToGrid(desired, gridSize, bounds);
        }

        return best;
    }

    private static double SnapLength(double length, double gridSize)
    {
        return Math.Max(gridSize, Math.Round(length / gridSize) * gridSize);
    }

    private static SvgPoint SnapPointToGrid(SvgPoint point, double gridSize, SvgRect bounds)
    {
        double x = Math.Round(point.X / gridSize) * gridSize;
        double y = Math.Round(point.Y / gridSize) * gridSize;
        double left = Math.Ceiling(bounds.Left / gridSize) * gridSize;
        double right = Math.Floor(bounds.Right / gridSize) * gridSize;
        double top = Math.Ceiling(bounds.Top / gridSize) * gridSize;
        double bottom = Math.Floor(bounds.Bottom / gridSize) * gridSize;

        if (right < left)
        {
            left = bounds.Left;
            right = bounds.Right;
        }

        if (bottom < top)
        {
            top = bounds.Top;
            bottom = bounds.Bottom;
        }

        return new SvgPoint(
            Math.Clamp(x, left, right),
            Math.Clamp(y, top, bottom));
    }

    private static SvgRect CreateGeometryBounds(SvgRenderOptions options, bool reserveLegendSpace)
    {
        int padding = options.EffectivePadding;
        double rightReserve = padding + (reserveLegendSpace ? options.LegendWidth + options.LegendGap : 0);
        return new SvgRect(
            padding,
            padding,
            Math.Max(padding, options.Width - rightReserve),
            Math.Max(padding, options.Height - padding));
    }

    private static double DistanceSquared(SvgPoint a, SvgPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static List<SourceStationPoint> ExpandCenter(List<SourceStationPoint> sourcePoints, double strength)
    {
        strength = Math.Clamp(strength, 0, 0.45);
        if (strength <= 0 || sourcePoints.Count < 3)
        {
            return sourcePoints;
        }

        double minX = sourcePoints.Min(point => point.X);
        double maxX = sourcePoints.Max(point => point.X);
        double minZ = sourcePoints.Min(point => point.Z);
        double maxZ = sourcePoints.Max(point => point.Z);
        double centerX = (minX + maxX) / 2;
        double centerZ = (minZ + maxZ) / 2;
        double halfWidth = Math.Max((maxX - minX) / 2, 1);
        double halfHeight = Math.Max((maxZ - minZ) / 2, 1);

        return sourcePoints
            .Select(point =>
            {
                double normalizedX = (point.X - centerX) / halfWidth;
                double normalizedZ = (point.Z - centerZ) / halfHeight;
                double normalizedDistance = Math.Sqrt(normalizedX * normalizedX + normalizedZ * normalizedZ);
                double centerWeight = Math.Max(0, 1 - Math.Min(1, normalizedDistance));
                double factor = 1 + strength * centerWeight;
                return new SourceStationPoint(
                    point.Id,
                    centerX + (point.X - centerX) * factor,
                    centerZ + (point.Z - centerZ) * factor);
            })
            .ToList();
    }

    private static void AppendHeader(StringBuilder svg, MetroExportDocument document, SvgRenderOptions options)
    {
        string cityName = string.IsNullOrWhiteSpace(document.City?.Name)
            ? "Unnamed City"
            : document.City!.Name!;

        int padding = options.EffectivePadding;
        double titleY = Math.Max(36, padding - 36);
        double labelHaloWidth = Math.Max(4, options.LabelFontSize * 0.36);

        svg.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{options.Width}" height="{options.Height}" viewBox="0 0 {options.Width} {options.Height}" role="img" aria-label="{Escape(cityName)} metro diagram">""");
        svg.AppendLine($"""<title>{Escape(cityName)} Metro</title>""");
        svg.AppendLine("<defs>");
        svg.AppendLine("<style>");
        svg.AppendLine("            .background { fill: #ffffff; }");
        svg.AppendLine("            .title { font: 700 28px Arial, sans-serif; fill: #1f2933; }");
        svg.AppendLine($"            .route {{ fill: none; stroke-width: {Format(options.LineWidth)}; stroke-linecap: round; stroke-linejoin: round; }}");
        svg.AppendLine("            .station { fill: #ffffff; stroke: #1f2933; stroke-width: 3; }");
        svg.AppendLine("            .station.interchange { stroke-width: 4; }");
        svg.AppendLine($"            .station-label {{ font: 600 {Format(options.LabelFontSize)}px Arial, sans-serif; fill: #1f2933; }}");
        svg.AppendLine($"            .station-label-halo {{ stroke: #ffffff; stroke-width: {Format(labelHaloWidth)}; paint-order: stroke; stroke-linejoin: round; }}");
        svg.AppendLine("            .empty-notice { font: 600 16px Arial, sans-serif; fill: #52616f; }");
        svg.AppendLine($"            .legend-label {{ font: 600 {Format(options.LegendLabelFontSize)}px Arial, sans-serif; fill: #1f2933; }}");
        svg.AppendLine("            .legend-title { font: 700 14px Arial, sans-serif; fill: #1f2933; }");
        svg.AppendLine("</style>");
        svg.AppendLine("</defs>");
        svg.AppendLine($"""<rect class="background" x="0" y="0" width="{options.Width}" height="{options.Height}" />""");
        svg.AppendLine($"""<text class="title" x="{padding}" y="{Format(titleY)}">{Escape(cityName)} Metro</text>""");
    }

    private static void AppendEmptyNotice(StringBuilder svg, List<MetroStation> stations, List<MetroLine> lines, SvgRenderOptions options)
    {
        if (stations.Count > 0 || lines.Count > 0)
        {
            return;
        }

        int padding = options.EffectivePadding;
        svg.AppendLine($"""<text class="empty-notice" x="{padding}" y="{padding + 40}">No metro stations or lines in this file.</text>""");
    }

    private static void AppendRoutes(
        StringBuilder svg,
        List<MetroLine> lines,
        Dictionary<string, MetroStation> stationsById,
        Dictionary<string, SvgPoint> stationPoints,
        SvgRenderOptions options,
        List<string> warnings)
    {
        svg.AppendLine($"""<g id="routes" data-layout="{GetLayoutModeName(options.LayoutMode)}">""");
        foreach (MetroLine line in lines)
        {
            List<SvgPoint> points = [];
            foreach (string stopId in line.Stops ?? [])
            {
                if (stationsById.ContainsKey(stopId) && stationPoints.TryGetValue(stopId, out SvgPoint point))
                {
                    points.Add(point);
                }
            }

            if (points.Count < 2)
            {
                warnings.Add($"Line '{line.Id}' did not have enough positioned stops to render.");
                continue;
            }

            string pointList = string.Join(" ", points.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
            svg.AppendLine($"""<polyline class="route" data-line-id="{Escape(line.Id)}" points="{pointList}" stroke="{Escape(line.Color)}" />""");
        }

        svg.AppendLine("</g>");
    }

    private static string GetLayoutModeName(SvgLayoutMode layoutMode)
    {
        return layoutMode switch
        {
            SvgLayoutMode.SchematicLite => "schematic-lite",
            _ => "geographic"
        };
    }

    private static void AppendStations(StringBuilder svg, List<MetroStation> stations, Dictionary<string, SvgPoint> stationPoints, SvgRenderOptions options)
    {
        svg.AppendLine("""<g id="stations">""");
        foreach (MetroStation station in stations)
        {
            if (string.IsNullOrWhiteSpace(station.Id) || !stationPoints.TryGetValue(station.Id, out SvgPoint point))
            {
                continue;
            }

            bool isInterchange = IsInterchange(station);
            double radius = GetStationRadius(station, options);
            string stationClass = isInterchange ? "station interchange" : "station";
            svg.AppendLine($"""<circle class="{stationClass}" data-station-id="{Escape(station.Id)}" cx="{Format(point.X)}" cy="{Format(point.Y)}" r="{Format(radius)}" />""");
        }

        svg.AppendLine("</g>");
    }

    private static void AppendLabels(
        StringBuilder svg,
        List<MetroStation> stations,
        Dictionary<string, SvgPoint> stationPoints,
        HashSet<string> terminalStationIds,
        SvgRenderOptions options,
        bool hasLegend)
    {
        List<SvgRect> stationObstacles = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Id) && stationPoints.ContainsKey(station.Id!))
            .Select(station =>
            {
                SvgPoint point = stationPoints[station.Id!];
                double radius = GetStationRadius(station, options) + 1.5;
                return SvgRect.FromCenter(point.X, point.Y, radius * 2, radius * 2);
            })
            .ToList();

        List<LabelRequest> labelRequests = [];
        for (int i = 0; i < stations.Count; i++)
        {
            MetroStation station = stations[i];
            if (string.IsNullOrWhiteSpace(station.Id) || !stationPoints.TryGetValue(station.Id, out SvgPoint point))
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(station.Name) ? station.Id! : station.Name!;
            bool isTerminal = terminalStationIds.Contains(station.Id!);
            bool usesFallbackName = IsFallbackStationName(name, station.Id);
            int priority = CalculateLabelPriority(station, isTerminal, usesFallbackName);
            labelRequests.Add(new LabelRequest(station, name, point, GetStationRadius(station, options), priority, i));
        }

        SvgRect allowedBounds = CreateAllowedLabelBounds(options, hasLegend);
        List<PlacedLabel> placedLabels = [];
        List<SvgRect> placedLabelBoxes = [];

        foreach (LabelRequest request in labelRequests
            .OrderByDescending(request => request.Priority)
            .ThenBy(request => request.Index))
        {
            PlacedLabel label = ChooseLabelPlacement(request, options, placedLabelBoxes, stationObstacles, allowedBounds);
            placedLabels.Add(label);
            placedLabelBoxes.Add(label.Box);
        }

        svg.AppendLine("""<g id="labels">""");
        foreach (PlacedLabel label in placedLabels.OrderBy(label => label.Priority).ThenBy(label => label.Index))
        {
            string anchor = label.Anchor == "start" ? string.Empty : $" text-anchor=\"{label.Anchor}\"";
            string commonAttributes = $"x=\"{Format(label.X)}\" y=\"{Format(label.Y)}\"{anchor} data-station-id=\"{Escape(label.StationId)}\" data-label-position=\"{label.PositionName}\"";
            svg.AppendLine($"""<text class="station-label station-label-halo" {commonAttributes}>{Escape(label.Text)}</text>""");
            svg.AppendLine($"""<text class="station-label" {commonAttributes}>{Escape(label.Text)}</text>""");
        }

        svg.AppendLine("</g>");
    }

    private static PlacedLabel ChooseLabelPlacement(
        LabelRequest request,
        SvgRenderOptions options,
        List<SvgRect> placedLabelBoxes,
        List<SvgRect> stationObstacles,
        SvgRect allowedBounds)
    {
        List<LabelCandidate> candidates = CreateLabelCandidates(request, options);
        LabelCandidate best = candidates[0];
        double bestScore = double.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            LabelCandidate candidate = candidates[i];
            double score = i * 0.01;

            foreach (SvgRect box in placedLabelBoxes)
            {
                score += candidate.Box.OverlapArea(box) * 14;
            }

            foreach (SvgRect obstacle in stationObstacles)
            {
                score += candidate.Box.OverlapArea(obstacle) * 8;
            }

            score += candidate.Box.OutsideArea(allowedBounds) * 18;

            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return new PlacedLabel(
            request.Station.Id!,
            request.Text,
            best.X,
            best.Y,
            best.Anchor,
            best.PositionName,
            best.Box,
            request.Priority,
            request.Index);
    }

    private static List<LabelCandidate> CreateLabelCandidates(LabelRequest request, SvgRenderOptions options)
    {
        double fontSize = options.LabelFontSize;
        double width = EstimateTextWidth(request.Text, fontSize);
        double height = fontSize * 1.25;
        double gap = options.LabelGap;
        double offset = request.StationRadius + gap;
        double diagonalOffset = request.StationRadius + gap * 0.9;
        SvgPoint point = request.Point;

        return
        [
            CreateLabelCandidate("right", point.X + offset, point.Y - height / 2, width, height, "start", fontSize),
            CreateLabelCandidate("left", point.X - offset - width, point.Y - height / 2, width, height, "end", fontSize),
            CreateLabelCandidate("top", point.X - width / 2, point.Y - offset - height, width, height, "middle", fontSize),
            CreateLabelCandidate("bottom", point.X - width / 2, point.Y + offset, width, height, "middle", fontSize),
            CreateLabelCandidate("top-right", point.X + diagonalOffset, point.Y - diagonalOffset - height, width, height, "start", fontSize),
            CreateLabelCandidate("bottom-right", point.X + diagonalOffset, point.Y + diagonalOffset, width, height, "start", fontSize),
            CreateLabelCandidate("top-left", point.X - diagonalOffset - width, point.Y - diagonalOffset - height, width, height, "end", fontSize),
            CreateLabelCandidate("bottom-left", point.X - diagonalOffset - width, point.Y + diagonalOffset, width, height, "end", fontSize)
        ];
    }

    private static LabelCandidate CreateLabelCandidate(string positionName, double left, double top, double width, double height, string anchor, double fontSize)
    {
        SvgRect box = new(left, top, left + width, top + height);
        double x = anchor switch
        {
            "end" => box.Right,
            "middle" => (box.Left + box.Right) / 2,
            _ => box.Left
        };
        double y = box.Top + fontSize;
        return new LabelCandidate(positionName, x, y, anchor, box);
    }

    private static SvgRect CreateAllowedLabelBounds(SvgRenderOptions options, bool hasLegend)
    {
        int padding = options.EffectivePadding;
        double left = Math.Max(4, padding * 0.25);
        double top = Math.Max(4, padding * 0.35);
        double right = hasLegend
            ? options.Width - padding - options.LegendWidth - 12
            : options.Width - Math.Max(4, padding * 0.25);
        double bottom = options.Height - Math.Max(4, padding * 0.25);

        if (right <= left + 100)
        {
            right = Math.Max(left + 100, options.Width - options.LegendWidth - 4);
        }

        return new SvgRect(left, top, right, bottom);
    }

    private static void AppendLegend(StringBuilder svg, IReadOnlyList<MetroLine> lines, SvgRenderOptions options, bool hasLegend)
    {
        if (!hasLegend)
        {
            return;
        }

        int padding = options.EffectivePadding;
        double legendX = options.Width - padding - options.LegendWidth;
        double legendY = padding;
        double sampleLength = Math.Min(42, Math.Max(28, options.LegendWidth * 0.18));
        double rowHeight = Math.Max(24, options.LegendLabelFontSize + 11);

        svg.AppendLine("""<g id="legend">""");
        svg.AppendLine($"""<text class="legend-title" x="{Format(legendX)}" y="{Format(legendY)}">Legend</text>""");

        for (int i = 0; i < lines.Count; i++)
        {
            MetroLine line = lines[i];
            double y = legendY + 28 + i * rowHeight;
            svg.AppendLine($"""<line x1="{Format(legendX)}" y1="{Format(y)}" x2="{Format(legendX + sampleLength)}" y2="{Format(y)}" stroke="{Escape(line.Color)}" stroke-width="{Format(Math.Max(5, options.LineWidth * 0.57))}" stroke-linecap="round" />""");
            svg.AppendLine($"""<text class="legend-label" x="{Format(legendX + sampleLength + 14)}" y="{Format(y + 5)}">{Escape(line.Name)}</text>""");
        }

        svg.AppendLine("</g>");
    }

    private static IReadOnlyList<MetroLine> SortLinesForLegend(List<MetroLine> lines)
    {
        return lines
            .Select((line, index) => new LegendLine(line, index, ExtractLineNumber(line.Name)))
            .OrderBy(item => item.LineNumber.HasValue ? 0 : 1)
            .ThenBy(item => item.LineNumber ?? int.MaxValue)
            .ThenBy(item => item.LineNumber.HasValue ? item.Index : 0)
            .ThenBy(item => item.LineNumber.HasValue ? string.Empty : item.Line.Name, StringComparer.CurrentCulture)
            .Select(item => item.Line)
            .ToList();
    }

    private static int? ExtractLineNumber(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        int? value = null;
        foreach (char character in name)
        {
            double numericValue = char.GetNumericValue(character);
            if (numericValue >= 0 && numericValue <= 9 && Math.Floor(numericValue) == numericValue)
            {
                value = (value ?? 0) * 10 + (int)numericValue;
            }
            else if (value.HasValue)
            {
                return value;
            }
        }

        return value;
    }

    private static HashSet<string> GetTerminalStationIds(List<MetroLine> lines, Dictionary<string, MetroStation> stationsById)
    {
        HashSet<string> terminalStationIds = new(StringComparer.Ordinal);

        foreach (MetroLine line in lines)
        {
            List<string> validStops = (line.Stops ?? [])
                .Where(stopId => !string.IsNullOrWhiteSpace(stopId) && stationsById.ContainsKey(stopId))
                .ToList();

            if (validStops.Count < 2)
            {
                continue;
            }

            string first = validStops.First();
            string last = validStops.Last();
            if (!string.Equals(first, last, StringComparison.Ordinal))
            {
                terminalStationIds.Add(first);
                terminalStationIds.Add(last);
            }
        }

        return terminalStationIds;
    }

    private static int CalculateLabelPriority(MetroStation station, bool isTerminal, bool usesFallbackName)
    {
        int priority = 0;

        if (IsInterchange(station))
        {
            priority += 100;
        }

        if (isTerminal)
        {
            priority += 70;
        }

        priority += usesFallbackName ? -25 : 20;
        return priority;
    }

    private static bool IsInterchange(MetroStation station)
    {
        return station.IsInterchange || (station.Lines?.Distinct(StringComparer.Ordinal).Count() ?? 0) > 1;
    }

    private static bool IsFallbackStationName(string name, string? stationId)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(stationId) && string.Equals(trimmed, stationId, StringComparison.Ordinal))
        {
            return true;
        }

        const string stationPrefix = "Station ";
        if (!trimmed.StartsWith(stationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string suffix = trimmed[stationPrefix.Length..];
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    private static double GetStationRadius(MetroStation station, SvgRenderOptions options)
    {
        return IsInterchange(station) ? options.InterchangeStationRadius : options.StationRadius;
    }

    private static double EstimateTextWidth(string text, double fontSize)
    {
        double units = 0;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                units += 0.35;
            }
            else if (character <= 0x007f)
            {
                units += 0.58;
            }
            else
            {
                units += 0.95;
            }
        }

        return Math.Max(fontSize * 1.4, units * fontSize);
    }

    private static void AppendFooter(StringBuilder svg)
    {
        svg.AppendLine("</svg>");
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder escaped = new(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];

            if (char.IsHighSurrogate(character))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    escaped.Append(character);
                    escaped.Append(value[i + 1]);
                    i++;
                }

                continue;
            }

            if (char.IsLowSurrogate(character) || !IsAllowedXmlCharacter(character))
            {
                continue;
            }

            switch (character)
            {
                case '&':
                    escaped.Append("&amp;");
                    break;
                case '<':
                    escaped.Append("&lt;");
                    break;
                case '>':
                    escaped.Append("&gt;");
                    break;
                case '"':
                    escaped.Append("&quot;");
                    break;
                case '\'':
                    escaped.Append("&apos;");
                    break;
                default:
                    escaped.Append(character);
                    break;
            }
        }

        return escaped.ToString();
    }

    private static bool IsAllowedXmlCharacter(char character)
    {
        return character == '\u0009'
            || character == '\u000a'
            || character == '\u000d'
            || character >= '\u0020';
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private readonly record struct SourceStationPoint(string Id, double X, double Z);

    private readonly record struct SvgPoint(double X, double Y);

    private readonly record struct LegendLine(MetroLine Line, int Index, int? LineNumber);

    private readonly record struct LabelRequest(
        MetroStation Station,
        string Text,
        SvgPoint Point,
        double StationRadius,
        int Priority,
        int Index);

    private readonly record struct LabelCandidate(
        string PositionName,
        double X,
        double Y,
        string Anchor,
        SvgRect Box);

    private readonly record struct PlacedLabel(
        string StationId,
        string Text,
        double X,
        double Y,
        string Anchor,
        string PositionName,
        SvgRect Box,
        int Priority,
        int Index);

    private readonly record struct SvgRect(double Left, double Top, double Right, double Bottom)
    {
        public static SvgRect FromCenter(double x, double y, double width, double height)
        {
            return new SvgRect(x - width / 2, y - height / 2, x + width / 2, y + height / 2);
        }

        public double OverlapArea(SvgRect other)
        {
            double width = Math.Max(0, Math.Min(Right, other.Right) - Math.Max(Left, other.Left));
            double height = Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top));
            return width * height;
        }

        public double OutsideArea(SvgRect bounds)
        {
            double width = Math.Max(0, Right - Left);
            double height = Math.Max(0, Bottom - Top);
            double insideWidth = Math.Max(0, Math.Min(Right, bounds.Right) - Math.Max(Left, bounds.Left));
            double insideHeight = Math.Max(0, Math.Min(Bottom, bounds.Bottom) - Math.Max(Top, bounds.Top));
            return width * height - insideWidth * insideHeight;
        }
    }
}
