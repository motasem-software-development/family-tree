using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.FamilyMembers;

/// <summary>
/// The CTE is the only thing standing between a mis-click and a tree that no longer
/// terminates, so it is tested directly rather than only through the service.
/// </summary>
public sealed class CycleCheckQueryTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    /// <summary>Seeds a tenant, a tree, and a chain of members, returning their ids root-first.</summary>
    private async Task<(Guid TenantId, Guid[] Chain)> SeedChainAsync(string slug, params string[] names)
    {
        await using var context = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var tree = FamilyTreeAggregate.Create(tenant.Id, $"Tree {slug}", Now);
        context.FamilyTrees.Add(tree);
        await context.SaveChangesAsync();

        var ids = new List<Guid>();
        Guid? parentId = null;
        foreach (var name in names)
        {
            var member = FamilyMember.Create(tenant.Id, tree.Id, parentId, name, Now);
            context.FamilyMembers.Add(member);
            await context.SaveChangesAsync();
            ids.Add(member.Id);
            parentId = member.Id;
        }

        return (tenant.Id, [.. ids]);
    }

    [Fact]
    public async Task Reports_a_cycle_when_the_target_is_a_direct_child()
    {
        var (tenantId, chain) = await SeedChainAsync("cyc-a", "سليمان", "فارس");
        await using var context = ContextFor(tenantId);

        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, tenantId, chain[0], chain[1], default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Reports_a_cycle_when_the_target_is_a_distant_descendant()
    {
        var (tenantId, chain) = await SeedChainAsync("cyc-b", "سليمان", "فارس", "عمر", "خالد");
        await using var context = ContextFor(tenantId);

        // Moving the root under its own great-grandchild.
        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, tenantId, chain[0], chain[3], default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Reports_a_cycle_when_the_target_is_the_member_itself()
    {
        var (tenantId, chain) = await SeedChainAsync("cyc-c", "سليمان");
        await using var context = ContextFor(tenantId);

        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, tenantId, chain[0], chain[0], default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Reports_no_cycle_for_a_move_into_an_unrelated_branch()
    {
        var (tenantId, chain) = await SeedChainAsync("cyc-d", "سليمان", "فارس", "عمر");
        await using var context = ContextFor(tenantId);

        // Moving the deepest member under the root: walking upward from the root, the chain
        // never reaches the member being moved.
        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, tenantId, chain[2], chain[0], default);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Reports_no_cycle_when_the_chain_belongs_to_another_tenant()
    {
        var (_, chain) = await SeedChainAsync("cyc-e", "سليمان", "فارس");
        var (otherTenantId, _) = await SeedChainAsync("cyc-f", "داوود");
        await using var context = ContextFor(otherTenantId);

        // The walk is tenant-scoped: another tenant's ancestry is invisible, so the query
        // finds nothing to walk rather than climbing into it.
        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, otherTenantId, chain[0], chain[1], default);

        result.Should().BeFalse();
    }
}
