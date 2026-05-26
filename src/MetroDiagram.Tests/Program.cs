using System.Globalization;
using System.Xml.Linq;
using MetroDiagram.Core.Loading;
using MetroDiagram.Core.Models;
using MetroDiagram.Core.Validation;
using MetroDiagram.Rendering;

SampleExpectation[] samples =
[
    new("sample-metro-small.json", "Example Metroville", ["Central", "North Pier"], 1, false),
    new("sample-metro-interchange.json", "Interchange City", ["Crossroads", "Blue Line", "Green Line"], 2, true),
    new("sample-metro-branch.json", "Branchwater", ["Junction", "Orange Main", "Orange Branch"], 2, true),
    new("sample-metro-loop.json", "Loop City", ["Harbor", "Circle Line", "Convention Spur"], 2, true),
    new("sample-metro-missing-fields.json", "Unnamed City", ["Station 1", "Station 3", "Needs Color", "Line 2"], 2, true),
    new("sample-metro-large-network.json", "Greater Sample City", ["Central", "Airport", "Purple Line"], 5, true)
];

List<(string Name, Action Test)> tests =
[
    ("all sample JSON files load and render valid SVG", () => AllSamplesLoadAndRender(samples)),
    ("legend sorts numeric line names naturally", LegendSortsNumericLineNamesNaturally),
    ("renderer sanitizes XML text values", RendererSanitizesXmlTextValues),
    ("schematic-lite snaps route points to grid and octilinear directions", SchematicLiteSnapsRoutePoints),
    ("generic station name detection covers default names", GenericStationNameDetectionCoversDefaultNames),
    ("crowded label hiding removes low priority labels only", CrowdedLabelHidingRemovesLowPriorityLabels),
    ("missing fields use documented fallbacks", MissingFieldsUseFallbacks),
    ("missing station references report a clear validation issue", MissingStationReferencesReportClearly),
    ("empty networks and empty lines do not crash", EmptyNetworksAndEmptyLinesDoNotCrash)
];

int failed = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void AllSamplesLoadAndRender(IEnumerable<SampleExpectation> samples)
{
    foreach (SampleExpectation sample in samples)
    {
        RenderedSample rendered = LoadAndRenderSample(sample.FileName);
        Assert(rendered.LoadResult.IsValid, $"{sample.FileName}: {string.Join(Environment.NewLine, rendered.LoadResult.Errors)}");
        AssertValidSvg(rendered.Xml, sample.FileName);
        AssertSvgContains(rendered.Svg, $"{sample.CityName} Metro", sample.FileName);

        foreach (string expectedText in sample.ExpectedText)
        {
            AssertSvgContains(rendered.Svg, expectedText, sample.FileName);
        }

        IReadOnlyList<XElement> routes = GetRouteElements(rendered.Xml);
        Assert(routes.Count == sample.ExpectedRouteCount, $"{sample.FileName}: expected {sample.ExpectedRouteCount} route polylines but found {routes.Count}.");
        AssertEveryRenderableLineHasRoute(rendered.Document, routes, sample.FileName);
        AssertLegendDoesNotCoverRoutes(rendered.Xml, routes, sample.FileName);

        bool hasInterchange = rendered.Xml
            .Descendants()
            .Any(element => element.Name.LocalName == "circle"
                && ((string?)element.Attribute("class"))?.Contains("station interchange", StringComparison.Ordinal) == true
                && ReadDouble(element.Attribute("r")) >= 9);
        Assert(hasInterchange == sample.ExpectInterchange, $"{sample.FileName}: interchange marker expectation did not match.");
    }
}

static void MissingFieldsUseFallbacks()
{
    RenderedSample rendered = LoadAndRenderSample("sample-metro-missing-fields.json");

    Assert(rendered.Document.City?.Name == "Unnamed City", "Missing city name did not fall back to 'Unnamed City'.");
    Assert(rendered.Document.Network?.Stations?[0].Name == "Station 1", "First missing station name did not fall back to 'Station 1'.");
    Assert(rendered.Document.Network?.Stations?[2].Name == "Station 3", "Blank station name did not fall back to 'Station 3'.");
    Assert(rendered.Document.Network?.Lines?[0].Color == "#D71920", "Missing line color did not use the first palette color.");
    Assert(rendered.Document.Network?.Lines?[1].Name == "Line 2", "Missing line name did not fall back to 'Line 2'.");
    Assert(rendered.Document.Network?.Lines?[1].Mode == "metro", "Missing line mode did not fall back to 'metro'.");
    Assert(rendered.LoadResult.Warnings.Any(warning => warning.Contains("Missing station reference", StringComparison.Ordinal)), "Missing station reference was not reported.");
}

