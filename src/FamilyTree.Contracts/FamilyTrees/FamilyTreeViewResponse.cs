namespace FamilyTree.Contracts.FamilyTrees;

/// <summary>
/// One node of the nested tree. <paramref name="Generation"/> is computed during assembly and
/// never stored (design spec §3.6, SRS §32). <paramref name="HasMoreChildren"/> is true when
/// this node has children that were not returned because of a depth limit — the flag is what
/// lets the client show an expander without guessing (design spec §4.5).
/// </summary>
public sealed record FamilyTreeNodeResponse(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Generation,
    bool HasMoreChildren,
    IReadOnlyList<FamilyTreeNodeResponse> Children);

/// <summary>
/// The root family plus its first-generation members. The root family is the tree itself, not
/// a member (technical specification §10, BR-003).
/// </summary>
public sealed record FamilyTreeViewResponse(
    Guid Id,
    string Name,
    IReadOnlyList<FamilyTreeNodeResponse> RootMembers);
