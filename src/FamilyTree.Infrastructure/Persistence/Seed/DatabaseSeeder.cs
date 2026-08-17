using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FamilyTree.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the single V1 tenant, its family tree, the permission catalog, the four system
/// roles, and the first Super Admin. Idempotent: safe to run on every startup.
/// </summary>
public sealed class DatabaseSeeder(
    ApplicationDbContext context,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IOptions<SeedOptions> options,
    TimeProvider timeProvider)
{
    private readonly SeedOptions _options = options.Value;

    /// <summary>
    /// The tenant slug the SeedImportedFamily migration (20260817131959) hardcodes when it
    /// creates — or reuses — the tenant that owns the 349-member imported family tree. Must
    /// stay in sync with that migration's TENANT_SLUG literal.
    /// </summary>
    private const string ImportedFamilyTenantSlug = "al-saqqa";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // Must run before SeedTenantAsync — that is what "before any tenant is created" means
        // here: if this throws, no second tenant ever comes into existence.
        await GuardAgainstTenantSlugMismatchAsync(ct);

        await SeedPermissionCatalogAsync(now, ct);
        var tenant = await SeedTenantAsync(now, ct);
        await SeedFamilyTreeAsync(tenant.Id, now, ct);
        var roleIds = await SeedSystemRolesAsync(tenant.Id, now, ct);
        await SeedAdminUserAsync(tenant.Id, roleIds[SystemRoles.SuperAdmin], now, ct);

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Refuses to start rather than silently orphaning the imported family tree. If
    /// SeedImportedFamily already created (or reused) a tenant under its hardcoded default slug
    /// and the configured SEED_TENANT_SLUG points somewhere else, continuing would create a
    /// second, empty tenant under the configured slug, seed the admin user there, and leave the
    /// 349 real members sitting under a tenant nobody can log into — with the app showing an
    /// empty tree and no error anywhere. This is a misconfiguration, not something to guess a
    /// fix for automatically, so it fails loudly instead.
    /// </summary>
    private async Task GuardAgainstTenantSlugMismatchAsync(CancellationToken ct)
    {
        if (_options.TenantSlug == ImportedFamilyTenantSlug) return;

        var importedFamilyTenantExists = await context.Tenants
            .AnyAsync(t => t.Slug == ImportedFamilyTenantSlug, ct);

        if (!importedFamilyTenantExists) return;

        throw new InvalidOperationException(
            $"Seed configuration mismatch: SEED_TENANT_SLUG is configured as " +
            $"'{_options.TenantSlug}', but a tenant with slug '{ImportedFamilyTenantSlug}' " +
            $"already exists — the slug the SeedImportedFamily migration uses for the imported " +
            $"349-member family tree. Starting up would create a second, empty tenant under " +
            $"'{_options.TenantSlug}' and seed the admin user there, leaving the real family " +
            $"data orphaned under '{ImportedFamilyTenantSlug}' with no visible error. Fix: set " +
            $"SEED_TENANT_SLUG='{ImportedFamilyTenantSlug}' to match the seeded data, or, if " +
            $"'{_options.TenantSlug}' is intentional, manually re-point the existing " +
            $"family_trees/family_members rows at the correct tenant before starting the app. " +
            $"Refusing to start.");
    }

    private async Task SeedPermissionCatalogAsync(DateTimeOffset now, CancellationToken ct)
    {
        var existing = await context.Permissions.Select(p => p.Code).ToListAsync(ct);
        var missing = Permissions.All.Except(existing).ToList();
        if (missing.Count == 0) return;

        context.Permissions.AddRange(missing.Select(code => Permission.Create(code, null, now)));
        await context.SaveChangesAsync(ct);
    }

    private async Task<Tenant> SeedTenantAsync(DateTimeOffset now, CancellationToken ct)
    {
        var existing = await context.Tenants.FirstOrDefaultAsync(t => t.Slug == _options.TenantSlug, ct);
        if (existing is not null) return existing;

        var tenant = Tenant.Create(_options.TenantName, _options.TenantSlug, now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(ct);
        return tenant;
    }

    private async Task SeedFamilyTreeAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var exists = await context.FamilyTrees.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId, ct);
        if (exists) return;

        context.FamilyTrees.Add(FamilyTreeAggregate.Create(tenantId, _options.FamilyTreeName, now));
        await context.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<string, Guid>> SeedSystemRolesAsync(
        Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var catalog = await context.Permissions.ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        var roleIds = new Dictionary<string, Guid>();

        foreach (var (roleName, permissionCodes) in SystemRoles.Definitions)
        {
            var role = await context.Roles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == roleName, ct);

            if (role is null)
            {
                role = Role.CreateSystem(tenantId, roleName, null, now);
                context.Roles.Add(role);
                await context.SaveChangesAsync(ct);
            }

            roleIds[roleName] = role.Id;

            var alreadyGranted = await context.RolePermissions
                .Where(rp => rp.RoleId == role.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);

            var toGrant = permissionCodes
                .Select(code => catalog[code])
                .Except(alreadyGranted)
                .Select(permissionId => RolePermission.Create(role.Id, permissionId))
                .ToList();

            if (toGrant.Count > 0)
            {
                context.RolePermissions.AddRange(toGrant);
                await context.SaveChangesAsync(ct);
            }
        }

        return roleIds;
    }

    private async Task SeedAdminUserAsync(
        Guid tenantId, Guid superAdminRoleId, DateTimeOffset now, CancellationToken ct)
    {
        var normalizedEmail = _options.AdminEmail.ToUpperInvariant();

        var user = await context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Email = _options.AdminEmail,
                NormalizedEmail = normalizedEmail,
                UserName = _options.AdminEmail,
                NormalizedUserName = normalizedEmail,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = now,
                SecurityStamp = Guid.CreateVersion7().ToString()
            };
            user.PasswordHash = passwordHasher.HashPassword(user, _options.AdminPassword);

            context.Users.Add(user);
            await context.SaveChangesAsync(ct);
        }

        var hasRole = await context.UserRoles
            .AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == superAdminRoleId, ct);

        if (!hasRole)
        {
            context.UserRoles.Add(UserRole.Create(user.Id, superAdminRoleId));
            await context.SaveChangesAsync(ct);
        }
    }
}