static void LegendSortsNumericLineNamesNaturally()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Legend City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "Station A",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_10", "line_2", "line_airport", "line_1", "line_8"]
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "Station B",
                    Position = new MetroPosition { X = 100, Z = 100 },
                    Lines = ["line_10", "line_2", "line_airport", "line_1", "line_8"]
                }
            ],
            Lines =
            [
                new MetroLine { Id = "line_10", Name = "10号线", Color = "#D71920", Stops = ["station_a", "station_b"] },
                new MetroLine { Id = "line_2", Name = "2号线", Color = "#0072BC", Stops = ["station_a", "station_b"] },
                new MetroLine { Id = "line_airport", Name = "Airport Express", Color = "#00A651", Stops = ["station_a", "station_b"] },
                new MetroLine { Id = "line_1", Name = "1号线", Color = "#F7941D", Stops = ["station_a", "station_b"] },
                new MetroLine { Id = "line_8", Name = "Metro Line 8", Color = "#92278F", Stops = ["station_a", "station_b"] }
            ]
        }
    };

    SvgRenderResult renderResult = new MetroSvgRenderer().Render(document);
    XDocument xml = XDocument.Parse(renderResult.Svg);
    XElement legend = xml
        .Descendants()
        .First(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "legend");
    List<string> labels = legend
        .Descendants()
        .Where(element => element.Name.LocalName == "text" && ((string?)element.Attribute("class")) == "legend-label")
        .Select(element => element.Value)
        .ToList();

    Assert(labels.SequenceEqual(["1号线", "2号线", "Metro Line 8", "10号线", "Airport Express"]), $"Legend order was not natural: {string.Join(", ", labels)}.");
}

static void RendererSanitizesXmlTextValues()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "XML <City> & Test" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "Bad <Name> & \u0001 \ud800 End",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_1"]
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "正常站",
                    Position = new MetroPosition { X = 100, Z = 100 },
                    Lines = ["line_1"]
                }
            ],
            Lines =
            [
                new MetroLine { Id = "line_1", Name = "1号线 <A&B>", Color = "#D71920", Stops = ["station_a", "station_b"] }
            ]
        }
    };

    SvgRenderResult renderResult = new MetroSvgRenderer().Render(document);
    XDocument xml = XDocument.Parse(renderResult.Svg);
    AssertValidSvg(xml, "XML text sanitization");
    Assert(xml.Descendants().Any(element => element.Value.Contains("Bad <Name> &", StringComparison.Ordinal)), "Escaped station text did not round-trip through XML parsing.");
    Assert(xml.Descendants().Any(element => element.Value.Contains("正常站", StringComparison.Ordinal)), "Unicode station text did not round-trip through XML parsing.");
}

