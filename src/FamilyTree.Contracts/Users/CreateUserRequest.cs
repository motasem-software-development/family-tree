namespace FamilyTree.Contracts.Users;

public sealed record CreateUserRequest(
    string Email, string Password, IReadOnlyList<Guid> RoleIds);
