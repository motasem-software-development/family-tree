using FamilyTree.Application.Common;
using FamilyTree.Application.Users;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Common;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Users;

/// <summary>
/// A tenant must not be able to strip itself of administration (design spec §4.9).
///
/// Three decisions here are load-bearing:
///
/// 1. The rule is expressed in PERMISSIONS, not role names. §4.3 says authorization is never
///    role-name-based; a name check would also be defeated by renaming Super Admin or by a
///    custom role that is Super Admin in all but name.
///
/// 2. The evaluation runs over the state the pending save will produce, not over the state in
///    the database. LINQ over a DbSet becomes SQL, and SQL cannot see un-saved changes tracked
///    in memory — a guard written that way reports safety it has not checked. So each query
///    here only *hydrates* the change tracker (Load, no projection, no AsNoTracking), and the
///    decision is taken in memory over ChangeTracker entries: Deleted rows drop out, Added rows
///    join in, and Modified entities are read at their current values.
///
/// 3. Tenant scope comes from Role.TenantId and ApplicationUser.TenantId — UserRole has no
///    tenant column. Tenant is re-checked in memory rather than relying on the query filter
///    alone, because Added entities and rows a caller loaded with IgnoreQueryFilters are in the
///    tracker too, and the filter never saw them.
/// </summary>
public sealed class AdministratorGuard(
    ApplicationDbContext context, ITenantContext tenant) : IAdministratorGuard
{
    /// <summary>The pair required to undo any change this guard protects against.</summary>
    private static readonly string[] RecoveryPermissions =
        [Permissions.User.Edit, Permissions.Role.Edit];

    public async Task EnsureAdministratorRemainsAsync(CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId;

        // The catalog is system-level and immutable, so a plain query is safe here.
        var recoveryPermissionIds = await context.Permissions
            .Where(p => RecoveryPermissions.Contains(p.Code))
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (recoveryPermissionIds.Count != RecoveryPermissions.Length)
            throw Rejected();

        // Tenant user counts are administrative in scale; loading them is what makes a pending
        // IsActive change visible, which a Select projection would have skipped entirely.
        await context.Users.LoadAsync(ct);
        var activeUserIds = Pending<ApplicationUser>()
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .Select(u => u.Id)
            .ToHashSet();

        if (activeUserIds.Count == 0) throw Rejected();

        await context.Roles.LoadAsync(ct);
        var tenantRoleIds = Pending<Role>()
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.Id)
            .ToHashSet();

        if (tenantRoleIds.Count == 0) throw Rejected();

        // Roles staged as Added have no rows yet; their grants and assignments are already in
        // the tracker, so only the persisted ones need loading.
        var persistedRoleIds = tenantRoleIds.ToList();
        await context.RolePermissions
            .Where(rp => persistedRoleIds.Contains(rp.RoleId))
            .LoadAsync(ct);
        await context.UserRoles
            .Where(ur => persistedRoleIds.Contains(ur.RoleId))
            .LoadAsync(ct);

        var recoveryGrantsByRole = Pending<RolePermission>()
            .Where(rp => tenantRoleIds.Contains(rp.RoleId)
                && recoveryPermissionIds.Contains(rp.PermissionId))
            .GroupBy(rp => rp.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(rp => rp.PermissionId).ToHashSet());

        // Accumulated across a user's roles, not per role: holding User.Edit through one role
        // and Role.Edit through another is still a user who can restore administration.
        var survives = Pending<UserRole>()
            .Where(ur => activeUserIds.Contains(ur.UserId) && tenantRoleIds.Contains(ur.RoleId))
            .GroupBy(ur => ur.UserId)
            .Any(assignments =>
            {
                var held = assignments
                    .SelectMany(ur => recoveryGrantsByRole.TryGetValue(ur.RoleId, out var granted)
                        ? granted
                        : [])
                    .ToHashSet();
                return recoveryPermissionIds.All(held.Contains);
            });

        if (!survives) throw Rejected();
    }

    /// <summary>
    /// The tracked entities of type <typeparamref name="T"/> as they will exist after the save:
    /// Added and Modified included at their current values, Deleted and Detached excluded.
    /// </summary>
    private IEnumerable<T> Pending<T>() where T : class =>
        context.ChangeTracker.Entries<T>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
            .Select(e => e.Entity);

    private static ConflictException Rejected() => new(
        "LAST_ADMINISTRATOR",
        "This change would leave the account with no one able to manage users and roles.");
}
