using FamilyTree.Application.Common;
using FamilyTree.Domain.Authentication;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantContext tenantContext)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    /// <summary>
    /// Read once per context instance and referenced by the global query filters below.
    /// EF re-evaluates this field per query, so filters follow the request's tenant.
    /// The context is registered scoped (not pooled) precisely so this stays correct.
    /// </summary>
    private readonly Guid _tenantId = tenantContext.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<FamilyTreeAggregate> FamilyTrees => Set<FamilyTreeAggregate>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<Permission> Permissions => Set<Permission>();
    // `new` is required: IdentityDbContext already declares Roles (DbSet&lt;IdentityRole&lt;Guid&gt;&gt;)
    // and UserRoles (DbSet&lt;IdentityUserRole&lt;Guid&gt;&gt;). Ours are deliberately different,
    // tenant-scoped, permission-backed types — see ApplicationUser's remarks.
    public new DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public new DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // IdentityDbContext's own OnModelCreating calls ToTable() with PascalCase names
        // (AspNetUsers, AspNetRoles, ...). EFCore.NamingConventions only rewrites
        // convention-derived names, not names set explicitly via Fluent API, so those
        // six tables must be renamed to snake_case explicitly here.
        // Identity also names two of its own indexes explicitly (RoleNameIndex, EmailIndex,
        // UserNameIndex), which the naming convention leaves untouched for the same reason.
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("asp_net_users");
            // Unique: LoginAsync looks users up globally by NormalizedEmail (IgnoreQueryFilters,
            // no tenant in scope yet), so two tenants sharing an email would otherwise make
            // login pick a tenant nondeterministically.
            b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("email_index").IsUnique();
            b.HasIndex(u => u.NormalizedUserName).HasDatabaseName("user_name_index");
            b.HasIndex(u => u.TenantId);
            b.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<IdentityRole<Guid>>(b =>
        {
            b.ToTable("asp_net_roles");
            b.HasIndex(r => r.NormalizedName).HasDatabaseName("role_name_index");
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("asp_net_user_roles");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("asp_net_role_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens");

        // Global query filters — the reason a forgotten Where clause is not a vulnerability.
        builder.Entity<FamilyTreeAggregate>().HasQueryFilter(x => x.TenantId == _tenantId);
        builder.Entity<Role>().HasQueryFilter(x => x.TenantId == _tenantId);
        builder.Entity<RefreshToken>().HasQueryFilter(x => x.TenantId == _tenantId);
        builder.Entity<ApplicationUser>().HasQueryFilter(x => x.TenantId == _tenantId);
        builder.Entity<FamilyMember>().HasQueryFilter(x => x.TenantId == _tenantId);

        // Tenant and Permission are deliberately unfiltered: Tenant is the filter's own subject,
        // and the permission catalog is system-level rather than tenant-owned.
    }
}
