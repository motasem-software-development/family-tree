using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Persistence;

public sealed class TenantIsolationTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private async Task<(Guid TenantA, Guid TenantB)> SeedTwoTenantsAsync()
    {
        // Seeding runs through an unfiltered context; production seeds exactly one tenant,
        // but the isolation guarantee is untestable with fewer than two (spec §6).
        await using var context = ContextFor(Guid.Empty);

        var a = Tenant.Create("Al-Saqqa Family", "al-saqqa", Now);
        var b = Tenant.Create("Al-Hassan Family", "al-hassan", Now);
        context.Tenants.AddRange(a, b);

        context.FamilyTrees.AddRange(
            FamilyTreeAggregate.Create(a.Id, "عائلة السقا", Now),
            FamilyTreeAggregate.Create(b.Id, "عائلة الحسن", Now));

        context.Roles.AddRange(
            Role.CreateSystem(a.Id, "Super Admin", null, Now),
            Role.CreateSystem(b.Id, "Super Admin", null, Now));

        await context.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task A_tenant_sees_only_its_own_family_tree()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync();

        await using var context = ContextFor(tenantA);
        var trees = await context.FamilyTrees.ToListAsync();

        trees.Should().ContainSingle();
        trees[0].TenantId.Should().Be(tenantA);
        trees[0].Name.Should().Be("عائلة السقا");
    }

    [Fact]
    public async Task Fetching_another_tenants_tree_by_its_exact_id_returns_null()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        Guid foreignTreeId;
        await using (var unfiltered = ContextFor(Guid.Empty))
        {
            foreignTreeId = await unfiltered.FamilyTrees
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantB)
                .Select(x => x.Id)
                .SingleAsync();
        }

        await using var context = ContextFor(tenantA);
        var found = await context.FamilyTrees.FirstOrDefaultAsync(x => x.Id == foreignTreeId);

        // Null is what lets the endpoint layer answer 404 rather than 403 — a 403 would
        // confirm the id exists, which is itself a disclosure.
        found.Should().BeNull();
    }

    [Fact]
    public async Task Roles_are_scoped_to_the_requesting_tenant()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync();

        await using var context = ContextFor(tenantA);
        var roles = await context.Roles.ToListAsync();

        roles.Should().ContainSingle().Which.TenantId.Should().Be(tenantA);
    }

    [Fact]
    public async Task An_unauthenticated_context_sees_no_tenant_owned_rows()
    {
        await SeedTwoTenantsAsync();

        await using var context = ContextFor(Guid.Empty);

        // Fails closed: an empty tenant id matches no row rather than matching every row.
        (await context.FamilyTrees.CountAsync()).Should().Be(0);
        (await context.Roles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_permission_catalog_is_visible_regardless_of_tenant()
    {
        await SeedTwoTenantsAsync();

        await using (var seed = ContextFor(Guid.Empty))
        {
            seed.Permissions.Add(Permission.Create(Permissions.Member.View, "View members", Now));
            await seed.SaveChangesAsync();
        }

        await using var context = ContextFor(Guid.CreateVersion7());
        (await context.Permissions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task One_tenant_cannot_own_two_family_trees()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync();

        await using var context = ContextFor(Guid.Empty);
        context.FamilyTrees.Add(FamilyTreeAggregate.Create(tenantA, "A Second Tree", Now));

        var act = () => context.SaveChangesAsync();

        // BR-001 enforced by the unique index on tenant_id, not by service code.
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
