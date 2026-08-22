using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Application.Reports;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using FamilyTree.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyTree.Api.IntegrationTests.Reports;

/// <summary>
/// Runs against real PostgreSQL because what is under test is the tenant query filter, not the
/// arithmetic — the calculators own that, and have their own fast unit suites.
/// </summary>
public sealed class ReportServiceTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
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

    private static IFamilyMemberService MembersFor(ApplicationDbContext context, Guid tenantId) =>
        new FamilyMemberService(context, new StubTenantContext(tenantId, Guid.CreateVersion7()), Clock);

    private static IReportService ReportsFor(ApplicationDbContext context, TimeProvider clock) =>
        new ReportService(context, clock);

    [Fact]
    public async Task A_tenant_with_no_tree_is_told_so()
    {
        await using var context = ContextFor(Guid.CreateVersion7());

        var act = () => ReportsFor(context, Clock).GetAsync();

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Code.Should().Be("FAMILY_TREE_NOT_FOUND");
    }

    [Fact]
    public async Task An_empty_tree_reports_zeros_rather_than_failing()
    {
        var tenantId = await SeedTenantWithTreeAsync("reports-empty");
        await using var context = ContextFor(tenantId);

        var report = await ReportsFor(context, Clock).GetAsync();

        report.Structure.TotalMembers.Should().Be(0);
        report.LifeStatus.Longevity.Should().BeNull();
        report.Completeness.Issues.Should().OnlyContain(i => i.Count == 0);
    }

    [Fact]
    public async Task Members_are_counted_and_the_generation_walk_reaches_the_leaves()
    {
        var tenantId = await SeedTenantWithTreeAsync("reports-counts");
        await using var context = ContextFor(tenantId);
        var members = MembersFor(context, tenantId);

        var suleiman = await members.CreateAsync(new CreateFamilyMemberRequest("سليمان", null));
        var faris = await members.CreateAsync(new CreateFamilyMemberRequest("فارس", suleiman.Id));
        await members.CreateAsync(new CreateFamilyMemberRequest("محمود", faris.Id));

        var report = await ReportsFor(context, Clock).GetAsync();

        report.Structure.TotalMembers.Should().Be(3);
        report.Structure.Depth.Should().Be(3);
        report.Structure.Branches.Should().ContainSingle().Which.DescendantCount.Should().Be(2);
    }

    /// <summary>Design §10: another tenant's members must not reach any count or list.</summary>
    [Fact]
    public async Task Another_tenants_members_are_invisible()
    {
        var mine = await SeedTenantWithTreeAsync("reports-mine");
        var theirs = await SeedTenantWithTreeAsync("reports-theirs");

        await using (var theirContext = ContextFor(theirs))
        {
            var theirMembers = MembersFor(theirContext, theirs);
            await theirMembers.CreateAsync(new CreateFamilyMemberRequest("داوود", null));
            await theirMembers.CreateAsync(new CreateFamilyMemberRequest("خالد", null));
        }

        await using var myContext = ContextFor(mine);
        await MembersFor(myContext, mine).CreateAsync(new CreateFamilyMemberRequest("سليمان", null));

        var report = await ReportsFor(myContext, Clock).GetAsync();

        report.Structure.TotalMembers.Should().Be(1);
        report.Structure.Branches.Should().ContainSingle().Which.Name.Should().Be("سليمان");
    }

    /// <summary>
    /// The reference day is the server's, in UTC, and is returned so a client never re-derives
    /// "today" in its own zone and disagrees (design §5).
    /// </summary>
    [Fact]
    public async Task The_reference_day_is_the_servers_utc_day_and_is_returned()
    {
        var tenantId = await SeedTenantWithTreeAsync("reports-today");
        await using var context = ContextFor(tenantId);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 20, 23, 30, 0, TimeSpan.Zero));

        var report = await ReportsFor(context, clock).GetAsync();

        report.GeneratedOn.Should().Be(new DateOnly(2026, 12, 20));
    }
}
