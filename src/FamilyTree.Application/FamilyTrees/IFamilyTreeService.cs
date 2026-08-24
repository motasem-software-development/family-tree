using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.FamilyTrees;

public interface IFamilyTreeService
{
    Task<FamilyTreeResponse> GetAsync(CancellationToken ct = default);

    Task<FamilyTreeResponse> RenameAsync(RenameFamilyTreeRequest request, CancellationToken ct = default);

    /// <summary>
    /// The whole tree by default. <paramref name="maxDepth"/> exists from the start so the
    /// growth path to incremental loading is real rather than aspirational (design spec §4.5);
    /// it stays a separate parameter because it is a transport concern — how much of the tree to
    /// ship — rather than a filter.
    ///
    /// <paramref name="filter"/> carries the root as well as the filter set. A member who fails
    /// the filter but has a matching descendant is returned with <c>Matches</c> false rather
    /// than dropped (design spec §4.2). Pass <see cref="MemberFilter.None"/> for the whole tree.
    /// </summary>
    Task<FamilyTreeViewResponse> GetViewAsync(
        MemberFilter filter, int? maxDepth, CancellationToken ct = default);

    /// <summary>
    /// The direct children of <paramref name="rootId"/> — the values the branch filter can take
    /// (design spec §1.3). Deliberately not narrowed by the rest of the filter: it answers "what
    /// is available", and narrowing it would build a dropdown that erases its own options.
    /// </summary>
    Task<IReadOnlyList<BranchResponse>> ListBranchesAsync(
        Guid? rootId, CancellationToken ct = default);

    /// <summary>
    /// The root-relative generation numbers present below <paramref name="rootId"/>, ascending
    /// from 0 at the root (design spec §1.2).
    /// </summary>
    Task<IReadOnlyList<int>> ListGenerationsAsync(
        Guid? rootId, CancellationToken ct = default);
}
