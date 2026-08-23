using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public async Task Does_not_climb_into_another_tenant_through_a_corrupted_parent_id()
    {
        // Tenant B: the ancestry the walk must never be able to reach into.
        var (_, chainB) = await SeedChainAsync("cyc-g", "داوود", "زياد");

        // Tenant A: a normal chain, seeded the ordinary way.
        var (tenantAId, _) = await SeedChainAsync("cyc-h", "سليمان", "فارس");

        await using var tenantAReadContext = ContextFor(tenantAId);
        var treeAId = await tenantAReadContext.FamilyTrees.Select(t => t.Id).SingleAsync();

        // A member whose parent_id points into another tenant is exactly what the service
        // layer's move validation exists to prevent: it looks the proposed parent up scoped to
        // the caller's tenant and refuses one it cannot find there, so going through the service
        // can never produce a row like this. Two composite foreign keys — fk_member_parent
        // (parent_id, family_tree_id) -> (id, family_tree_id), and family_tree_id/tenant_id's own
        // FK to family_trees — also make it physically unrepresentable through a normal EF
        // Add + SaveChanges, by design (spec §3.3). The only way to put a corrupted row like this
        // on disk — the kind a restored backup or a future relaxation of those constraints could
        // still produce — is to disable FK enforcement for this one insert, which is what
        // session_replication_role does. ContextFor(Guid.Empty) is used only as the connection
        // for that insert, the same insert-only pattern SeedChainAsync itself relies on: the
        // tenant query filter governs reads, not writes, so which tenant id the context carries
        // is irrelevant here.
        var corrupted = FamilyMember.Create(tenantAId, treeAId, chainB[1], "يوسف", Now);
        await using (var seedContext = ContextFor(Guid.Empty))
        {
            seedContext.FamilyMembers.Add(corrupted);

            // session_replication_role is connection-scoped, so the connection has to be opened
            // explicitly here and kept open across the SET and the SaveChanges — otherwise EF
            // opens its own connection lazily inside SaveChanges and the setting never applies.
            await seedContext.Database.OpenConnectionAsync();
            try
            {
                await seedContext.Database.ExecuteSqlRawAsync("SET session_replication_role = replica;");
                await seedContext.SaveChangesAsync();
            }
            finally
            {
                await seedContext.Database.ExecuteSqlRawAsync("SET session_replication_role = DEFAULT;");
                await seedContext.Database.CloseConnectionAsync();
            }
        }

        await using var context = ContextFor(tenantAId);

        // Walking upward from the corrupted member climbs into tenant B (زياد, then داوود) only
        // if the recursive term's own tenant_id predicate is missing. member_id here is داوود:
        // reachable only by crossing that boundary, so a correctly tenant-pinned walk must
        // report no cycle.
        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, tenantAId, chainB[0], corrupted.Id, default);

        result.Should().BeFalse();
    }
}
