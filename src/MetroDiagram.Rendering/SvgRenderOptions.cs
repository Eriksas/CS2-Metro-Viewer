namespace MetroDiagram.Rendering;

public enum SvgLayoutMode
{
    Geographic,
    SchematicLite
}

public sealed class SvgRenderOptions
{
    public SvgLayoutMode LayoutMode { get; init; } = SvgLayoutMode.Geographic;

    public int Width { get; init; } = 1200;

    public int Height { get; init; } = 800;

    public int Padding { get; init; } = 80;

    public int Margin { get; init; } = 80;

    public int LegendWidth { get; init; } = 240;

    public int LegendGap { get; init; } = 40;

    public double LineWidth { get; init; } = 14;

    public double StationRadius { get; init; } = 5.5;

    public double InterchangeStationRadius { get; init; } = 9;

    public double LabelFontSize { get; init; } = 12;

    public double LegendLabelFontSize { get; init; } = 13;

    public double LabelGap { get; init; } = 8;

    public bool EnableCenterExpansion { get; init; }

    public double CenterExpansionStrength { get; init; } = 0.18;

    public double GridSize { get; init; } = 32;

    internal int EffectivePadding => Padding != 80 ? Padding : Margin;
}
