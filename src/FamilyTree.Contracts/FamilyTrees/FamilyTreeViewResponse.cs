namespace FamilyTree.Contracts.FamilyTrees;

/// <summary>
/// One node of the nested tree. <paramref name="Generation"/> is computed during assembly and
/// never stored (design spec §3.6, SRS §32). <paramref name="HasMoreChildren"/> is true when
/// this node has children that were not returned because of a depth limit — the flag is what
/// lets the client show an expander without guessing (design spec §4.5).
///
/// <paramref name="Matches"/> is false when this member is present only to hold up a matching
/// descendant (design spec §4.2). The client renders them dimmed and non-selectable; dropping
/// them server-side would detach the subtree and render the outline as garbage.
///
/// It defaults to true because that is what "no filter applied" means, and because the safe
/// failure for a construction site that forgets it is a visible member rather than an invisible
/// one. The assembler always passes it explicitly.
///
/// <paramref name="Generation"/> stays the absolute 1-based number, even under a generation
/// filter expressed root-relative (design spec §1.2) — the reports page and the PDF caption read
/// this field and are tree-wide.
/// </summary>
public sealed record FamilyTreeNodeResponse(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Generation,
    bool HasMoreChildren,
    IReadOnlyList<FamilyTreeNodeResponse> Children,
    bool Matches = true);

/// <summary>
/// The root family plus its first-generation members. The root family is the tree itself, not
/// a member (technical specification §10, BR-003).
/// </summary>
public sealed record FamilyTreeViewResponse(
    Guid Id,
    string Name,
    IReadOnlyList<FamilyTreeNodeResponse> RootMembers);
