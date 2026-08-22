namespace FamilyTree.Contracts.Reports;

/// <summary>
/// Shape only, no member lists: the tree screen already browses these. Because a parent link
/// is guaranteed to resolve, <paramref name="TotalMembers"/> always equals what the tree
/// screen renders and generation 1 is exactly <paramref name="Branches"/> (design §5).
/// </summary>
public sealed record StructureReport(
    int TotalMembers,
    int Depth,
    IReadOnlyList<GenerationCount> Generations,
    IReadOnlyList<BranchSummary> Branches,
    int MembersWithChildren,
    int LeafMembers,
    decimal AverageChildrenPerParent);

public sealed record GenerationCount(int Generation, int Count);

/// <summary>One first-generation member and the subtree hanging off it.</summary>
public sealed record BranchSummary(Guid Id, string Name, int DescendantCount, int Depth);
