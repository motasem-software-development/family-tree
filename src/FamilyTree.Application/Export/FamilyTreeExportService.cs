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
    IFamilyTreeService trees, ITreeRendererAdapter renderer) : IFamilyTreeExporter
{
    private const int MemberCap = 10_000;

    // Process-wide, not per-request: the limit exists to cap total CPU, so a per-instance
    // semaphore would not bound anything.
    private static readonly SemaphoreSlim RenderSlots = new(2, 2);

    public async Task<ExportResult> ExportAsync(
        Guid? rootId, int? maxDepth, ExportStyle style, string pageFormat, CancellationToken ct)
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

        await RenderSlots.WaitAsync(ct);
        try
        {
            return new ExportResult(renderer.Render(view.RootMembers, style, pageFormat), view.Name);
        }
        finally
        {
            RenderSlots.Release();
        }
    }

    private static int Count(FamilyTreeNodeResponse node) => 1 + node.Children.Sum(Count);
}
