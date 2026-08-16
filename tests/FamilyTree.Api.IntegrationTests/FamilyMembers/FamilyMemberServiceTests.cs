using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.FamilyMembers;

public sealed class FamilyMemberServiceTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

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

    private static IFamilyMemberService ServiceFor(ApplicationDbContext context, Guid tenantId) =>
        new FamilyMemberService(context, new StubTenantContext(tenantId, Guid.CreateVersion7()), Clock);

    [Fact]
    public async Task CreateAsync_adds_a_first_generation_member_when_parent_is_null()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("svc-alpha");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var created = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);

        created.Id.Should().NotBeEmpty();
        created.Name.Should().Be("سليمان");
        created.ParentId.Should().BeNull();
        created.Version.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_attaches_a_child_to_an_existing_parent()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("svc-beta");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var parent = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        var child = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", parent.Id), default);

        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_parent_id_that_does_not_exist()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("svc-gamma");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var act = async () => await service.CreateAsync(
            new CreateFamilyMemberRequest("فارس", Guid.CreateVersion7()), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MEMBER_PARENT_NOT_FOUND");
    }

    [Fact]
    public async Task CreateAsync_rejects_a_parent_belonging_to_another_tenant()
    {
        // The service must never see the foreign row, so the failure is indistinguishable
        // from "no such parent" — which is the point (design spec §4.4).
        var (tenantA, _) = await SeedTenantWithTreeAsync("svc-delta");
        var (tenantB, _) = await SeedTenantWithTreeAsync("svc-epsilon");

        Guid foreignParentId;
        await using (var contextB = ContextFor(tenantB))
        {
            var created = await ServiceFor(contextB, tenantB)
                .CreateAsync(new CreateFamilyMemberRequest("غريب", null), default);
            foreignParentId = created.Id;
        }

        await using var contextA = ContextFor(tenantA);
        var act = async () => await ServiceFor(contextA, tenantA)
            .CreateAsync(new CreateFamilyMemberRequest("فارس", foreignParentId), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MEMBER_PARENT_NOT_FOUND");
    }

    [Fact]
    public async Task CreateAsync_fails_when_the_tenant_has_no_family_tree()
    {
        Guid tenantId;
        await using (var context = ContextFor(Guid.Empty))
        {
            var tenant = Tenant.Create("Treeless", "treeless", Now);
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        await using var scoped = ContextFor(tenantId);
        var act = async () => await ServiceFor(scoped, tenantId)
            .CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("FAMILY_TREE_NOT_FOUND");
    }

    [Fact]
    public async Task GetAsync_returns_a_member_of_the_caller_tenant()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("svc-zeta");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var created = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);

        var found = await service.GetAsync(created.Id, default);

        found.Should().NotBeNull();
        found!.Name.Should().Be("سليمان");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_a_member_of_another_tenant()
    {
        var (tenantA, _) = await SeedTenantWithTreeAsync("svc-eta");
        var (tenantB, _) = await SeedTenantWithTreeAsync("svc-theta");

        Guid foreignId;
        await using (var contextB = ContextFor(tenantB))
        {
            foreignId = (await ServiceFor(contextB, tenantB)
                .CreateAsync(new CreateFamilyMemberRequest("غريب", null), default)).Id;
        }

        await using var contextA = ContextFor(tenantA);

        (await ServiceFor(contextA, tenantA).GetAsync(foreignId, default)).Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_returns_only_the_caller_tenant_members()
    {
        var (tenantA, _) = await SeedTenantWithTreeAsync("svc-iota");
        var (tenantB, _) = await SeedTenantWithTreeAsync("svc-kappa");

        await using (var contextB = ContextFor(tenantB))
        {
            await ServiceFor(contextB, tenantB)
                .CreateAsync(new CreateFamilyMemberRequest("غريب", null), default);
        }

        await using var contextA = ContextFor(tenantA);
        var service = ServiceFor(contextA, tenantA);
        await service.CreateAsync(new CreateFamilyMemberRequest("عمر", null), default);
        await service.CreateAsync(new CreateFamilyMemberRequest("أحمد", null), default);

        var all = await service.ListAsync(default);

        all.Should().HaveCount(2);
        all.Select(m => m.Name).Should().NotContain("غريب");
    }
}
