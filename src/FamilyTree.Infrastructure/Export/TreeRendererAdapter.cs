using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Infrastructure.Export;

public sealed class TreeRendererAdapter : ITreeRendererAdapter
{
    private static readonly ILayoutStrategy Xmind = new XmindLayoutStrategy();

    public byte[] Render(
        IReadOnlyList<FamilyTreeNodeResponse> roots, ExportStyle style, string pageFormat)
    {
        var options = LayoutOptions.Default;

        // Task 14 adds the clean strategy; until then both styles share this geometry.
        var strategy = Xmind;

        var scene = strategy.Build(roots, options, SkiaTextMeasurer.Delegate);
        var format = pageFormat == "a4" ? ExportPageFormat.A4 : ExportPageFormat.Sheet;

        var fitted = format == ExportPageFormat.Sheet
            ? SceneScaler.FitToSheet(scene, options.Metrics)
            : scene;

        return new SkiaTreeRenderer().Render(fitted, format);
    }
}
