using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Authorization;
using FamilyTree.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using FamilyTree.Infrastructure.Identity;

namespace FamilyTree.Api.IntegrationTests.Persistence;

public sealed class DatabaseSeederTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly SeedOptions Options = new()
    {
        TenantName = "Al-Saqqa Family",
        TenantSlug = "al-saqqa",
        FamilyTreeName = "عائلة السقا",
        AdminEmail = "admin@example.com",
        AdminPassword = "Str0ng!Seed#Password"
    };

    private async Task RunSeederAsync()
    {
        await using var context = ContextFor(Guid.Empty);
        var hasher = new PasswordHasher<ApplicationUser>();
        var seeder = new DatabaseSeeder(context, hasher, Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);
        await seeder.SeedAsync();
    }

    [Fact]
    public async Task Seeds_one_tenant_one_tree_the_full_catalog_and_four_system_roles()
    {
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);

        (await context.Tenants.CountAsync()).Should().Be(1);
        (await context.FamilyTrees.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await context.Permissions.CountAsync()).Should().Be(Permissions.All.Count);
        (await context.Roles.IgnoreQueryFilters().CountAsync()).Should().Be(4);
        (await context.Roles.IgnoreQueryFilters().CountAsync(r => r.IsSystem)).Should().Be(4);
    }

    [Fact]
    public async Task Grants_the_super_admin_role_every_permission_in_the_catalog()
    {
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);
        var superAdmin = await context.Roles.IgnoreQueryFilters()
            .SingleAsync(r => r.Name == SystemRoles.SuperAdmin);

        var granted = await context.RolePermissions.CountAsync(rp => rp.RoleId == superAdmin.Id);

        granted.Should().Be(Permissions.All.Count);
    }

    [Fact]
    public async Task Creates_the_admin_user_bound_to_the_tenant_with_the_super_admin_role()
    {
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);
        var tenantId = await context.Tenants.Select(t => t.Id).SingleAsync();
        var user = await context.Users.IgnoreQueryFilters().SingleAsync();

        user.Email.Should().Be("admin@example.com");
        user.TenantId.Should().Be(tenantId);
        user.IsActive.Should().BeTrue();
        user.PasswordHash.Should().NotBeNullOrWhiteSpace();
        user.PasswordHash.Should().NotContain("Str0ng!Seed#Password", "the password is hashed, never stored");

        var superAdmin = await context.Roles.IgnoreQueryFilters()
            .SingleAsync(r => r.Name == SystemRoles.SuperAdmin);
        (await context.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == superAdmin.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Running_the_seeder_twice_changes_nothing()
    {
        await RunSeederAsync();
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);

        (await context.Tenants.CountAsync()).Should().Be(1);
        (await context.Users.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await context.Roles.IgnoreQueryFilters().CountAsync()).Should().Be(4);
        (await context.Permissions.CountAsync()).Should().Be(Permissions.All.Count);
    }

    [Fact]
    public async Task Viewer_role_receives_only_read_permissions()
    {
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);
        var viewer = await context.Roles.IgnoreQueryFilters().SingleAsync(r => r.Name == SystemRoles.Viewer);

        var codes = await (from rp in context.RolePermissions
                           join p in context.Permissions on rp.PermissionId equals p.Id
                           where rp.RoleId == viewer.Id
                           select p.Code).ToListAsync();

        codes.Should().BeEquivalentTo("FamilyTree.View", "Member.View");
    }

    [Fact]
    public async Task SeedAsync_throws_when_the_configured_slug_does_not_match_the_imported_family_tenant()
    {
        // DatabaseTestBase.InitializeAsync already ran every migration, including
        // SeedImportedFamily, which creates the "al-saqqa" tenant that owns the imported
        // 349-member family tree. A seeder configured for a different slug must refuse to
        // start rather than silently create a second, empty tenant and orphan that data.
        var mismatched = new SeedOptions
        {
            TenantName = "Some Other Family",
            TenantSlug = "some-other-slug",
            FamilyTreeName = "Some Other Tree",
            AdminEmail = "admin@example.com",
            AdminPassword = "Str0ng!Seed#Password"
        };

        await using var context = ContextFor(Guid.Empty);
        var hasher = new PasswordHasher<ApplicationUser>();
        var seeder = new DatabaseSeeder(
            context, hasher, Microsoft.Extensions.Options.Options.Create(mismatched), TimeProvider.System);

        var act = async () => await seeder.SeedAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("al-saqqa").And.Contain("some-other-slug");

        // And no second tenant was created — the guard runs before any tenant does.
        (await context.Tenants.CountAsync(t => t.Slug == "some-other-slug")).Should().Be(0);
    }

    [Fact]
    public async Task SeedAsync_does_not_throw_when_the_configured_slug_matches_the_imported_family_tenant()
    {
        // The default configuration (Options above, slug "al-saqqa") must behave exactly as it
        // did before this guard existed: the migration's tenant and the seeder's configured
        // slug agree, so the seeder reuses it without complaint.
        var act = async () => await RunSeederAsync();

        await act.Should().NotThrowAsync();

        await using var context = ContextFor(Guid.Empty);
        (await context.Tenants.CountAsync()).Should().Be(1);
    }
}
