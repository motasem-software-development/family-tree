using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

public enum ExportStyle { Xmind, Clean }

/// <returns>The rendered PDF, and the family tree name used for the download filename.</returns>
public sealed record ExportResult(byte[] Content, string FamilyTreeName);

public interface IFamilyTreeExporter
{
    Task<ExportResult> ExportAsync(
        Guid? rootId, int? maxDepth, ExportStyle style, string pageFormat, CancellationToken ct);
}

/// <summary>
/// The Infrastructure seam. Application defines the shape; Infrastructure owns SkiaSharp, which
/// is what keeps the SkiaSharp package out of this project (design §4.2).
/// </summary>
public interface ITreeRendererAdapter
{
    byte[] Render(IReadOnlyList<FamilyTreeNodeResponse> roots, ExportStyle style, string pageFormat);
}
