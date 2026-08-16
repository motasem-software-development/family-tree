namespace FamilyTree.Domain.Authorization;

/// <summary>Join entity. Composite key (RoleId, PermissionId) is configured in Task 5.</summary>
public sealed class RolePermission
{
    private RolePermission() { }

    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    public static RolePermission Create(Guid roleId, Guid permissionId) =>
        new() { RoleId = roleId, PermissionId = permissionId };
}
