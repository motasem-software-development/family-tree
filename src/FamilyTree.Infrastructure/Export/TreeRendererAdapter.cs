using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Infrastructure.Export;

public sealed class TreeRendererAdapter : ITreeRendererAdapter
{
    private static readonly ILayoutStrategy Xmind = new XmindLayoutStrategy();
    private static readonly ILayoutStrategy Clean = new CleanLayoutStrategy();

    public byte[] Render(
        IReadOnlyList<FamilyTreeNodeResponse> roots,
        ExportStyle style,
        string pageFormat,
        PdfCaption? caption = null)
    {
        var options = LayoutOptions.Default;

        var strategy = style switch
        {
            ExportStyle.Xmind => Xmind,
            ExportStyle.Clean => Clean,
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };

        var scene = strategy.Build(roots, options, SkiaTextMeasurer.Delegate);
        var format = pageFormat == "a4" ? ExportPageFormat.A4 : ExportPageFormat.Sheet;

        var fitted = format == ExportPageFormat.Sheet
            ? SceneScaler.FitToSheet(scene, options.Metrics)
            : scene;

        return new SkiaTreeRenderer().Render(fitted, format, caption);
    }
}
