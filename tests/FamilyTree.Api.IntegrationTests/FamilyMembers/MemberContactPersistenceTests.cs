using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyTree.Api.IntegrationTests.FamilyMembers;

/// <summary>
/// The database-level half of the contact rules. The aggregate already refuses a malformed
/// national ID; these tests cover what only the database can hold — uniqueness scoped to a
/// tenant, and the check constraint that the bulk import cannot bypass.
/// </summary>
public sealed class MemberContactPersistenceTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    // Matches CountrySeedTests: DatabaseTestBase only migrates, it does not seed. The last test
    // below needs a real countries row, so it runs the seeder itself, exactly as CountrySeedTests
    // does.
    private static readonly SeedOptions Options = new()
    {
        TenantName = "Al-Saqqa Family",
        TenantSlug = "al-saqqa",
        FamilyTreeName = "عائلة السقا",
        AdminEmail = "admin@example.com",
        AdminPassword = "Str0ng!Seed#Password"
    };

    private async Task SeedCountriesAsync()
    {
        await using var context = ContextFor(Guid.Empty);
        var hasher = new PasswordHasher<ApplicationUser>();
        var seeder = new DatabaseSeeder(context, hasher, Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);
        await seeder.SeedAsync();
    }

    private async Task<(Guid TenantId, Guid TreeId)> ATenantWithATreeAsync(string slug)
    {
        await using var seed = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        seed.Tenants.Add(tenant);
        await seed.SaveChangesAsync();

        var tree = FamilyTreeAggregate.Create(tenant.Id, $"Tree {slug}", Now);
        seed.FamilyTrees.Add(tree);
        await seed.SaveChangesAsync();

        return (tenant.Id, tree.Id);
    }

    private static FamilyMember MemberWithNationalId(
        Guid tenantId, Guid treeId, string name, string nationalId)
    {
        var member = FamilyMember.Create(tenantId, treeId, null, name, Now);
        member.Update(name, null, null, false, new ContactDetails(nationalId, null, null, null), Now);
        return member;
    }

    [Fact]
    public async Task Two_members_in_one_tenant_cannot_share_a_national_id()
    {
        var (tenantId, treeId) = await ATenantWithATreeAsync("nid-dup");

        await using var context = ContextFor(tenantId);
        context.FamilyMembers.Add(MemberWithNationalId(tenantId, treeId, "سليمان", "123456789"));
        await context.SaveChangesAsync();

        context.FamilyMembers.Add(MemberWithNationalId(tenantId, treeId, "داوود", "123456789"));

        var act = async () => await context.SaveChangesAsync();

        var thrown = await act.Should().ThrowAsync<DbUpdateException>()
            .WithInnerException<DbUpdateException, PostgresException>();
        thrown.Which.SqlState.Should().Be("23505");
    }

    [Fact]
    public async Task Two_tenants_may_each_hold_the_same_national_id()
    {
        var first = await ATenantWithATreeAsync("nid-t1");
        var second = await ATenantWithATreeAsync("nid-t2");

        await using (var context = ContextFor(first.TenantId))
        {
            context.FamilyMembers.Add(
                MemberWithNationalId(first.TenantId, first.TreeId, "سليمان", "123456789"));
            await context.SaveChangesAsync();
        }

        await using var other = ContextFor(second.TenantId);
        other.FamilyMembers.Add(
            MemberWithNationalId(second.TenantId, second.TreeId, "داوود", "123456789"));

        var act = async () => await other.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Many_members_may_have_no_national_id()
    {
        var (tenantId, treeId) = await ATenantWithATreeAsync("nid-null");

        await using var context = ContextFor(tenantId);
        context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, null, "سليمان", Now));
        await context.SaveChangesAsync();
        context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, null, "داوود", Now));

        // The unique index is filtered on NOT NULL, so nulls do not collide.
        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_check_constraint_refuses_a_malformed_national_id_written_around_the_aggregate()
    {
        var (tenantId, treeId) = await ATenantWithATreeAsync("nid-ck");
        await using var context = ContextFor(tenantId);
        var member = FamilyMember.Create(tenantId, treeId, null, "سليمان", Now);
        context.FamilyMembers.Add(member);
        await context.SaveChangesAsync();

        // Raw SQL, bypassing the aggregate exactly as the bulk import would.
        var act = async () => await context.Database.ExecuteSqlAsync(
            $"UPDATE family_members SET national_id = '12345' WHERE id = {member.Id}");

        var thrown = await act.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("23514");
    }

    [Fact]
    public async Task Contact_details_round_trip_through_the_database()
    {
        await SeedCountriesAsync();
        var (tenantId, treeId) = await ATenantWithATreeAsync("nid-trip");
        Guid memberId;

        await using (var context = ContextFor(tenantId))
        {
            var palestine = await context.Countries.FirstAsync(c => c.Code == "PS");
            var member = FamilyMember.Create(tenantId, treeId, null, "سليمان", Now);
            member.Update(
                "سليمان", null, null, false,
                new ContactDetails("012345678", "+970599123456", "+201012345678", palestine.Id), Now);
            context.FamilyMembers.Add(member);
            await context.SaveChangesAsync();
            memberId = member.Id;
        }

        await using var reader = ContextFor(tenantId);
        var stored = await reader.FamilyMembers.AsNoTracking().FirstAsync(m => m.Id == memberId);

        stored.NationalId.Should().Be("012345678");
        stored.MobileNumber.Should().Be("+970599123456");
        stored.WhatsAppNumber.Should().Be("+201012345678");
        stored.CountryId.Should().NotBeNull();
    }
}
