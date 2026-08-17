namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// A matched member and the path that disambiguates them. Design spec §5.4 calls the ancestor
/// path "required rather than decorative": the imported tree has 39 members named محمد, and
/// generation alone cannot tell a user which one they are looking at.
/// </summary>
/// <param name="Ancestors">Root first, excluding the hit itself. Empty for a root member.</param>
/// <param name="Generation">1-based; equals Ancestors.Count + 1.</param>
public sealed record FamilyMemberSearchHit(
    Guid Id,
    string Name,
    int Generation,
    IReadOnlyList<FamilyMemberAncestor> Ancestors);
