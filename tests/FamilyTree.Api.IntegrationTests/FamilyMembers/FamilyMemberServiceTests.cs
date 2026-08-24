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

        var all = await service.ListAsync(MemberFilter.None, default);

        all.Should().HaveCount(2);
        all.Select(m => m.Name).Should().NotContain("غريب");
    }

    [Fact]
    public async Task ListAsync_carries_the_branch_and_the_generation()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("svc-lambda");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var root = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        var branch = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", root.Id), default);
        var leaf = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", branch.Id), default);

        var all = await service.ListAsync(MemberFilter.None, default);

        // The root reads generation 0 with no branch — "Root" per specification §21 — while
        // everything below it inherits the branch of the direct child it descends from.
        all.Single(m => m.Id == root.Id).Should().Match<FamilyMemberListItem>(
            m => m.BranchId == null && m.BranchName == null && m.Generation == 0);
        all.Single(m => m.Id == branch.Id).Should().Match<FamilyMemberListItem>(
            m => m.BranchId == branch.Id && m.BranchName == "سليمان" && m.Generation == 1);
        all.Single(m => m.Id == leaf.Id).Should().Match<FamilyMemberListItem>(
            m => m.BranchId == branch.Id && m.BranchName == "سليمان" && m.Generation == 2);
    }

    [Fact]
    public async Task ListAsync_narrows_to_the_filter()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("svc-mu");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var root = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", root.Id) { IsDeceased = true }, default);
        await service.CreateAsync(new CreateFamilyMemberRequest("عمر", root.Id), default);

        var deceased = await service.ListAsync(
            MemberFilter.None with { Status = MemberStatusFilter.Deceased }, default);

        deceased.Select(m => m.Name).Should().Equal("سليمان");
    }

    [Fact]
    public async Task ListAsync_is_ordered_by_name()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("svc-nu");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var root = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        await service.CreateAsync(new CreateFamilyMemberRequest("عمر", root.Id), default);
        await service.CreateAsync(new CreateFamilyMemberRequest("أحمد", root.Id), default);

        var all = await service.ListAsync(MemberFilter.None, default);

        all.Select(m => m.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);
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
    public async Task UpdateAsync_rejects_a_cross_tenant_write_even_when_the_query_filter_lets_the_row_through()
    {
        // Design spec §3.2, layer 2. The EF global query filter (layer 1) is bound to the
        // context at construction and normally hides another tenant's row before the service
        // ever sees it — which is exactly why "UpdateAsync_reports_another_tenants_member_as_
        // not_found" above cannot prove the new assertion is load-bearing on its own: with the
        // assertion deleted, that test would still pass on layer 1 alone.
        //
        // To exercise layer 2 in isolation we decouple the two: the DbContext here is scoped
        // to tenant B (the row's real owner), so layer 1 lets the row through untouched — the
        // same effect a future `.IgnoreQueryFilters()` lookup would have. The service's own
        // `ITenantContext`, however, is wired to tenant A, modelling the caller layer 1 failed
        // to stop. Only the ownership assertion added to UpdateAsync can catch this.
        var (tenantA, _) = await SeedTenantWithTreeAsync("layer2-a");
        var (tenantB, _) = await SeedTenantWithTreeAsync("layer2-b");

        Guid foreignId;
        await using (var contextB = ContextFor(tenantB))
        {
            foreignId = (await ServiceFor(contextB, tenantB)
                .CreateAsync(new CreateFamilyMemberRequest("غريب", null), default)).Id;
        }

        await using var rowVisibleContext = ContextFor(tenantB);
        var service = new FamilyMemberService(
            rowVisibleContext, new StubTenantContext(tenantA, Guid.CreateVersion7()), Clock);

        var act = async () => await service.UpdateAsync(
            foreignId, new UpdateFamilyMemberRequest("مخترق", 1), default);

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

    [Fact]
    public async Task DeleteAsync_removes_a_leaf_member()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("del-alpha");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var created = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", null), default);

        await service.DeleteAsync(created.Id, default);

        (await service.GetAsync(created.Id, default)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_refuses_a_member_that_has_children()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("del-beta");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var parent = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        await service.CreateAsync(new CreateFamilyMemberRequest("فارس", parent.Id), default);

        var act = async () => await service.DeleteAsync(parent.Id, default);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("MEMBER_HAS_CHILDREN");

        // And nothing was removed.
        (await service.GetAsync(parent.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_reports_a_missing_member_as_not_found()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("del-gamma");
        await using var context = ContextFor(tenantId);

        var act = async () => await ServiceFor(context, tenantId).DeleteAsync(Guid.CreateVersion7(), default);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("MEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task DeleteAsync_reports_another_tenants_member_as_not_found_and_leaves_it_intact()
    {
        var (tenantA, _) = await SeedTenantWithTreeAsync("del-delta");
        var (tenantB, _) = await SeedTenantWithTreeAsync("del-epsilon");

        Guid foreignId;
        await using (var contextB = ContextFor(tenantB))
        {
            foreignId = (await ServiceFor(contextB, tenantB)
                .CreateAsync(new CreateFamilyMemberRequest("غريب", null), default)).Id;
        }

        await using (var contextA = ContextFor(tenantA))
        {
            var act = async () => await ServiceFor(contextA, tenantA).DeleteAsync(foreignId, default);
            (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("MEMBER_NOT_FOUND");
        }

        await using var contextBAgain = ContextFor(tenantB);
        (await ServiceFor(contextBAgain, tenantB).GetAsync(foreignId, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_allows_a_parent_once_its_children_are_gone()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("del-zeta");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var parent = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        var child = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", parent.Id), default);

        await service.DeleteAsync(child.Id, default);
        await service.DeleteAsync(parent.Id, default);

        (await service.ListAsync(MemberFilter.None, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task MoveAsync_reattaches_a_member_to_a_new_parent()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("mv-alpha");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var first = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        var second = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        var child = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", first.Id), default);

        var moved = await service.MoveAsync(
            child.Id, new MoveFamilyMemberRequest(second.Id, child.Version), default);

        moved.ParentId.Should().Be(second.Id);
        moved.Version.Should().Be(child.Version + 1);
    }

    [Fact]
    public async Task MoveAsync_promotes_a_member_to_first_generation()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("mv-beta");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var parent = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        var child = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", parent.Id), default);

        var moved = await service.MoveAsync(
            child.Id, new MoveFamilyMemberRequest(null, child.Version), default);

        moved.ParentId.Should().BeNull();
    }

    [Fact]
    public async Task MoveAsync_refuses_a_move_under_the_members_own_descendant()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("mv-gamma");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var grandparent = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        var parent = await service.CreateAsync(
            new CreateFamilyMemberRequest("فارس", grandparent.Id), default);
        var child = await service.CreateAsync(new CreateFamilyMemberRequest("عمر", parent.Id), default);

        var act = async () => await service.MoveAsync(
            grandparent.Id, new MoveFamilyMemberRequest(child.Id, grandparent.Version), default);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("MOVE_CREATES_CYCLE");
    }

    [Fact]
    public async Task MoveAsync_refuses_a_move_under_the_member_themselves()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("mv-delta");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var member = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);

        var act = async () => await service.MoveAsync(
            member.Id, new MoveFamilyMemberRequest(member.Id, member.Version), default);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("MOVE_CREATES_CYCLE");
    }

    [Fact]
    public async Task MoveAsync_reports_a_missing_member_as_not_found()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("mv-epsilon");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var act = async () => await service.MoveAsync(
            Guid.CreateVersion7(), new MoveFamilyMemberRequest(null, 1), default);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("MEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task MoveAsync_reports_a_target_in_another_tenant_as_not_found()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("mv-zeta");
        var (otherTenantId, _) = await SeedTenantWithTreeAsync("mv-eta");

        Guid strangerId;
        await using (var otherContext = ContextFor(otherTenantId))
        {
            var otherService = ServiceFor(otherContext, otherTenantId);
            strangerId = (await otherService.CreateAsync(
                new CreateFamilyMemberRequest("داوود", null), default)).Id;
        }

        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var member = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);

        var act = async () => await service.MoveAsync(
            member.Id, new MoveFamilyMemberRequest(strangerId, member.Version), default);

        // Not 403, and not a distinct PARENT_NOT_FOUND: another tenant's id must be
        // indistinguishable from an id that never existed (design spec §4.4).
        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("MEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task MoveAsync_rejects_a_stale_version()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("mv-theta");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var parent = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        var member = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", null), default);
        await service.UpdateAsync(
            member.Id, new UpdateFamilyMemberRequest("فارس أحمد", member.Version), default);

        var act = async () => await service.MoveAsync(
            member.Id, new MoveFamilyMemberRequest(parent.Id, member.Version), default);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task MoveAsync_accepts_a_move_to_the_parent_the_member_already_has()
    {
        var (tenantId, _) = await SeedTenantWithTreeAsync("mv-iota");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var parent = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        var child = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", parent.Id), default);

        // Design §6 rule 4: a no-op move is not an error. No user could tell the refusal apart
        // from success, and a third error code would have to be translated for it.
        var moved = await service.MoveAsync(
            child.Id, new MoveFamilyMemberRequest(parent.Id, child.Version), default);

        moved.ParentId.Should().Be(parent.Id);
    }
}
