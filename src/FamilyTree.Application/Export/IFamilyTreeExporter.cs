using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

public enum ExportStyle { Xmind, Clean }

/// <returns>The rendered PDF, and the family tree name used for the download filename.</returns>
public sealed record ExportResult(byte[] Content, string FamilyTreeName);

public interface IFamilyTreeExporter
{
    Task<ExportResult> ExportAsync(
        Guid? rootId,
        int? maxDepth,
        ExportStyle style,
        string pageFormat,
        CaptionLanguage language,
        CancellationToken ct);
}

/// <summary>
/// The Infrastructure seam. Application defines the shape; Infrastructure owns SkiaSharp, which
/// is what keeps the SkiaSharp package out of this project (design §4.2).
/// </summary>
public interface ITreeRendererAdapter
{
    /// <param name="caption">
    /// Null draws no caption (design §4.6). Optional, not required, so every existing call site
    /// that predates the caption keeps compiling and rendering byte-for-byte as before.
    /// </param>
    /// <param name="ct">
    /// Rendering is the long pole of an export -- a large A4 document is many pages of CPU held
    /// inside one of only two process-wide render slots -- so a caller that has gone away must be
    /// able to stop it, not merely stop waiting for it (final review, Critical 2). Optional for
    /// the same reason <paramref name="caption"/> is: existing call sites keep compiling.
    /// </param>
    byte[] Render(
        IReadOnlyList<FamilyTreeNodeResponse> roots,
        ExportStyle style,
        string pageFormat,
        PdfCaption? caption = null,
        CancellationToken ct = default);
}
