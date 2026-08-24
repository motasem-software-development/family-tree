using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Persistence;

/// <summary>
/// The catalog is seeded by code, not by id, so running the seeder twice must not duplicate a
/// row — the api container re-runs seeding on every boot.
/// </summary>
public sealed class CountrySeedTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    // Matches the "al-saqqa" slug the SeedImportedFamily migration hardcodes, exactly as
    // DatabaseSeederTests does — DatabaseTestBase.InitializeAsync already ran that migration, so
    // a mismatched slug here would make the seeder's own guard throw before anything is seeded.
    private static readonly SeedOptions Options = new()
    {
        TenantName = "Al-Saqqa Family",
        TenantSlug = "al-saqqa",
        FamilyTreeName = "عائلة السقا",
        AdminEmail = "admin@example.com",
        AdminPassword = "Str0ng!Seed#Password"
    };

    private async Task RunSeederAsync()
    {
        await using var context = ContextFor(Guid.Empty);
        var hasher = new PasswordHasher<ApplicationUser>();
        var seeder = new DatabaseSeeder(context, hasher, Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);
        await seeder.SeedAsync();
    }

    [Fact]
    public async Task Countries_are_visible_without_a_tenant_in_scope()
    {
        await RunSeederAsync();

        // Guid.Empty: no tenant. A tenant-filtered entity would come back empty here.
        await using var context = ContextFor(Guid.Empty);

        var palestine = await context.Countries.FirstOrDefaultAsync(c => c.Code == "PS");

        palestine.Should().NotBeNull();
        palestine!.DialCode.Should().Be("+970");
        palestine.NameEn.Should().Be("Palestine");
        palestine.NameAr.Should().Be("فلسطين");
    }

    [Fact]
    public async Task Every_catalog_entry_is_present_exactly_once()
    {
        await RunSeederAsync();
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);

        var codes = await context.Countries.Select(c => c.Code).ToListAsync();

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().BeEquivalentTo(CountryCatalog.All.Select(entry => entry.Code));
    }
}
