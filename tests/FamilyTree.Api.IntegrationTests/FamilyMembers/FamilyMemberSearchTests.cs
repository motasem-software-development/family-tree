using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.FamilyMembers;

/// <summary>
/// Design spec §5.4. These run against real PostgreSQL because the whole feature IS a
/// recursive CTE — there is nothing left to test once you fake the database (spec §6).
/// </summary>
public sealed class FamilyMemberSearchTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private async Task<Guid> SeedTenantWithTreeAsync(string slug)
    {
        await using var context = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        context.FamilyTrees.Add(FamilyTreeAggregate.Create(tenant.Id, $"Tree {slug}", Now));
        await context.SaveChangesAsync();

        return tenant.Id;
    }

    private static IFamilyMemberService ServiceFor(ApplicationDbContext context, Guid tenantId) =>
        new FamilyMemberService(context, new StubTenantContext(tenantId, Guid.CreateVersion7()), Clock);

    /// <summary>Creates a root-to-leaf chain, returning every created member in order.</summary>
    private static async Task<IReadOnlyList<FamilyMemberResponse>> SeedChainAsync(
        IFamilyMemberService service, params string[] names)
    {
        var created = new List<FamilyMemberResponse>();
        Guid? parentId = null;
        foreach (var name in names)
        {
            var member = await service.CreateAsync(new CreateFamilyMemberRequest(name, parentId), default);
            created.Add(member);
            parentId = member.Id;
        }
        return created;
    }

    [Fact]
    public async Task Matching_names_are_returned_with_a_root_first_ancestor_path()
    {
        var tenantId = await SeedTenantWithTreeAsync("search-path");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var chain = await SeedChainAsync(service, "داوود", "سلمان", "علي", "خالد");

        var page = await service.SearchAsync("خالد", 20, 0, default);

        page.Total.Should().Be(1);
        var hit = page.Items.Should().ContainSingle().Subject;
        hit.Id.Should().Be(chain[3].Id);
        hit.Ancestors.Select(a => a.Name).Should().Equal("داوود", "سلمان", "علي");
        hit.Generation.Should().Be(4);
    }

    [Fact]
    public async Task A_root_member_has_an_empty_ancestor_path_and_generation_one()
    {
        var tenantId = await SeedTenantWithTreeAsync("search-root");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        await SeedChainAsync(service, "داوود", "سلمان");

        var page = await service.SearchAsync("داوود", 20, 0, default);

        var hit = page.Items.Should().ContainSingle().Subject;
        hit.Ancestors.Should().BeEmpty();
        hit.Generation.Should().Be(1);
    }

    [Fact]
    public async Task Total_counts_every_match_even_when_the_page_is_smaller()
    {
        // The Phase 2.5 finding in the flesh: the label must be able to say "3 of 5", so the
        // total has to be independent of the page size.
        var tenantId = await SeedTenantWithTreeAsync("search-total");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var root = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        for (var i = 0; i < 5; i++)
            await service.CreateAsync(new CreateFamilyMemberRequest("محمد", root.Id), default);

        var page = await service.SearchAsync("محمد", 3, 0, default);

        page.Total.Should().Be(5);
        page.Items.Should().HaveCount(3);

        // Each محمد is a direct child of داوود. Without the per-hit ancestor reset in
        // ReadPageAsync, the second and third hits would accumulate the earlier hits'
        // chains — which every count-only assertion in this file would happily allow.
        page.Items.Should().OnlyContain(i => i.Generation == 2);
        page.Items.Should().OnlyContain(i => i.Ancestors.Count == 1 && i.Ancestors[0].Name == "داوود");
    }

    [Fact]
    public async Task Offset_walks_through_the_matches_without_repeating_one()
    {
        var tenantId = await SeedTenantWithTreeAsync("search-offset");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var root = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        for (var i = 0; i < 5; i++)
            await service.CreateAsync(new CreateFamilyMemberRequest("محمد", root.Id), default);

        var first = await service.SearchAsync("محمد", 3, 0, default);
        var second = await service.SearchAsync("محمد", 3, 3, default);

        second.Items.Should().HaveCount(2);
        first.Items.Select(i => i.Id).Should().NotIntersectWith(second.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Matching_is_case_insensitive_and_unanchored()
    {
        var tenantId = await SeedTenantWithTreeAsync("search-ilike");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        await SeedChainAsync(service, "Abdullah Al-Saqqa");

        var page = await service.SearchAsync("al-saqqa", 20, 0, default);

        page.Items.Should().ContainSingle().Which.Name.Should().Be("Abdullah Al-Saqqa");
    }

    [Fact]
    public async Task Wildcard_characters_in_the_query_are_matched_literally()
    {
        // A bare % must not become "match everything" — the query is user input, and LIKE
        // metacharacters are the injection surface that survives parameterisation.
        var tenantId = await SeedTenantWithTreeAsync("search-wildcard");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        await SeedChainAsync(service, "داوود", "سلمان");

        var page = await service.SearchAsync("%", 20, 0, default);

        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task A_literal_percent_or_underscore_in_a_name_is_findable()
    {
        // The other half of escaping: over-escaping would make every query containing a
        // metacharacter silently return nothing, and the fail-closed test above cannot see it.
        var tenantId = await SeedTenantWithTreeAsync("search-literal");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        await SeedChainAsync(service, "50% محمد");

        var page = await service.SearchAsync("50%", 20, 0, default);

        page.Items.Should().ContainSingle().Which.Name.Should().Be("50% محمد");
    }

    [Fact]
    public async Task A_blank_query_matches_nothing_rather_than_everything()
    {
        var tenantId = await SeedTenantWithTreeAsync("search-blank");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        await SeedChainAsync(service, "داوود", "سلمان");

        var page = await service.SearchAsync("   ", 20, 0, default);

        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_tenants_members_are_never_matched()
    {
        // The standing requirement of spec §6, and load-bearing here specifically because raw
        // SQL bypasses the EF global query filter that protects every other read.
        var tenantA = await SeedTenantWithTreeAsync("search-iso-a");
        var tenantB = await SeedTenantWithTreeAsync("search-iso-b");

        await using (var contextB = ContextFor(tenantB))
            await SeedChainAsync(ServiceFor(contextB, tenantB), "غريب");

        await using var contextA = ContextFor(tenantA);
        var page = await ServiceFor(contextA, tenantA).SearchAsync("غريب", 20, 0, default);

        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unauthenticated_tenant_context_matches_nothing()
    {
        var tenantId = await SeedTenantWithTreeAsync("search-anon");
        await using (var seeded = ContextFor(tenantId))
            await SeedChainAsync(ServiceFor(seeded, tenantId), "داوود");

        await using var context = ContextFor(Guid.Empty);
        var page = await ServiceFor(context, Guid.Empty).SearchAsync("داوود", 20, 0, default);

        // Fails closed, matching QueryFilterInvariantTests: an empty tenant id is not a
        // wildcard.
        page.Total.Should().Be(0);
    }

    [Fact]
    public async Task An_oversized_limit_is_clamped_rather_than_rejected()
    {
        var tenantId = await SeedTenantWithTreeAsync("search-clamp");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var root = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        for (var i = 0; i < 60; i++)
            await service.CreateAsync(new CreateFamilyMemberRequest("محمد", root.Id), default);

        var page = await service.SearchAsync("محمد", 5000, 0, default);

        page.Items.Should().HaveCount(FamilyMemberSearchQuery.MaxLimit);
        page.Total.Should().Be(60);
    }
}
