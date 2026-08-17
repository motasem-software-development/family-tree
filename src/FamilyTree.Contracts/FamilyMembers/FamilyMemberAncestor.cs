namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>One link in a search hit's chain back to the root.</summary>
public sealed record FamilyMemberAncestor(Guid Id, string Name);
