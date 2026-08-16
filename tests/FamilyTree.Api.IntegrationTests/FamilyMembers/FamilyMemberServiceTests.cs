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

    [Fact]
    public async Task UpdateAsync_renames_a_member_and_advances_its_version()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("upd-alpha");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var created = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", null), default);

        var updated = await service.UpdateAsync(
            created.Id, new UpdateFamilyMemberRequest("فارس أحمد", created.Version), default);

        updated.Name.Should().Be("فارس أحمد");
        updated.Version.Should().Be(created.Version + 1);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_stale_update_with_a_concurrency_conflict()
    {
        // Two administrators read the same member, then both save (technical specification §43).
        // If OriginalValue is not set from the request, the second save silently overwrites
        // the first and this test fails.
        var (tenantId, _) = await SeedTenantWithTreeAsync("upd-beta");

        Guid memberId;
        int versionBothAdminsRead;
        await using (var setup = ContextFor(tenantId))
        {
            var created = await ServiceFor(setup, tenantId)
                .CreateAsync(new CreateFamilyMemberRequest("أحمد", null), default);
            memberId = created.Id;
            versionBothAdminsRead = created.Version;
        }

        await using (var contextA = ContextFor(tenantId))
        {
            await ServiceFor(contextA, tenantId).UpdateAsync(
                memberId, new UpdateFamilyMemberRequest("أحمد علي", versionBothAdminsRead), default);
        }

        await using (var contextB = ContextFor(tenantId))
        {
            var act = async () => await ServiceFor(contextB, tenantId).UpdateAsync(
                memberId, new UpdateFamilyMemberRequest("أحمد محمد", versionBothAdminsRead), default);

            (await act.Should().ThrowAsync<ConflictException>())
                .Which.Code.Should().Be("CONCURRENCY_CONFLICT");
        }

        // And the first write survived — the loser did not overwrite it.
        await using var reader = ContextFor(tenantId);
        (await ServiceFor(reader, tenantId).GetAsync(memberId, default))!.Name.Should().Be("أحمد علي");
    }

    [Fact]
    public async Task UpdateAsync_reports_a_missing_member_as_not_found()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("upd-gamma");
        await using var context = ContextFor(tenantId);

        var act = async () => await ServiceFor(context, tenantId).UpdateAsync(
            Guid.CreateVersion7(), new UpdateFamilyMemberRequest("فارس", 1), default);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("MEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateAsync_reports_another_tenants_member_as_not_found()
    {
        var (tenantA, _) = await SeedTenantWithTreeAsync("upd-delta");
        var (tenantB, _) = await SeedTenantWithTreeAsync("upd-epsilon");

        Guid foreignId;
        await using (var contextB = ContextFor(tenantB))
        {
            foreignId = (await ServiceFor(contextB, tenantB)
                .CreateAsync(new CreateFamilyMemberRequest("غريب", null), default)).Id;
        }

        await using var contextA = ContextFor(tenantA);
        var act = async () => await ServiceFor(contextA, tenantA).UpdateAsync(
            foreignId, new UpdateFamilyMemberRequest("مخترق", 1), default);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("MEMBER_NOT_FOUND");
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("tenant")]
    [InlineData("tree")]
    public async Task UpdateAsync_rejects_rather_than_ignores_an_immutable_field(string field)
    {
        // Design spec §4.6: PUT must REJECT parentId / tenantId / familyTreeId, not silently
        // drop them, so a client cannot believe a re-parent succeeded. Moving is Phase 5.
        var (tenantId, _) = await SeedTenantWithTreeAsync($"upd-immutable-{field}");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var created = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", null), default);
        var other = Guid.CreateVersion7();

        var request = field switch
        {
            "parent" => new UpdateFamilyMemberRequest("فارس", created.Version, ParentId: other),
            "tenant" => new UpdateFamilyMemberRequest("فارس", created.Version, TenantId: other),
            _ => new UpdateFamilyMemberRequest("فارس", created.Version, FamilyTreeId: other)
        };

        var act = async () => await service.UpdateAsync(created.Id, request, default);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("MEMBER_FIELD_NOT_UPDATABLE");
    }

    [Fact]
    public async Task UpdateAsync_applies_the_domain_name_rules()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("upd-zeta");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var created = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", null), default);

        var act = async () => await service.UpdateAsync(
            created.Id, new UpdateFamilyMemberRequest("   ", created.Version), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MEMBER_NAME_REQUIRED");
    }
}
