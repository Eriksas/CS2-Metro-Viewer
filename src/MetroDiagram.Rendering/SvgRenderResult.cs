namespace MetroDiagram.Rendering;

public sealed class SvgRenderResult
{
    public SvgRenderResult(string svg, IReadOnlyList<string> warnings)
    {
        Svg = svg;
        Warnings = warnings;
    }

    public string Svg { get; }

    public IReadOnlyList<string> Warnings { get; }
}

