namespace FamilyTree.Application.Export;

/// <summary>
/// Width of a shaped label at a given size. Injected because shaped-text width is a font fact
/// and Application may not reference SkiaSharp (design §4.2). Tests pass a stub.
/// </summary>
public delegate double MeasureText(string text, double fontSize);

/// <summary>Defaults are measured from <c>familytree.pdf</c> (design §3.2). Do not re-derive.</summary>
public sealed record LayoutMetrics
{
    public double LeafPitch { get; init; } = 15.0;
    public double GroupSeparation { get; init; } = 29.5;
    public double ColumnGap { get; init; } = 14.0;
    public double LabelPadding { get; init; } = 6.0;
    public double ConnectorStroke { get; init; } = 1.48;
    public double CentreStroke { get; init; } = 2.22;
    public double CornerRadius { get; init; } = 6.0;
    public double CentreFontSize { get; init; } = 26.68;
    public double LevelOneFontSize { get; init; } = 17.78;
    public double BodyFontSize { get; init; } = 13.34;
    public double Margin { get; init; } = 24.0;
    public double MinFontSize { get; init; } = 6.0;
    public double MaxPageExtent { get; init; } = 14400.0;
    public double RibbonHalfWidth { get; init; } = 5.0;

    /// <summary>Extra space inserted between two siblings when either has children of its own.
    /// Turns the uniform leaf pitch into the reference's bimodal 15 / 29.5 rhythm.</summary>
    public double SiblingGroupGap => GroupSeparation - LeafPitch;

    public double FontSizeForDepth(int depth) => depth switch
    {
        0 => CentreFontSize,
        1 => LevelOneFontSize,
        _ => BodyFontSize
    };

    public NodeShape ShapeForDepth(int depth) => depth <= 1 ? NodeShape.RoundedBox : NodeShape.Tick;
}

/// <summary>
/// Hue binds to top-level branch index and is inherited by every descendant (design §3.1).
/// The first four are the reference's measured colours; the remaining eight are chosen to stay
/// separable from their neighbours both in colour and in greyscale.
/// </summary>
public sealed record BranchPalette(IReadOnlyList<string> Colors, string CentreColor)
{
    public static BranchPalette Default { get; } = new(
        [
            "#518CD8", "#FD6D5A", "#6DC354", "#FEB40B",
            "#9B6BD6", "#2FB0A5", "#E5568E", "#8A9A2B",
            "#4A6FA5", "#D97A2B", "#5FA8D3", "#A05252"
        ],
        "#8793A5");

    public string ColorAt(int branchIndex) => Colors[branchIndex % Colors.Count];
}

public sealed record LayoutOptions(LayoutMetrics Metrics, BranchPalette Palette)
{
    public static LayoutOptions Default { get; } = new(new LayoutMetrics(), BranchPalette.Default);
}
