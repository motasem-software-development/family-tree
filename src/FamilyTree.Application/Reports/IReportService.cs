using FamilyTree.Contracts.Reports;

namespace FamilyTree.Application.Reports;

public interface IReportService
{
    /// <summary>
    /// Throws NotFoundException("FAMILY_TREE_NOT_FOUND") when the caller's tenant has no tree.
    /// An empty tree is not an error: it reports zeros.
    /// </summary>
    Task<ReportsResponse> GetAsync(CancellationToken ct = default);
}
