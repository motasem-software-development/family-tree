using FamilyTree.Application.FamilyTrees;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.Common;

namespace FamilyTree.Application.Export;

/// <summary>
/// Turns a tree into PDF bytes. Rendering is CPU-bound, so concurrency is bounded here rather
/// than left to the thread pool: one tenant's repeated exports must not starve everyone else's
/// requests (design §5.2).
/// </summary>
public sealed class FamilyTreeExportService(
    IFamilyTreeService trees, ITreeRendererAdapter renderer, TimeProvider timeProvider) : IFamilyTreeExporter
{
    private const int MemberCap = 10_000;

    // Process-wide, not per-request: the limit exists to cap total CPU, so a per-instance
    // semaphore would not bound anything.
    private static readonly SemaphoreSlim RenderSlots = new(2, 2);

    public async Task<ExportResult> ExportAsync(
        Guid? rootId,
        int? maxDepth,
        ExportStyle style,
        string pageFormat,
        CaptionLanguage language,
        CancellationToken ct)
    {
        var view = await trees.GetViewAsync(rootId, maxDepth, ct);

        // Assemble returns an empty list for an unknown id; the tenant-safe response is the
        // same 404 an unknown member gets anywhere else (design §5.3).
        if (rootId is not null && view.RootMembers.Count == 0)
            throw new NotFoundException("MEMBER_NOT_FOUND", "No such member for this tenant.");

        var count = view.RootMembers.Sum(Count);
        if (count > MemberCap)
            throw new TooLargeException(
                "EXPORT_TREE_TOO_LARGE",
                $"This tree has {count} members, above the {MemberCap} export limit.",
                "member-cap");

        // The export date is a value captured once here, not read from the clock inside
        // rendering -- SkiaTreeRenderer stays byte-deterministic for a fixed input that way.
        var caption = new PdfCaption(
            view.Name,
            count,
            GenerationCount(view.RootMembers),
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            language);

        await RenderSlots.WaitAsync(ct);
        try
        {
            return new ExportResult(
                renderer.Render(view.RootMembers, style, pageFormat, caption), view.Name);
        }
        finally
        {
            RenderSlots.Release();
        }
    }

    private static int Count(FamilyTreeNodeResponse node) => 1 + node.Children.Sum(Count);

    /// <summary>
    /// Distinct generations reachable from the roots. A forest's roots are not necessarily all
    /// at the same generation, so this is a min/max span over every node, not just tree depth.
    /// </summary>
    private static int GenerationCount(IReadOnlyList<FamilyTreeNodeResponse> roots)
    {
        if (roots.Count == 0) return 0;

        var min = int.MaxValue;
        var max = int.MinValue;
        Visit(roots);
        return max - min + 1;

        void Visit(IReadOnlyList<FamilyTreeNodeResponse> nodes)
        {
            foreach (var node in nodes)
            {
                min = Math.Min(min, node.Generation);
                max = Math.Max(max, node.Generation);
                Visit(node.Children);
            }
        }
    }
}
