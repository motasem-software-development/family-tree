using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.FamilyTrees;

public interface IFamilyTreeService
{
    Task<FamilyTreeResponse> GetAsync(CancellationToken ct = default);

    Task<FamilyTreeResponse> RenameAsync(RenameFamilyTreeRequest request, CancellationToken ct = default);

    /// <summary>
    /// The whole tree by default. <paramref name="rootId"/> and <paramref name="maxDepth"/>
    /// exist from the start so the growth path to incremental loading is real rather than
    /// aspirational (design spec §4.5).
    /// </summary>
    Task<FamilyTreeViewResponse> GetViewAsync(
        Guid? rootId, int? maxDepth, CancellationToken ct = default);
}
