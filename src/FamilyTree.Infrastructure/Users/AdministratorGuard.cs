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
///    The blindness is symmetric, and the other direction fails OPEN: ExecuteUpdateAsync,
///    ExecuteDeleteAsync and raw SQL apply straight at the database and never touch the tracker,
///    so an already-loaded entity keeps its stale values and the guard would happily count an
///    administrator that a bulk `UPDATE ... SET is_active = false` has already removed. Any
///    mutation this guard protects must therefore go through tracked entities — never a bulk
///    update or raw-SQL path. (The solution currently contains no such call.)
///
/// 3. Tenant scope comes from Role.TenantId and ApplicationUser.TenantId — UserRole has no
///    tenant column. Tenant is re-checked in memory rather than relying on the query filter
///    alone, because Added entities and rows a caller loaded with IgnoreQueryFilters are in the
///    tracker too, and the filter never saw them.
///
/// 4. Reading is not enough: check-then-save is a race. Two requests each deactivating a
///    different one of the last two administrators would each see the other still active, both
///    pass, and both save — zero administrators, recoverable only by hand. The guard therefore
///    serializes per tenant on a PostgreSQL advisory lock held for the rest of the caller's
///    transaction, so the second request blocks until the first has committed and then re-reads
///    the state the first produced.
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

        await SerializeOnTenantAsync(tenantId, ct);

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

        // Every in-tenant role id, including Added ones that have no rows yet — those simply
        // match nothing here, and their grants and assignments are already in the tracker.
        // (A List, not the HashSet, because EF translates List.Contains to an IN clause.)
        var roleIdFilter = tenantRoleIds.ToList();
        await context.RolePermissions
            .Where(rp => roleIdFilter.Contains(rp.RoleId))
            .LoadAsync(ct);
        await context.UserRoles
            .Where(ur => roleIdFilter.Contains(ur.RoleId))
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
    /// Blocks until this tenant's check-then-save sequence can run alone.
    ///
    /// pg_advisory_xact_lock is released when the surrounding transaction ends, which is exactly
    /// the window that needs protecting — read, decide, save, commit — and means a crashed or
    /// abandoned request cannot leave the lock held. It also means the lock is worthless without
    /// a transaction: outside one, PostgreSQL commits the SELECT immediately and the lock is
    /// dropped before the guard has read anything. A caller with no ambient transaction has
    /// therefore not met the contract, and that is a programming error, not a conflict — so it
    /// throws rather than silently proceeding unlocked. Fail closed.
    ///
    /// One further assumption is invisible from here and load-bearing: the re-reads that follow
    /// the lock only observe what an earlier transaction committed under READ COMMITTED, this
    /// connection's default. Under REPEATABLE READ or SERIALIZABLE the snapshot is taken at the
    /// transaction's first statement — before the lock was granted — so every query below would
    /// return the pre-lock state, the second caller would decide on data the first has already
    /// superseded, the lock would become decorative, and the TOCTOU this whole design exists to
    /// close would reopen with no test failing. If the isolation level of the callers'
    /// transactions is ever raised, this guard must be revisited.
    /// </summary>
    private async Task SerializeOnTenantAsync(Guid tenantId, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                $"{nameof(IAdministratorGuard)} must be called inside a transaction on the same " +
                "DbContext that stages the change and then saves it. Its per-tenant lock is " +
                "transaction-scoped, so without a transaction the check-then-save race it exists " +
                "to close would be left open. Begin a transaction, stage the change, call the " +
                "guard, save, then commit.");

        // The advisory lock namespace is a single bigint, so the tenant GUID is folded into one
        // by reinterpreting its first 8 bytes. Two tenants could in principle collide; that is
        // harmless. A collision means the two tenants briefly serialize against each other —
        // a contention cost on an already rare administrative path, never a wrong answer, since
        // every query below is still tenant-scoped.
        var lockKey = BitConverter.ToInt64(tenantId.ToByteArray(), 0);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", ct);
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
