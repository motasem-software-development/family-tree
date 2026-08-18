using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.Users;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Common;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Authorization;

[Collection("postgres")]
public sealed class AdministratorGuardTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();

        // The guard is tenant-scoped, so the scope must name a tenant the way an authenticated
        // request's claims would. Without this every tenant-filtered query returns nothing and
        // the guard would throw vacuously — passing the rejection tests for the wrong reason.
        _tenantId = await _factory.SeededTenantIdAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task The_guard_passes_while_an_active_administrator_remains()
    {
        await using var scope = _factory.CreateTenantScope(_tenantId);
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();

        var act = () => guard.EnsureAdministratorRemainsAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_guard_rejects_deactivating_the_only_administrator()
    {
        await using var scope = _factory.CreateTenantScope(_tenantId);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();

        var admin = await context.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());

        // Before staging anything the guard must be satisfied — otherwise the rejection below
        // would prove nothing about whether the pending change was seen.
        await guard.Invoking(g => g.EnsureAdministratorRemainsAsync()).Should().NotThrowAsync();

        admin.IsActive = false;

        // Staged but not saved: the guard must see the pending change, which is what makes it
        // usable as a pre-save gate rather than an after-the-fact audit.
        var act = () => guard.EnsureAdministratorRemainsAsync();

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("LAST_ADMINISTRATOR");
    }

    [Fact]
    public async Task The_guard_rejects_stripping_the_only_administrators_roles()
    {
        await using var scope = _factory.CreateTenantScope(_tenantId);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();

        // UserRole carries no tenant column and therefore no query filter, so this clears every
        // assignment in the database. Safe only because the seeded fixture holds one tenant —
        // do not copy this line into a multi-tenant test.
        context.UserRoles.RemoveRange(await context.UserRoles.ToListAsync());

        var act = () => guard.EnsureAdministratorRemainsAsync();

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("LAST_ADMINISTRATOR");
    }

    [Fact]
    public async Task A_role_named_something_else_still_counts_as_administration()
    {
        await using var scope = _factory.CreateTenantScope(_tenantId);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();
        var now = TimeProvider.System.GetUtcNow();

        var admin = await context.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());

        // A custom role holding the two recovery permissions, with a name matching no system
        // role. If the guard ever regresses to a name check, this test fails.
        var custom = Role.Create(admin.TenantId, "أمناء العائلة", null, now);
        context.Roles.Add(custom);

        var permissionIds = await context.Permissions
            .Where(p => p.Code == Permissions.User.Edit || p.Code == Permissions.Role.Edit)
            .Select(p => p.Id)
            .ToListAsync();

        permissionIds.Should().HaveCount(2);

        foreach (var permissionId in permissionIds)
            context.RolePermissions.Add(RolePermission.Create(custom.Id, permissionId));

        context.UserRoles.RemoveRange(await context.UserRoles.ToListAsync());
        context.UserRoles.Add(UserRole.Create(admin.Id, custom.Id));

        var act = () => guard.EnsureAdministratorRemainsAsync();

        await act.Should().NotThrowAsync();
    }
}
