using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence;
using FamilyTree.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.FamilyMembers;

public sealed class MemberContactServiceTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    // Matches CountrySeedTests and MemberContactPersistenceTests: DatabaseTestBase only
    // migrates, it does not seed. Tests below that read context.Countries run the seeder
    // themselves first.
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

    private static IFamilyMemberService ServiceFor(ApplicationDbContext context, Guid tenantId) =>
        new FamilyMemberService(context, new StubTenantContext(tenantId, Guid.CreateVersion7()), Clock);

    private async Task<Guid> ATenantWithATreeAsync(string slug)
    {
        await using var seed = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        seed.Tenants.Add(tenant);
        await seed.SaveChangesAsync();
        seed.FamilyTrees.Add(FamilyTreeAggregate.Create(tenant.Id, $"Tree {slug}", Now));
        await seed.SaveChangesAsync();
        return tenant.Id;
    }

    [Fact]
    public async Task Update_saves_and_returns_the_contact_details()
    {
        await SeedCountriesAsync();
        var tenantId = await ATenantWithATreeAsync("svc-save");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var palestine = await context.Countries.FirstAsync(c => c.Code == "PS");
        var member = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null));

        var updated = await service.UpdateAsync(member.Id, new UpdateFamilyMemberRequest(
            "سليمان", member.Version,
            NationalId: "123456789",
            MobileNumber: "+970599123456",
            WhatsAppNumber: "+970599123456",
            CountryId: palestine.Id));

        updated.NationalId.Should().Be("123456789");
        updated.MobileNumber.Should().Be("+970599123456");
        updated.CountryId.Should().Be(palestine.Id);
        updated.CountryCode.Should().Be("PS");
    }

    [Fact]
    public async Task Create_accepts_contact_details()
    {
        var tenantId = await ATenantWithATreeAsync("svc-create");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var created = await service.CreateAsync(new CreateFamilyMemberRequest(
            "داوود", null, NationalId: "987654321", MobileNumber: "+970599000111"));

        created.NationalId.Should().Be("987654321");
        created.MobileNumber.Should().Be("+970599000111");
    }

    [Fact]
    public async Task A_duplicate_national_id_within_the_tenant_is_a_conflict()
    {
        var tenantId = await ATenantWithATreeAsync("svc-dup");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var first = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null));
        var second = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null));
        await service.UpdateAsync(first.Id, new UpdateFamilyMemberRequest(
            "سليمان", first.Version, NationalId: "123456789"));

        var act = async () => await service.UpdateAsync(second.Id, new UpdateFamilyMemberRequest(
            "داوود", second.Version, NationalId: "123456789"));

        var thrown = await act.Should().ThrowAsync<ConflictException>();
        thrown.Which.Code.Should().Be("MEMBER_NATIONAL_ID_DUPLICATE");
    }

    [Fact]
    public async Task An_unknown_country_is_rejected()
    {
        var tenantId = await ATenantWithATreeAsync("svc-country");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var member = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null));

        var act = async () => await service.UpdateAsync(member.Id, new UpdateFamilyMemberRequest(
            "سليمان", member.Version, CountryId: 999_999));

        var thrown = await act.Should().ThrowAsync<DomainException>();
        thrown.Which.Code.Should().Be("MEMBER_COUNTRY_NOT_FOUND");
    }

    [Fact]
    public async Task A_mobile_number_that_contradicts_the_selected_country_is_rejected()
    {
        await SeedCountriesAsync();
        var tenantId = await ATenantWithATreeAsync("svc-dial");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var egypt = await context.Countries.FirstAsync(c => c.Code == "EG");
        var member = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null));

        // Egypt is +20; the number is Palestinian.
        var act = async () => await service.UpdateAsync(member.Id, new UpdateFamilyMemberRequest(
            "سليمان", member.Version, MobileNumber: "+970599123456", CountryId: egypt.Id));

        var thrown = await act.Should().ThrowAsync<DomainException>();
        thrown.Which.Code.Should().Be("MEMBER_PHONE_INVALID");
    }

    [Fact]
    public async Task A_WhatsApp_number_from_another_country_is_accepted()
    {
        await SeedCountriesAsync();
        var tenantId = await ATenantWithATreeAsync("svc-wa-dial");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var egypt = await context.Countries.FirstAsync(c => c.Code == "EG");
        var member = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null));

        // A member who moved to Egypt keeps the Palestinian WhatsApp number the rest of the
        // family already has saved. Only the mobile is held to the residence's dial code.
        var updated = await service.UpdateAsync(member.Id, new UpdateFamilyMemberRequest(
            "سليمان", member.Version,
            MobileNumber: "+201018124080",
            WhatsAppNumber: "+970599850444",
            CountryId: egypt.Id));

        updated.MobileNumber.Should().Be("+201018124080");
        updated.WhatsAppNumber.Should().Be("+970599850444");
    }

    [Fact]
    public async Task A_phone_number_is_accepted_when_no_country_is_selected()
    {
        var tenantId = await ATenantWithATreeAsync("svc-nodial");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var member = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null));

        // No country to check against, so shape validation is all that applies. A member abroad
        // may well keep a number from somewhere else.
        var updated = await service.UpdateAsync(member.Id, new UpdateFamilyMemberRequest(
            "سليمان", member.Version, MobileNumber: "+970599123456"));

        updated.MobileNumber.Should().Be("+970599123456");
    }

    [Fact]
    public async Task Update_clears_contact_details_that_are_omitted()
    {
        var tenantId = await ATenantWithATreeAsync("svc-clear");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);
        var member = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null));
        var saved = await service.UpdateAsync(member.Id, new UpdateFamilyMemberRequest(
            "سليمان", member.Version, NationalId: "123456789"));

        // Replace-semantics, exactly like the life details: omitting a field clears it, which
        // is what makes correcting a wrong entry possible.
        var cleared = await service.UpdateAsync(saved.Id, new UpdateFamilyMemberRequest(
            "سليمان", saved.Version));

        cleared.NationalId.Should().BeNull();
    }
}
