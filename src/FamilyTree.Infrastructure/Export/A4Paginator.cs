using FamilyTree.Application.Export;
using FamilyTree.Domain.Common;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// Tiling across A4 pages, implemented in Task 13. Until then the format is refused explicitly
/// rather than silently falling back to a single sheet, which would hand the user a page their
/// printer cannot take while reporting success.
/// </summary>
public static class A4Paginator
{
    public static IEnumerable<PageWindow> Pages(TreeScene scene) =>
        throw new TooLargeException(
            "EXPORT_TREE_TOO_LARGE",
            "A4 pagination is not available yet. Export a single sheet instead.",
            "format-unavailable");
}