static void SchematicLiteSnapsRoutePoints()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Layout City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "A",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_test"]
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "B",
                    Position = new MetroPosition { X = 84, Z = 29 },
                    Lines = ["line_test"]
                },
                new MetroStation
                {
                    Id = "station_c",
                    Name = "C",
                    Position = new MetroPosition { X = 148, Z = 112 },
                    Lines = ["line_test"]
                },
                new MetroStation
                {
                    Id = "station_d",
                    Name = "D",
                    Position = new MetroPosition { X = 210, Z = 119 },
                    Lines = ["line_test"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_test",
                    Name = "Test Line",
                    Color = "#D71920",
                    Stops = ["station_a", "station_b", "station_c", "station_d"]
                }
            ]
        }
    };

    SvgRenderOptions options = new()
    {
        LayoutMode = SvgLayoutMode.SchematicLite,
        Width = 640,
        Height = 480,
        Padding = 64,
        Margin = 64,
        LegendWidth = 160,
        GridSize = 32
    };

    SvgRenderResult renderResult = new MetroSvgRenderer().Render(document, options);
    XDocument xml = XDocument.Parse(renderResult.Svg);
    AssertValidSvg(xml, "schematic-lite layout");

    XElement routes = xml
        .Descendants()
        .First(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "routes");
    Assert((string?)routes.Attribute("data-layout") == "schematic-lite", "Schematic-lite SVG did not record the layout mode.");

    XElement route = GetRouteElements(xml).Single();
    List<(double X, double Y)> points = SplitPoints((string?)route.Attribute("points")).ToList();
    Assert(points.Count == 4, "Schematic-lite route did not preserve the stop count.");

    foreach ((double x, double y) in points)
    {
        Assert(IsOnGrid(x, options.GridSize), $"Schematic-lite x={x:0.###} was not on the grid.");
        Assert(IsOnGrid(y, options.GridSize), $"Schematic-lite y={y:0.###} was not on the grid.");
    }

    for (int i = 1; i < points.Count; i++)
    {
        double dx = Math.Abs(points[i].X - points[i - 1].X);
        double dy = Math.Abs(points[i].Y - points[i - 1].Y);
        bool octilinear = dx < 0.001 || dy < 0.001 || Math.Abs(dx - dy) < 0.001;
        Assert(octilinear, $"Segment {i - 1}->{i} was not horizontal, vertical, or 45-degree diagonal.");
    }
}

static void GenericStationNameDetectionCoversDefaultNames()
{
    string[] genericNames =
    [
        "小型地铁广场",
        "现代地铁站",
        "地下地铁站",
        "地铁站",
        "Subway Station",
        "Metro Station",
        "Station 1",
        "Station 25"
    ];

    foreach (string name in genericNames)
    {
        Assert(StationLabelClassifier.IsGenericOrFallbackName(name), $"Expected '{name}' to be detected as generic/default.");
    }

    Assert(StationLabelClassifier.IsGenericOrFallbackName("station_a", "station_a"), "Station id fallback was not detected.");
    Assert(!StationLabelClassifier.IsGenericOrFallbackName("Central Park"), "User station name was incorrectly treated as generic.");
    Assert(!StationLabelClassifier.IsGenericOrFallbackName("城东站"), "Named Chinese station was incorrectly treated as generic.");
}

static void CrowdedLabelHidingRemovesLowPriorityLabels()
{
    List<MetroStation> stations =
    [
        new MetroStation
        {
            Id = "station_terminal",
            Name = "Central Terminal",
            Position = new MetroPosition { X = 0, Z = 0 },
            Lines = ["line_test"]
        }
    ];
    List<string> stops = ["station_terminal"];
    for (int i = 1; i <= 12; i++)
    {
        string stationId = $"station_crowded_{i}";
        stations.Add(new MetroStation
        {
            Id = stationId,
            Name = i % 2 == 0 ? $"Local Stop {i}" : "现代地铁站",
            Position = new MetroPosition { X = 1 + i * 0.02, Z = 1 + i * 0.02 },
            Lines = ["line_test"]
        });
        stops.Add(stationId);
    }

    stations.Add(new MetroStation
    {
        Id = "station_end",
        Name = "Airport",
        Position = new MetroPosition { X = 200, Z = 160 },
        Lines = ["line_test"]
    });
    stops.Add("station_end");

    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Crowded City" },
        Network = new MetroNetwork
        {
            Stations = stations,
            Lines =
            [
                new MetroLine
                {
                    Id = "line_test",
                    Name = "Test Line",
                    Color = "#D71920",
                    Stops = stops
                }
            ]
        }
    };

    SvgRenderOptions options = new()
    {
        Width = 500,
        Height = 360,
        Padding = 80,
        Margin = 80,
        LegendWidth = 140,
        LabelFontSize = 18,
        HideCrowdedLabels = true,
        AlwaysShowTerminals = true
    };

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    int visibleLabels = CountVisibleStationLabels(xml);
    Assert(visibleLabels < stations.Count, $"Crowded label hiding did not reduce labels. Visible={visibleLabels}, stations={stations.Count}.");
    Assert(HasVisibleLabel(xml, "station_terminal"), "High priority terminal label was hidden.");
    Assert(stations.All(station => HasStationCircle(xml, station.Id!)), "One or more station circles were hidden with labels.");

    SvgRenderOptions hideGenericOptions = new()
    {
        HideGenericStationLabels = true,
        AlwaysShowTerminals = true
    };
    XDocument hiddenGenericXml = XDocument.Parse(new MetroSvgRenderer().Render(document, hideGenericOptions).Svg);
    Assert(!HasVisibleLabel(hiddenGenericXml, "station_crowded_1"), "Generic intermediate label was not hidden by HideGenericStationLabels.");
    Assert(HasVisibleLabel(hiddenGenericXml, "station_terminal"), "Terminal label was hidden by HideGenericStationLabels.");
}

