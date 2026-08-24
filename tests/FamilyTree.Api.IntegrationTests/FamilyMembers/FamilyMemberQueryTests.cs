using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Domain.Countries;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.FamilyMembers;

/// <summary>
/// Design spec §3. These run against real PostgreSQL because the feature IS a recursive CTE —
/// there is nothing left to test once the database is faked (design spec §8).
///
/// The worked-example table below is the same one MemberDerivationTests asserts against. That is
/// the point: two implementations of one walk (design spec §4.2), pinned to the same answers, so
/// a change to either that is not made to the other fails here.
/// </summary>
public sealed class FamilyMemberQueryTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    /// <summary>
    /// داوود                     branch = (none → "Root")   generation 0
    /// ├── سليمان                branch = سليمان             generation 1
    /// │   ├── فارس              branch = سليمان             generation 2
    /// │   │   └── محمود         branch = سليمان             generation 3
    /// │   └── خالد              branch = سليمان             generation 2
    /// └── عمر                   branch = عمر                generation 1
    ///     └── يوسف              branch = عمر                generation 2
    /// </summary>
    private sealed record Family(Guid TenantId, IReadOnlyDictionary<string, Guid> Ids)
    {
        public Guid this[string name] => Ids[name];
    }

    private async Task<Guid> SeedTenantAsync(string slug)
    {
        await using var context = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        context.FamilyTrees.Add(FamilyTreeAggregate.Create(tenant.Id, $"Tree {slug}", Now));
        await context.SaveChangesAsync();

        return tenant.Id;
    }

    /// <summary>
    /// Saves one member per SaveChanges, parent before child: the composite self-FK is
    /// deliberately invisible to EF (see FamilyMemberConfiguration), so a single batch is not
    /// ordering-safe.
    /// </summary>
    private async Task<Family> SeedWorkedExampleAsync(string slug)
    {
        var tenantId = await SeedTenantAsync(slug);
        await using var context = ContextFor(tenantId);
        var treeId = (await context.FamilyTrees.SingleAsync()).Id;
        var ids = new Dictionary<string, Guid>();

        async Task<Guid> Add(string name, string? parent, bool isDeceased = false, int? countryId = null)
        {
            var member = FamilyMember.Create(
                tenantId, treeId, parent is null ? null : ids[parent], name, Now,
                dateOfBirth: null, dateOfDeath: null, isDeceased: isDeceased,
                contact: new ContactDetails(null, null, null, countryId));
            context.FamilyMembers.Add(member);
            await context.SaveChangesAsync();
            ids[name] = member.Id;
            return member.Id;
        }

        var palestine = await EnsurePalestineAsync(context);

        await Add("داوود", null);
        await Add("سليمان", "داوود");
        await Add("فارس", "سليمان", isDeceased: true, countryId: palestine);
        await Add("محمود", "فارس");
        await Add("خالد", "سليمان");
        await Add("عمر", "داوود");
        await Add("يوسف", "عمر");

        return new Family(tenantId, ids);
    }

    private static Task<IReadOnlyList<Contracts.FamilyMembers.FamilyMemberListItem>> ListAsync(
        ApplicationDbContext context, Guid tenantId, MemberFilter filter) =>
        FamilyMemberQuery.ListAsync(context, tenantId, filter, FamilyMemberQuery.NoLimit, 0, default);

    /// <summary>
    /// DatabaseTestBase migrates but does not seed, and the country catalog is seeded by
    /// DatabaseSeeder rather than by a migration. Inserting the one row these tests need is
    /// cheaper than running the whole seeder, and countries are system-level reference data with
    /// no tenant filter to satisfy (design spec §2.1).
    /// </summary>
    private static async Task<int> EnsurePalestineAsync(ApplicationDbContext context)
    {
        var existing = await context.Countries
            .Where(c => c.Code == "PS").Select(c => c.Id).FirstOrDefaultAsync();
        if (existing != 0) return existing;

        var palestine = Country.Create("PS", "فلسطين", "Palestine", "+970");
        context.Countries.Add(palestine);
        await context.SaveChangesAsync();
        return palestine.Id;
    }

    [Theory]
    [InlineData("داوود", null, 0)]
    [InlineData("سليمان", "سليمان", 1)]
    [InlineData("فارس", "سليمان", 2)]
    [InlineData("محمود", "سليمان", 3)]
    [InlineData("خالد", "سليمان", 2)]
    [InlineData("عمر", "عمر", 1)]
    [InlineData("يوسف", "عمر", 2)]
    public async Task The_worked_example(string name, string? branchName, int generation)
    {
        var family = await SeedWorkedExampleAsync($"cte-{generation}-{name.Length}-{branchName?.Length ?? 0}");
        await using var context = ContextFor(family.TenantId);

        var rows = await ListAsync(context, family.TenantId, MemberFilter.None);

        var row = rows.Should().ContainSingle(r => r.Name == name).Subject;
        row.Generation.Should().Be(generation);
        row.BranchId.Should().Be(branchName is null ? null : family[branchName]);
        row.BranchName.Should().Be(branchName);
    }

    /// <summary>
    /// Puts a row on disk that the two composite foreign keys make physically unrepresentable
    /// through a normal Add + SaveChanges (design spec §3.3) — a member whose parent_id points
    /// across the tenant boundary. The same session_replication_role pattern
    /// CycleCheckQueryTests uses, and the only way to model the row a restored backup or a
    /// future relaxation of those constraints could still produce.
    ///
    /// The setting is connection-scoped, so the connection is opened explicitly and held across
    /// both statements — otherwise EF opens its own inside SaveChanges and it never applies.
    /// </summary>
    private async Task InsertBypassingForeignKeysAsync(params FamilyMember[] members)
    {
        foreach (var member in members)
        {
            await using var seed = ContextFor(Guid.Empty);
            seed.FamilyMembers.Add(member);

            await seed.Database.OpenConnectionAsync();
            try
            {
                await seed.Database.ExecuteSqlRawAsync("SET session_replication_role = replica;");
                await seed.SaveChangesAsync();
            }
            finally
            {
                await seed.Database.ExecuteSqlRawAsync("SET session_replication_role = DEFAULT;");
                await seed.Database.CloseConnectionAsync();
            }
        }
    }

    private async Task<Guid> TreeOfAsync(Guid tenantId)
    {
        await using var context = ContextFor(tenantId);
        return (await context.FamilyTrees.SingleAsync()).Id;
    }

    [Fact]
    public async Task A_walk_started_in_one_tenant_cannot_descend_into_another()
    {
        // The test design spec §3.1 exists for, and the mirror image of CycleCheckQueryTests'
        // "does not climb into another tenant" — that walk goes upward, this one goes downward.
        //
        // The stepping stone is what gives this teeth. A leaked row of the intruder's own would
        // be caught anyway by the tenant predicate on the outer join, so it would prove nothing
        // about the RECURSIVE term. Hanging a HOST member off the intruder makes the difference
        // observable: it passes the outer join, and is reachable only by walking through a row
        // in another tenant. Remove the recursive term's predicate and this test fails; it is
        // the only one in the file that does.
        var host = await SeedWorkedExampleAsync("cte-tenant-host");
        var intruderTenantId = await SeedTenantAsync("cte-tenant-intruder");
        var intruderTreeId = await TreeOfAsync(intruderTenantId);
        var hostTreeId = await TreeOfAsync(host.TenantId);

        var steppingStone = FamilyMember.Create(
            intruderTenantId, intruderTreeId, host["داوود"], "دخيل", Now);
        var reachableOnlyThroughIt = FamilyMember.Create(
            host.TenantId, hostTreeId, steppingStone.Id, "زياد", Now);

        await InsertBypassingForeignKeysAsync(steppingStone, reachableOnlyThroughIt);

        await using var context = ContextFor(host.TenantId);
        var rows = await ListAsync(context, host.TenantId, MemberFilter.None);

        rows.Should().HaveCount(7);
        rows.Should().NotContain(r => r.Name == "زياد");
        rows.Should().NotContain(r => r.Name == "دخيل");
    }

    [Fact]
    public async Task The_reference_lists_do_not_cross_the_tenant_boundary()
    {
        // The generations query has no outer join to fall back on — it reads the walk directly —
        // so a leaked descendant shows up as a generation the host tree does not have. Grafted
        // two levels below محمود, the deepest host member, so generations 4 and 5 would appear.
        var host = await SeedWorkedExampleAsync("cte-ref-tenant-host");
        var intruderTenantId = await SeedTenantAsync("cte-ref-tenant-intruder");
        var intruderTreeId = await TreeOfAsync(intruderTenantId);

        var first = FamilyMember.Create(intruderTenantId, intruderTreeId, host["محمود"], "دخيل", Now);
        var second = FamilyMember.Create(intruderTenantId, intruderTreeId, first.Id, "دخيلة", Now);

        await InsertBypassingForeignKeysAsync(first, second);

        await using var context = ContextFor(host.TenantId);

        var generations = await FamilyMemberQuery.ListGenerationsAsync(context, host.TenantId, null, default);
        generations.Should().Equal(0, 1, 2, 3);

        var branches = await FamilyMemberQuery.ListBranchesAsync(context, host.TenantId, null, default);
        branches.Select(b => b.Name).Should().Equal("سليمان", "عمر");
    }

    [Fact]
    public async Task An_unauthenticated_caller_gets_nothing()
    {
        var family = await SeedWorkedExampleAsync("cte-anon");
        await using var context = ContextFor(family.TenantId);

        // Guid.Empty is an unauthenticated caller. Fail closed, before any SQL runs.
        (await ListAsync(context, Guid.Empty, MemberFilter.None)).Should().BeEmpty();
        (await FamilyMemberQuery.ListBranchesAsync(context, Guid.Empty, null, default)).Should().BeEmpty();
        (await FamilyMemberQuery.ListGenerationsAsync(context, Guid.Empty, null, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_root_re_measures_everything_below_it()
    {
        var family = await SeedWorkedExampleAsync("cte-subtree");
        await using var context = ContextFor(family.TenantId);

        var rows = await ListAsync(
            context, family.TenantId, MemberFilter.None with { RootId = family["سليمان"] });

        rows.Select(r => r.Name).Should().BeEquivalentTo(["سليمان", "فارس", "خالد", "محمود"]);
        rows.Single(r => r.Name == "سليمان").Should().Match<Contracts.FamilyMembers.FamilyMemberListItem>(
            r => r.BranchId == null && r.Generation == 0);
        rows.Single(r => r.Name == "محمود").Should().Match<Contracts.FamilyMembers.FamilyMemberListItem>(
            r => r.BranchId == family["فارس"] && r.Generation == 2);
    }

    [Fact]
    public async Task An_unknown_root_returns_nobody()
    {
        var family = await SeedWorkedExampleAsync("cte-unknown-root");
        await using var context = ContextFor(family.TenantId);

        var rows = await ListAsync(
            context, family.TenantId, MemberFilter.None with { RootId = Guid.CreateVersion7() });

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_filter_narrows_the_result()
    {
        var family = await SeedWorkedExampleAsync("cte-filters");
        await using var context = ContextFor(family.TenantId);
        var palestine = await EnsurePalestineAsync(context);

        var bySearch = await ListAsync(context, family.TenantId, MemberFilter.None with { Search = "فارس" });
        bySearch.Select(r => r.Name).Should().Equal("فارس");

        var byStatus = await ListAsync(
            context, family.TenantId, MemberFilter.None with { Status = MemberStatusFilter.Deceased });
        byStatus.Select(r => r.Name).Should().Equal("فارس");

        var byBranch = await ListAsync(
            context, family.TenantId, MemberFilter.None with { BranchId = family["عمر"] });
        byBranch.Select(r => r.Name).Should().BeEquivalentTo(["عمر", "يوسف"]);

        var byGeneration = await ListAsync(
            context, family.TenantId, MemberFilter.None with { Generation = 1 });
        byGeneration.Select(r => r.Name).Should().BeEquivalentTo(["سليمان", "عمر"]);

        var byCountry = await ListAsync(
            context, family.TenantId, MemberFilter.None with { CountryId = palestine });
        byCountry.Select(r => r.Name).Should().Equal("فارس");
    }

    [Fact]
    public async Task The_filters_combine_with_and()
    {
        var family = await SeedWorkedExampleAsync("cte-combined");
        await using var context = ContextFor(family.TenantId);
        var palestine = await EnsurePalestineAsync(context);

        var all = new MemberFilter(
            "فارس", MemberStatusFilter.Deceased, family["سليمان"], 2, palestine, null);
        (await ListAsync(context, family.TenantId, all)).Select(r => r.Name).Should().Equal("فارس");

        // Specification §15 is an AND, not an OR: changing one axis empties the result.
        (await ListAsync(context, family.TenantId, all with { Generation = 3 })).Should().BeEmpty();
        (await ListAsync(context, family.TenantId, all with { BranchId = family["عمر"] })).Should().BeEmpty();
    }

    [Fact]
    public async Task An_unmatched_filter_is_an_empty_list_not_an_error()
    {
        var family = await SeedWorkedExampleAsync("cte-unmatched");
        await using var context = ContextFor(family.TenantId);

        (await ListAsync(context, family.TenantId, MemberFilter.None with { BranchId = Guid.CreateVersion7() }))
            .Should().BeEmpty();
        (await ListAsync(context, family.TenantId, MemberFilter.None with { CountryId = -1 }))
            .Should().BeEmpty();
        (await ListAsync(context, family.TenantId, MemberFilter.None with { Generation = 99 }))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task A_search_metacharacter_matches_literally()
    {
        // A parameter binds the pattern, not its meaning, so "%" would otherwise match everyone.
        var family = await SeedWorkedExampleAsync("cte-wildcard");
        await using var context = ContextFor(family.TenantId);

        (await ListAsync(context, family.TenantId, MemberFilter.None with { Search = "%" }))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task The_root_belongs_to_no_branch()
    {
        // "Root" is the absence of a branch, so filtering by the root's own id matches nobody.
        var family = await SeedWorkedExampleAsync("cte-root-branch");
        await using var context = ContextFor(family.TenantId);

        (await ListAsync(context, family.TenantId, MemberFilter.None with { BranchId = family["داوود"] }))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task The_list_is_ordered_by_name()
    {
        var family = await SeedWorkedExampleAsync("cte-order");
        await using var context = ContextFor(family.TenantId);

        var names = (await ListAsync(context, family.TenantId, MemberFilter.None))
            .Select(r => r.Name).ToList();

        names.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_country_code_rides_along_with_the_row()
    {
        var family = await SeedWorkedExampleAsync("cte-country-code");
        await using var context = ContextFor(family.TenantId);

        var rows = await ListAsync(context, family.TenantId, MemberFilter.None);

        rows.Single(r => r.Name == "فارس").CountryCode.Should().Be("PS");
        rows.Single(r => r.Name == "محمود").CountryCode.Should().BeNull();
    }

    [Fact]
    public async Task Branches_are_the_roots_direct_children_ordered_by_name()
    {
        var family = await SeedWorkedExampleAsync("cte-branches");
        await using var context = ContextFor(family.TenantId);

        var branches = await FamilyMemberQuery.ListBranchesAsync(context, family.TenantId, null, default);

        branches.Select(b => b.Name).Should().Equal("سليمان", "عمر");
        branches.Should().NotContain(b => b.Name == "داوود");
    }

    [Fact]
    public async Task Branches_follow_the_selected_root()
    {
        var family = await SeedWorkedExampleAsync("cte-branches-root");
        await using var context = ContextFor(family.TenantId);

        var branches = await FamilyMemberQuery
            .ListBranchesAsync(context, family.TenantId, family["سليمان"], default);

        branches.Select(b => b.Name).Should().Equal("خالد", "فارس");
    }

    [Fact]
    public async Task Generations_are_distinct_and_ascending_from_zero()
    {
        var family = await SeedWorkedExampleAsync("cte-generations");
        await using var context = ContextFor(family.TenantId);

        var generations = await FamilyMemberQuery
            .ListGenerationsAsync(context, family.TenantId, null, default);

        generations.Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public async Task A_root_naming_nothing_has_no_branches_and_no_generations()
    {
        // "This subtree has no branches" and "no such subtree" are the same answer to a
        // dropdown, so neither is an error.
        var family = await SeedWorkedExampleAsync("cte-empty-reference");
        await using var context = ContextFor(family.TenantId);
        var unknown = Guid.CreateVersion7();

        (await FamilyMemberQuery.ListBranchesAsync(context, family.TenantId, unknown, default))
            .Should().BeEmpty();
        (await FamilyMemberQuery.ListGenerationsAsync(context, family.TenantId, unknown, default))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Branches_and_generations_ignore_the_other_tenants_members()
    {
        var host = await SeedWorkedExampleAsync("cte-ref-host");
        var other = await SeedWorkedExampleAsync("cte-ref-other");
        await using var context = ContextFor(host.TenantId);

        var branches = await FamilyMemberQuery.ListBranchesAsync(context, host.TenantId, null, default);

        branches.Should().HaveCount(2);
        branches.Select(b => b.Id).Should().BeEquivalentTo([host["سليمان"], host["عمر"]]);
        branches.Select(b => b.Id).Should().NotContain(other["سليمان"]);
    }

    [Fact]
    public async Task The_in_memory_derivation_agrees_with_the_walk()
    {
        // The duplication design spec §4.2 asks for, watched. MemberDerivation feeds the tree
        // page; this CTE feeds the list and the export. If one is changed without the other,
        // this fails rather than shipping two answers to the same question.
        var family = await SeedWorkedExampleAsync("cte-agrees");
        await using var context = ContextFor(family.TenantId);

        var rows = await ListAsync(context, family.TenantId, MemberFilter.None);
        var members = await context.FamilyMembers.AsNoTracking().ToListAsync();
        var derived = MemberDerivation.Derive(members, rootId: null);

        derived.Should().HaveCount(rows.Count);
        foreach (var row in rows)
        {
            derived[row.Id].Should().Be(new MemberPlacement(row.BranchId, row.Generation));
        }
    }
}
