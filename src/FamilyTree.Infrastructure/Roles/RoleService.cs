using FamilyTree.Application.Roles;
using FamilyTree.Contracts.Roles;
using FamilyTree.Domain.Authorization;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Roles;

/// <summary>
/// Roles are tenant-owned and read through the query filter, so another tenant's role id is
/// indistinguishable from a nonexistent one (design spec §4.4). Permissions are not
/// tenant-owned — the catalog is global by design.
/// </summary>
public sealed class RoleService(ApplicationDbContext context) : IRoleService
{
    public async Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken ct = default)
    {
        var roles = await context.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var ids = roles.Select(r => r.Id).ToList();

        var permissions = await PermissionsByRoleAsync(ids, ct);
        var counts = await UserCountsByRoleAsync(ids, ct);

        return roles.Select(r => Map(r, permissions, counts)).ToList();
    }

    public async Task<RoleResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var role = await context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return null;

        return Map(role,
            await PermissionsByRoleAsync([role.Id], ct),
            await UserCountsByRoleAsync([role.Id], ct));
    }

    public async Task<IReadOnlyList<PermissionResponse>> ListPermissionsAsync(
        CancellationToken ct = default) =>
        await context.Permissions.AsNoTracking()
            .OrderBy(p => p.Code)
            .Select(p => new PermissionResponse(p.Code, p.Description!))
            .ToListAsync(ct);

    private async Task<ILookup<Guid, string>> PermissionsByRoleAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        var rows = await (
            from rolePermission in context.RolePermissions
            join permission in context.Permissions
                on rolePermission.PermissionId equals permission.Id
            where roleIds.Contains(rolePermission.RoleId)
            select new { rolePermission.RoleId, permission.Code })
            .ToListAsync(ct);

        return rows.ToLookup(r => r.RoleId, r => r.Code);
    }

    private async Task<Dictionary<Guid, int>> UserCountsByRoleAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        var rows = await context.UserRoles
            .Where(ur => roleIds.Contains(ur.RoleId))
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.RoleId, r => r.Count);
    }

    private static RoleResponse Map(
        Role role, ILookup<Guid, string> permissions, Dictionary<Guid, int> counts) =>
        new(role.Id, role.Name, role.Description, role.IsSystem,
            counts.GetValueOrDefault(role.Id),
            permissions[role.Id].OrderBy(c => c).ToList());
}
