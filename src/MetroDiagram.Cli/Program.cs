using System.Globalization;
using System.Text;
using MetroDiagram.Core.Loading;
using MetroDiagram.Rendering;

if (args.Length < 2 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: MetroDiagram.Cli <input.json> <output.svg> [--layout geographic|schematic-lite] [--grid-size N] [--width N] [--height N] [--legend-width N] [--padding N] [--line-width N] [--station-radius N] [--label-font-size N] [--center-expansion] [--hide-generic-labels] [--hide-crowded-labels] [--always-show-interchanges] [--always-show-terminals]");
    return args.Length < 2 ? 2 : 0;
}

string inputPath = Path.GetFullPath(args[0]);
string outputPath = Path.GetFullPath(args[1]);
SvgRenderOptions renderOptions;

try
{
    renderOptions = ParseRenderOptions(args.Skip(2).ToArray());
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 2;
}

MetroLoadResult loadResult = MetroJsonLoader.LoadFromFile(inputPath);
foreach (string warning in loadResult.Warnings)
{
    Console.Error.WriteLine($"Warning: {warning}");
}

if (!loadResult.IsValid || loadResult.Document is null)
{
    foreach (string error in loadResult.Errors)
    {
        Console.Error.WriteLine($"Error: {error}");
    }

    return 1;
}

MetroSvgRenderer renderer = new();
SvgRenderResult renderResult = renderer.Render(loadResult.Document, renderOptions);
foreach (string warning in renderResult.Warnings)
{
    Console.Error.WriteLine($"Warning: {warning}");
}

string? outputDirectory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrWhiteSpace(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

File.WriteAllText(outputPath, renderResult.Svg, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"SVG written to {outputPath}");
return 0;

static SvgRenderOptions ParseRenderOptions(string[] optionArgs)
{
    int? width = null;
    int? height = null;
    int? legendWidth = null;
    int? padding = null;
    double? lineWidth = null;
    double? stationRadius = null;
    double? labelFontSize = null;
    SvgLayoutMode? layoutMode = null;
    double? gridSize = null;
    bool centerExpansion = false;
    bool hideGenericLabels = false;
    bool hideCrowdedLabels = false;
    bool alwaysShowInterchanges = false;
    bool alwaysShowTerminals = false;

    for (int i = 0; i < optionArgs.Length; i++)
    {
        string option = optionArgs[i];
        switch (option)
        {
            case "--layout":
                layoutMode = ReadLayoutMode(optionArgs, ref i, option);
                break;
            case "--grid-size":
                gridSize = ReadDouble(optionArgs, ref i, option);
                break;
            case "--width":
                width = ReadInt(optionArgs, ref i, option);
                break;
            case "--height":
                height = ReadInt(optionArgs, ref i, option);
                break;
            case "--legend-width":
                legendWidth = ReadInt(optionArgs, ref i, option);
                break;
            case "--padding":
                padding = ReadInt(optionArgs, ref i, option);
                break;
            case "--line-width":
                lineWidth = ReadDouble(optionArgs, ref i, option);
                break;
            case "--station-radius":
                stationRadius = ReadDouble(optionArgs, ref i, option);
                break;
            case "--label-font-size":
                labelFontSize = ReadDouble(optionArgs, ref i, option);
                break;
            case "--center-expansion":
                centerExpansion = true;
                break;
            case "--hide-generic-labels":
                hideGenericLabels = true;
                break;
            case "--hide-crowded-labels":
                hideCrowdedLabels = true;
                break;
            case "--always-show-interchanges":
                alwaysShowInterchanges = true;
                break;
            case "--always-show-terminals":
                alwaysShowTerminals = true;
                break;
            default:
                throw new ArgumentException($"Unknown option '{option}'.");
        }
    }

    SvgRenderOptions defaults = new();
    return new SvgRenderOptions
    {
        LayoutMode = layoutMode ?? defaults.LayoutMode,
        Width = width ?? defaults.Width,
        Height = height ?? defaults.Height,
        Padding = padding ?? defaults.Padding,
        Margin = padding ?? defaults.Margin,
        LegendWidth = legendWidth ?? defaults.LegendWidth,
        LegendGap = defaults.LegendGap,
        LineWidth = lineWidth ?? defaults.LineWidth,
        StationRadius = stationRadius ?? defaults.StationRadius,
        InterchangeStationRadius = stationRadius.HasValue ? Math.Max(stationRadius.Value + 3.5, stationRadius.Value * 1.45) : defaults.InterchangeStationRadius,
        LabelFontSize = labelFontSize ?? defaults.LabelFontSize,
        LegendLabelFontSize = defaults.LegendLabelFontSize,
        LabelGap = defaults.LabelGap,
        EnableCenterExpansion = centerExpansion,
        CenterExpansionStrength = defaults.CenterExpansionStrength,
        GridSize = gridSize ?? defaults.GridSize,
        HideGenericStationLabels = hideGenericLabels,
        HideCrowdedLabels = hideCrowdedLabels,
        AlwaysShowInterchanges = alwaysShowInterchanges || defaults.AlwaysShowInterchanges,
        AlwaysShowTerminals = alwaysShowTerminals || defaults.AlwaysShowTerminals
    };
}

static SvgLayoutMode ReadLayoutMode(string[] args, ref int index, string option)
{
    string value = ReadValue(args, ref index, option);
    return value switch
    {
        "geographic" => SvgLayoutMode.Geographic,
        "schematic-lite" => SvgLayoutMode.SchematicLite,
        _ => throw new ArgumentException($"{option} expects 'geographic' or 'schematic-lite'.")
    };
}

static int ReadInt(string[] args, ref int index, string option)
{
    string value = ReadValue(args, ref index, option);
    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
    {
        throw new ArgumentException($"{option} expects a positive integer.");
    }

    return parsed;
}

static double ReadDouble(string[] args, ref int index, string option)
{
    string value = ReadValue(args, ref index, option);
    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || parsed <= 0)
    {
        throw new ArgumentException($"{option} expects a positive number.");
    }

    return parsed;
}

static string ReadValue(string[] args, ref int index, string option)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"{option} expects a value.");
    }

    index++;
    return args[index];
}