static void MissingStationReferencesReportClearly()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Warning City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "Only Station",
                    Position = new MetroPosition { X = 0, Z = 0 }
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "Other Station",
                    Position = new MetroPosition { X = 100, Z = 100 }
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_a",
                    Name = "Broken Line",
                    Color = "#0072BC",
                    Stops = ["station_a", "station_missing", "station_b"]
                }
            ]
        }
    };

    MetroLoadResult result = MetroNetworkValidator.ValidateAndNormalize(document);
    string message = result.Warnings.FirstOrDefault(warning => warning.Contains("station_missing", StringComparison.Ordinal)) ?? string.Empty;
    Assert(message.StartsWith("Missing station reference:", StringComparison.Ordinal), "Missing station reference message was not clear.");

    SvgRenderResult renderResult = new MetroSvgRenderer().Render(result.Document!);
    AssertValidSvg(XDocument.Parse(renderResult.Svg), "missing station reference document");
    Assert(renderResult.Svg.Contains("Broken Line", StringComparison.Ordinal), "Renderer did not preserve the line in the legend.");
}

static void EmptyNetworksAndEmptyLinesDoNotCrash()
{
    MetroLoadResult emptyNetwork = MetroNetworkValidator.ValidateAndNormalize(new MetroExportDocument
    {
        City = new CityInfo { Name = "Empty City" },
        Network = new MetroNetwork
        {
            Stations = [],
            Lines = []
        }
    });

    SvgRenderResult emptySvg = new MetroSvgRenderer().Render(emptyNetwork.Document!);
    AssertValidSvg(XDocument.Parse(emptySvg.Svg), "empty network");
    Assert(emptySvg.Svg.Contains("No metro stations or lines", StringComparison.Ordinal), "Empty network did not render an empty notice.");

    MetroLoadResult emptyLine = MetroNetworkValidator.ValidateAndNormalize(new MetroExportDocument
    {
        City = new CityInfo { Name = "Empty Line City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "Station A",
                    Position = new MetroPosition { X = 0, Z = 0 }
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_empty",
                    Name = "Empty Line",
                    Color = "#D71920",
                    Stops = []
                }
            ]
        }
    });

    Assert(emptyLine.Warnings.Any(warning => warning.Contains("fewer than two valid stops", StringComparison.Ordinal)), "Empty line did not produce a warning.");
    SvgRenderResult emptyLineSvg = new MetroSvgRenderer().Render(emptyLine.Document!);
    AssertValidSvg(XDocument.Parse(emptyLineSvg.Svg), "empty line document");
}

static RenderedSample LoadAndRenderSample(string fileName)
{
    string samplePath = Path.Combine(FindRepositoryRoot(), "samples", fileName);
    MetroLoadResult loadResult = MetroJsonLoader.LoadFromFile(samplePath);
    Assert(loadResult.Document is not null, $"{fileName}: sample did not load a document.");

    MetroExportDocument document = loadResult.Document!;
    SvgRenderResult renderResult = new MetroSvgRenderer().Render(document);
    XDocument xml = XDocument.Parse(renderResult.Svg);
    return new RenderedSample(document, loadResult, renderResult.Svg, xml);
}

