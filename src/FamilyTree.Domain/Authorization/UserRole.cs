namespace FamilyTree.Domain.Authorization;

/// <summary>Join entity. Composite key (UserId, RoleId) is configured in Task 5.</summary>
public sealed class UserRole
{
    private UserRole() { }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }

    public static UserRole Create(Guid userId, Guid roleId) =>
        new() { UserId = userId, RoleId = roleId };
}
