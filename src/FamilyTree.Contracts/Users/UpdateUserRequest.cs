namespace FamilyTree.Contracts.Users;

/// <summary>
/// Roles are replaced wholesale rather than patched. A partial update would need add/remove
/// lists and a merge rule; sending the intended final set makes the guard in §4.9 a simple
/// question about the state the request asks for.
/// </summary>
public sealed record UpdateUserRequest(string Email, IReadOnlyList<Guid> RoleIds);