static void AssertEveryRenderableLineHasRoute(MetroExportDocument document, IReadOnlyList<XElement> routes, string context)
{
    HashSet<string> routeLineIds = routes
        .Select(route => (string?)route.Attribute("data-line-id"))
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id!)
        .ToHashSet(StringComparer.Ordinal);

    Dictionary<string, MetroStation> stationsById = (document.Network?.Stations ?? [])
        .Where(station => !string.IsNullOrWhiteSpace(station.Id) && station.Position is not null)
        .GroupBy(station => station.Id!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    foreach (MetroLine line in document.Network?.Lines ?? [])
    {
        int validPositionedStops = (line.Stops ?? []).Count(stopId => stationsById.ContainsKey(stopId));
        if (validPositionedStops >= 2)
        {
            Assert(routeLineIds.Contains(line.Id!), $"{context}: renderable line '{line.Id}' did not produce a route polyline.");
        }
    }
}

static void AssertLegendDoesNotCoverRoutes(XDocument xml, IReadOnlyList<XElement> routes, string context)
{
    XElement? legend = xml
        .Descendants()
        .FirstOrDefault(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "legend");

    if (legend is null || routes.Count == 0)
    {
        return;
    }

    XElement? legendTitle = legend
        .Descendants()
        .FirstOrDefault(element => element.Name.LocalName == "text" && element.Value == "Legend");
    double legendX = ReadDouble(legendTitle?.Attribute("x"));
    double maxRouteX = routes
        .SelectMany(route => SplitPoints((string?)route.Attribute("points")))
        .Select(point => point.X)
        .DefaultIfEmpty(0)
        .Max();

    Assert(maxRouteX <= legendX - 20, $"{context}: route geometry reached x={maxRouteX:0.###}, too close to legend at x={legendX:0.###}.");
}

static IReadOnlyList<XElement> GetRouteElements(XDocument xml)
{
    return xml
        .Descendants()
        .Where(element => element.Name.LocalName == "polyline" && (string?)element.Attribute("class") == "route")
        .ToList();
}

static bool HasVisibleLabel(XDocument xml, string stationId)
{
    return xml
        .Descendants()
        .Any(element => element.Name.LocalName == "text"
            && ((string?)element.Attribute("class")) == "station-label"
            && (string?)element.Attribute("data-station-id") == stationId);
}

static bool HasStationCircle(XDocument xml, string stationId)
{
    return xml
        .Descendants()
        .Any(element => element.Name.LocalName == "circle"
            && (string?)element.Attribute("data-station-id") == stationId);
}

static int CountVisibleStationLabels(XDocument xml)
{
    return xml
        .Descendants()
        .Count(element => element.Name.LocalName == "text"
            && ((string?)element.Attribute("class")) == "station-label"
            && !string.IsNullOrWhiteSpace((string?)element.Attribute("data-station-id")));
}

static IEnumerable<(double X, double Y)> SplitPoints(string? points)
{
    foreach (string pair in (points ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        string[] parts = pair.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            yield return (double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
        }
    }
}

static void AssertValidSvg(XDocument xml, string context)
{
    XElement root = xml.Root ?? throw new InvalidOperationException($"{context}: SVG XML had no root element.");
    Assert(root.Name.LocalName == "svg", $"{context}: XML root was not <svg>.");
    Assert(root.Name.NamespaceName == "http://www.w3.org/2000/svg", $"{context}: SVG namespace was missing.");
    Assert(root.Descendants().Any(element => element.Name.LocalName == "title"), $"{context}: SVG did not contain a <title> element.");
}

static void AssertSvgContains(string svg, string expected, string context)
{
    Assert(svg.Contains(expected, StringComparison.Ordinal), $"{context}: SVG did not contain '{expected}'.");
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "samples")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root containing the samples folder.");
}

static double ReadDouble(XAttribute? attribute)
{
    return double.Parse(attribute?.Value ?? "0", CultureInfo.InvariantCulture);
}

static bool IsOnGrid(double value, double gridSize)
{
    return Math.Abs(value / gridSize - Math.Round(value / gridSize)) < 0.001;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record SampleExpectation(
    string FileName,
    string CityName,
    IReadOnlyList<string> ExpectedText,
    int ExpectedRouteCount,
    bool ExpectInterchange);

internal sealed record RenderedSample(
    MetroExportDocument Document,
    MetroLoadResult LoadResult,
    string Svg,
    XDocument Xml);
