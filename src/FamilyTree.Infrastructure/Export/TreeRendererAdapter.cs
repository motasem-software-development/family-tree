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

        // The caption band must be reserved BEFORE scaling, not added to an already-maxed page
        // afterward, or a scene that scales to exactly MaxPageExtent plus a caption pushes the
        // page past the PDF format's legal maximum (design §4.6, Round-3 review Critical 1).
        var reservedHeight = caption is not null && format == ExportPageFormat.Sheet
            ? SkiaTreeRenderer.CaptionBandHeight
            : 0f;

        var fitted = format == ExportPageFormat.Sheet
            ? SceneScaler.FitToSheet(scene, options.Metrics, reservedHeight)
            : scene;

        return new SkiaTreeRenderer().Render(fitted, format, caption);
    }
}
