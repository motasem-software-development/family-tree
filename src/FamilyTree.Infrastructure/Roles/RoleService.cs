using FamilyTree.Application.Common;
using FamilyTree.Application.Roles;
using FamilyTree.Application.Users;
using FamilyTree.Contracts.Roles;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Common;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Roles;

/// <summary>
/// Roles are tenant-owned and read through the query filter, so another tenant's role id is
/// indistinguishable from a nonexistent one (design spec §4.4). Permissions are not
/// tenant-owned — the catalog is global by design.
/// </summary>
public sealed class RoleService(
    ApplicationDbContext context,
    ITenantContext tenant,
    IAdministratorGuard guard,
    TimeProvider timeProvider) : IRoleService
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
            .Select(p => new PermissionResponse(p.Code, p.Description))
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

    public async Task<RoleResponse> CreateAsync(
        SaveRoleRequest request, CancellationToken ct = default)
    {
        var permissionIds = await ResolvePermissionIdsAsync(request.Permissions, ct);
        await EnsureNameIsFreeAsync(request.Name, excludingRoleId: null, ct);

        // Role.Create validates the name and throws ROLE_NAME_REQUIRED / ROLE_NAME_TOO_LONG.
        var role = Role.Create(
            tenant.TenantId, request.Name, request.Description, timeProvider.GetUtcNow());

        context.Roles.Add(role);
        foreach (var permissionId in permissionIds)
            context.RolePermissions.Add(RolePermission.Create(role.Id, permissionId));

        await context.SaveChangesAsync(ct);
        return (await GetAsync(role.Id, ct))!;
    }

    public async Task<RoleResponse> UpdateAsync(
        Guid id, SaveRoleRequest request, CancellationToken ct = default)
    {
        // IAdministratorGuard needs an ambient transaction on this same DbContext: it takes a
        // per-tenant advisory lock held for the transaction's lifetime to close the TOCTOU
        // window where two concurrent requests each remove a different administrator.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        var role = await context.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("ROLE_NOT_FOUND", "No such role.");

        // Checked first, before permission resolution or staging, so a rejected request leaves
        // no partial effect on the tracked graph. Role.Update also calls EnsureNotSystem()
        // internally, but relying on that alone would mean permission rows had already been
        // staged by the time it fired.
        role.EnsureNotSystem();

        var permissionIds = await ResolvePermissionIdsAsync(request.Permissions, ct);
        await EnsureNameIsFreeAsync(request.Name, excludingRoleId: role.Id, ct);

        // Name and description are replaced together — the edit form submits both.
        role.Update(request.Name, request.Description, timeProvider.GetUtcNow());

        var existing = await context.RolePermissions
            .Where(rp => rp.RoleId == role.Id).ToListAsync(ct);
        context.RolePermissions.RemoveRange(existing);
        foreach (var permissionId in permissionIds)
            context.RolePermissions.Add(RolePermission.Create(role.Id, permissionId));

        // A role edit can remove User.Edit or Role.Edit from everyone who holds it — the same
        // lockout the user-facing paths guard against (spec §4.9). Staged, not saved: the guard
        // evaluates the state this request is asking for.
        await guard.EnsureAdministratorRemainsAsync(ct);

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return (await GetAsync(role.Id, ct))!;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("ROLE_NOT_FOUND", "No such role.");

        role.EnsureNotSystem();

        // Refuse rather than cascade: silently unassigning people would change what they can
        // do without anyone asking for that.
        if (await context.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, ct))
            throw new ConflictException("ROLE_IN_USE",
                "This role is still assigned to one or more users.");

        var permissions = await context.RolePermissions
            .Where(rp => rp.RoleId == role.Id).ToListAsync(ct);
        context.RolePermissions.RemoveRange(permissions);
        context.Roles.Remove(role);

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Maps codes to ids, rejecting any code the catalog does not contain. Sending a code that
    /// does not exist is a client bug, not a permission the tenant simply lacks.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolvePermissionIdsAsync(
        IReadOnlyList<string>? codes, CancellationToken ct)
    {
        var requested = (codes ?? []).Distinct().ToList();
        if (requested.Count == 0) return [];

        var found = await context.Permissions
            .Where(p => requested.Contains(p.Code))
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (found.Count != requested.Count)
            throw new DomainException("PERMISSION_NOT_FOUND", "One or more permissions do not exist.");

        return found;
    }

    private async Task EnsureNameIsFreeAsync(
        string? name, Guid? excludingRoleId, CancellationToken ct)
    {
        var trimmed = (name ?? string.Empty).Trim();
        // Left to Role.Create / Update, which produce the proper ROLE_NAME_REQUIRED error.
        if (trimmed.Length == 0) return;

        // Filtered: another tenant's role of the same name is not a collision (query filter
        // applies to context.Roles).
        var taken = await context.Roles.AnyAsync(
            r => r.Name == trimmed && (excludingRoleId == null || r.Id != excludingRoleId), ct);

        if (taken)
            throw new ConflictException("ROLE_NAME_TAKEN", "A role with that name already exists.");
    }

    private static RoleResponse Map(
        Role role, ILookup<Guid, string> permissions, Dictionary<Guid, int> counts) =>
        new(role.Id, role.Name, role.Description, role.IsSystem,
            counts.GetValueOrDefault(role.Id),
            permissions[role.Id].OrderBy(c => c).ToList());
}
