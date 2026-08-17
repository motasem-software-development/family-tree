using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Authorization;
using FamilyTree.Infrastructure.Identity;

namespace FamilyTree.Api.IntegrationTests.Authorization;

public sealed class PermissionResolverTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private async Task<(Guid TenantId, Guid UserId)> SeedUserWithRolesAsync(
        params (string RoleName, string[] Permissions)[] roles)
    {
        await using var context = ContextFor(Guid.Empty);

        // Slug deliberately does not collide with the "al-saqqa" slug the SeedImportedFamily
        // migration inserts before every test runs (ix_tenants_slug is unique).
        var tenant = Tenant.Create("Al-Saqqa Family", "perm-al-saqqa", Now);
        context.Tenants.Add(tenant);

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Email = "admin@example.com",
            UserName = "admin@example.com",
            CreatedAt = Now
        };
        context.Users.Add(user);

        var catalog = Permissions.All
            .Select(code => Permission.Create(code, null, Now))
            .ToDictionary(p => p.Code);
        context.Permissions.AddRange(catalog.Values);

        foreach (var (roleName, permissionCodes) in roles)
        {
            var role = Role.Create(tenant.Id, roleName, null, Now);
            context.Roles.Add(role);
            context.UserRoles.Add(UserRole.Create(user.Id, role.Id));
            context.RolePermissions.AddRange(
                permissionCodes.Select(code => RolePermission.Create(role.Id, catalog[code].Id)));
        }

        await context.SaveChangesAsync();
        return (tenant.Id, user.Id);
    }

    [Fact]
    public async Task Resolves_the_permissions_granted_by_a_users_single_role()
    {
        var (tenantId, userId) = await SeedUserWithRolesAsync(
            ("Editor", [Permissions.Member.View, Permissions.Member.Create]));

        await using var context = ContextFor(tenantId);
        var resolver = new PermissionResolver(context, new StubTenantContext(tenantId, userId));

        var permissions = await resolver.GetPermissionsAsync(userId);

        permissions.Should().BeEquivalentTo("Member.View", "Member.Create");
    }

    [Fact]
    public async Task Resolves_the_union_of_multiple_roles_without_duplicates()
    {
        var (tenantId, userId) = await SeedUserWithRolesAsync(
            ("Viewer", [Permissions.Member.View]),
            ("Mover",  [Permissions.Member.View, Permissions.Member.Move]));

        await using var context = ContextFor(tenantId);
        var resolver = new PermissionResolver(context, new StubTenantContext(tenantId, userId));

        var permissions = await resolver.GetPermissionsAsync(userId);

        permissions.Should().BeEquivalentTo("Member.View", "Member.Move");
        permissions.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Returns_empty_for_a_user_with_no_roles()
    {
        var (tenantId, userId) = await SeedUserWithRolesAsync();

        await using var context = ContextFor(tenantId);
        var resolver = new PermissionResolver(context, new StubTenantContext(tenantId, userId));

        (await resolver.GetPermissionsAsync(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_empty_for_a_user_id_belonging_to_another_tenant()
    {
        var (_, userId) = await SeedUserWithRolesAsync(("Editor", [Permissions.Member.View]));
        var otherTenant = Guid.CreateVersion7();

        await using var context = ContextFor(otherTenant);
        var resolver = new PermissionResolver(context, new StubTenantContext(otherTenant, userId));

        // The tenant predicate excludes the other tenant's roles, so no permission leaks across.
        (await resolver.GetPermissionsAsync(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_explicit_tenant_overload_resolves_permissions_without_an_ambient_tenant()
    {
        var (tenantId, userId) = await SeedUserWithRolesAsync(
            ("Editor", [Permissions.Member.View, Permissions.Member.Create]));

        // Models login: no authenticated principal yet, so the ambient tenant is empty.
        await using var context = ContextFor(Guid.Empty);
        var resolver = new PermissionResolver(context, new StubTenantContext(Guid.Empty, Guid.Empty));

        var permissions = await resolver.GetPermissionsAsync(userId, tenantId);

        permissions.Should().BeEquivalentTo("Member.View", "Member.Create");
    }

    [Fact]
    public async Task The_explicit_tenant_overload_returns_empty_when_the_tenant_does_not_match()
    {
        var (_, userId) = await SeedUserWithRolesAsync(("Editor", [Permissions.Member.View]));

        await using var context = ContextFor(Guid.Empty);
        var resolver = new PermissionResolver(context, new StubTenantContext(Guid.Empty, Guid.Empty));

        (await resolver.GetPermissionsAsync(userId, Guid.CreateVersion7())).Should().BeEmpty();
    }
}
