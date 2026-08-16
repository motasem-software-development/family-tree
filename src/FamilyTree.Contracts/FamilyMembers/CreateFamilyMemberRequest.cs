namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// Creates a member. <paramref name="ParentId"/> null means a first-generation member directly
/// under the root family (technical specification §10). The tenant and the family tree are
/// resolved server-side and are never accepted from the client.
/// </summary>
public sealed record CreateFamilyMemberRequest(string Name, Guid? ParentId);
