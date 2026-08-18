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
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();
        await using var tx = await context.Database.BeginTransactionAsync();

        var act = () => guard.EnsureAdministratorRemainsAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_guard_refuses_to_run_outside_a_transaction()
    {
        // Its per-tenant lock is transaction-scoped, so running without a transaction would
        // leave the check-then-save race open. That is a caller error, not a conflict: it must
        // fail loudly rather than report a safety it did not enforce.
        await using var scope = _factory.CreateTenantScope(_tenantId);
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();

        var act = () => guard.EnsureAdministratorRemainsAsync();

        // ConflictException derives from DomainException, not from InvalidOperationException,
        // so ThrowExactly here also proves the failure is not dressed up as LAST_ADMINISTRATOR.
        (await act.Should().ThrowExactlyAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("transaction");
    }

    [Fact]
    public async Task The_guard_serializes_concurrent_callers_for_the_same_tenant()
    {
        // Without this, two requests each deactivating a different one of the last two
        // administrators would both read the other as still active, both pass, and both save.
        await using var first = _factory.CreateTenantScope(_tenantId);
        var firstContext = first.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var firstTx = await firstContext.Database.BeginTransactionAsync();
        await first.ServiceProvider.GetRequiredService<IAdministratorGuard>()
            .EnsureAdministratorRemainsAsync();

        await using var second = _factory.CreateTenantScope(_tenantId);
        var secondContext = second.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var secondTx = await secondContext.Database.BeginTransactionAsync();
        var blocked = second.ServiceProvider.GetRequiredService<IAdministratorGuard>()
            .EnsureAdministratorRemainsAsync();

        var firstToFinish = await Task.WhenAny(blocked, Task.Delay(TimeSpan.FromSeconds(1)));
        firstToFinish.Should().NotBeSameAs(blocked,
            "the second caller must wait while the first holds the tenant lock");

        // Ending the first transaction releases the lock, and the second proceeds.
        await firstTx.RollbackAsync();
        await blocked.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task The_guard_rejects_deactivating_the_only_administrator()
    {
        await using var scope = _factory.CreateTenantScope(_tenantId);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();
        await using var tx = await context.Database.BeginTransactionAsync();

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
        await using var tx = await context.Database.BeginTransactionAsync();

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
        await using var tx = await context.Database.BeginTransactionAsync();

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

    [Fact]
    public async Task The_guard_rejects_revoking_a_recovery_permission_from_the_only_administrators_role()
    {
        // The tracker state a role EDIT produces, and the only one the other tests here never
        // stage: RoleService.UpdateAsync replaces a role's grants with RemoveRange + re-Add,
        // so a dropped permission arrives as a *Deleted* RolePermission — not the Deleted
        // UserRole, Added RolePermission or Modified ApplicationUser covered above.
        await using var scope = _factory.CreateTenantScope(_tenantId);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();
        await using var tx = await context.Database.BeginTransactionAsync();

        var admin = await context.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
        var adminRoleIds = await context.UserRoles
            .Where(ur => ur.UserId == admin.Id).Select(ur => ur.RoleId).ToListAsync();

        var roleEditId = await context.Permissions
            .Where(p => p.Code == Permissions.Role.Edit).Select(p => p.Id).SingleAsync();

        // Satisfied before staging, so the rejection below is attributable to the pending
        // deletion rather than to some pre-existing gap in the fixture.
        await guard.Invoking(g => g.EnsureAdministratorRemainsAsync()).Should().NotThrowAsync();

        var grants = await context.RolePermissions
            .Where(rp => adminRoleIds.Contains(rp.RoleId) && rp.PermissionId == roleEditId)
            .ToListAsync();
        grants.Should().NotBeEmpty();
        context.RolePermissions.RemoveRange(grants);

        var act = () => guard.EnsureAdministratorRemainsAsync();

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("LAST_ADMINISTRATOR");
    }
}
