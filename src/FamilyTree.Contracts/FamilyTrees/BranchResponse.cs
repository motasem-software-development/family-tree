namespace FamilyTree.Contracts.FamilyTrees;

/// <summary>
/// One branch of the tree: a direct child of the currently selected root (design spec §1.3), and
/// therefore one of the values the branch filter can take.
///
/// The root itself is not a branch and never appears here — specification §21 renders it as
/// "Root", which is the absence of a branch rather than a value that can be selected.
/// </summary>
public sealed record BranchResponse(Guid Id, string Name);
