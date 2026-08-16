using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Persistence;

/// <summary>
/// Proves the database itself refuses the states the design forbids. Every test here must
/// fail if the corresponding constraint is removed from FamilyMemberConfiguration.
/// </summary>
public sealed class FamilyMemberConstraintTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private async Task<(Guid TenantId, Guid TreeId)> SeedTenantWithTreeAsync(string slug)
    {
        await using var context = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var tree = FamilyTreeAggregate.Create(tenant.Id, $"Tree {slug}", Now);
        context.FamilyTrees.Add(tree);
        await context.SaveChangesAsync();

        return (tenant.Id, tree.Id);
    }

    [Fact]
    public async Task A_first_generation_member_persists_with_a_null_parent()
    {
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("alpha");

        await using var context = ContextFor(tenantId);
        context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, null, "سليمان", Now));
        await context.SaveChangesAsync();

        var stored = await context.FamilyMembers.SingleAsync();
        stored.ParentId.Should().BeNull();
        stored.Name.Should().Be("سليمان");
    }

    [Fact]
    public async Task A_child_persists_with_its_parent_in_the_same_tree()
    {
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("beta");

        await using var context = ContextFor(tenantId);
        var parent = FamilyMember.Create(tenantId, treeId, null, "سليمان", Now);
        context.FamilyMembers.Add(parent);
        await context.SaveChangesAsync();

        context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, parent.Id, "فارس", Now));
        await context.SaveChangesAsync();

        var child = await context.FamilyMembers.SingleAsync(m => m.Name == "فارس");
        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task The_database_rejects_a_parent_that_belongs_to_another_tree()
    {
        // The load-bearing test for design spec §3.3. If the composite FK is reduced to a
        // single-column parent_id -> id reference, this insert succeeds and the test fails.
        var (tenantA, treeA) = await SeedTenantWithTreeAsync("gamma");
        var (tenantB, treeB) = await SeedTenantWithTreeAsync("delta");

        Guid foreignParentId;
        await using (var contextB = ContextFor(tenantB))
        {
            var foreignParent = FamilyMember.Create(tenantB, treeB, null, "غريب", Now);
            contextB.FamilyMembers.Add(foreignParent);
            await contextB.SaveChangesAsync();
            foreignParentId = foreignParent.Id;
        }

        await using var contextA = ContextFor(tenantA);
        contextA.FamilyMembers.Add(FamilyMember.Create(tenantA, treeA, foreignParentId, "فارس", Now));

        var act = async () => await contextA.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task The_database_rejects_a_parent_id_that_does_not_exist()
    {
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("epsilon");

        await using var context = ContextFor(tenantId);
        context.FamilyMembers.Add(
            FamilyMember.Create(tenantId, treeId, Guid.CreateVersion7(), "فارس", Now));

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task The_database_rejects_a_member_whose_tenant_does_not_own_its_tree()
    {
        // Design spec §3.3 applied to the tenant axis (Task 2 review, Important finding 2). The
        // domain factory does not reject this combination — tenantA and treeB are each valid on
        // their own — so only the composite (family_tree_id, tenant_id) foreign key can catch
        // a member whose tenant does not own the tree it claims to belong to.
        var (tenantA, _) = await SeedTenantWithTreeAsync("mu");
        var (_, treeB) = await SeedTenantWithTreeAsync("nu");

        await using var context = ContextFor(tenantA);
        context.FamilyMembers.Add(FamilyMember.Create(tenantA, treeB, null, "مخالف", Now));

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task The_database_refuses_to_delete_a_member_that_still_has_children()
    {
        // OnDelete(Restrict) makes "a parent with children cannot be deleted" a database
        // guarantee, not only a service check.
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("zeta");

        await using var context = ContextFor(tenantId);
        var parent = FamilyMember.Create(tenantId, treeId, null, "سليمان", Now);
        context.FamilyMembers.Add(parent);
        await context.SaveChangesAsync();
        context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, parent.Id, "فارس", Now));
        await context.SaveChangesAsync();

        context.FamilyMembers.Remove(parent);
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Members_of_another_tenant_are_invisible_through_the_query_filter()
    {
        var (tenantA, treeA) = await SeedTenantWithTreeAsync("eta");
        var (tenantB, treeB) = await SeedTenantWithTreeAsync("theta");

        await using (var contextA = ContextFor(tenantA))
        {
            contextA.FamilyMembers.Add(FamilyMember.Create(tenantA, treeA, null, "أ", Now));
            await contextA.SaveChangesAsync();
        }

        await using (var contextB = ContextFor(tenantB))
        {
            contextB.FamilyMembers.Add(FamilyMember.Create(tenantB, treeB, null, "ب", Now));
            await contextB.SaveChangesAsync();
        }

        await using var reader = ContextFor(tenantA);
        var visible = await reader.FamilyMembers.ToListAsync();

        visible.Should().ContainSingle().Which.Name.Should().Be("أ");
    }

    [Fact]
    public async Task An_unauthenticated_context_sees_no_members_at_all()
    {
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("iota");

        await using (var context = ContextFor(tenantId))
        {
            context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, null, "أ", Now));
            await context.SaveChangesAsync();
        }

        await using var anonymous = ContextFor(Guid.Empty);

        // Guid.Empty is the unauthenticated tenant: the filter must fail closed.
        (await anonymous.FamilyMembers.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_id_is_looked_up_only_within_the_caller_tenant()
    {
        // The IDOR case: tenant A holds a REAL id belonging to tenant B and still finds nothing.
        var (tenantA, _) = await SeedTenantWithTreeAsync("kappa");
        var (tenantB, treeB) = await SeedTenantWithTreeAsync("lambda");

        Guid foreignId;
        await using (var contextB = ContextFor(tenantB))
        {
            var member = FamilyMember.Create(tenantB, treeB, null, "غريب", Now);
            contextB.FamilyMembers.Add(member);
            await contextB.SaveChangesAsync();
            foreignId = member.Id;
        }

        await using var contextA = ContextFor(tenantA);

        (await contextA.FamilyMembers.FirstOrDefaultAsync(m => m.Id == foreignId)).Should().BeNull();
    }
}
