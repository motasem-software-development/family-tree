# Member Contact Data Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add national ID, mobile number, WhatsApp number, and country of residence to the family member record, backed by a seeded `countries` reference table.

**Architecture:** A new system-level `countries` table (unfiltered by tenant, seeded idempotently) plus four nullable columns on `family_members`. Validation lives in the `FamilyMember` aggregate for anything self-contained (national ID format, E.164 shape) and in `FamilyMemberService` for anything needing the database (country existence, dial-code agreement, per-tenant national ID uniqueness). The four fields ride the existing single `Update()` command so one form submission still costs exactly one `Version` bump.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, PostgreSQL, xunit + FluentAssertions, React 19 + TanStack Query, react-i18next, vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-08-24-member-data-filters-export-design.md`

This is **Plan 1 of 4** from the spec's §9 decomposition. It ships alone: contact details can be recorded before any filter or export exists. Plans 2–4 (derivation and shared query, filter UI, Excel export) are written separately and depend on this one.

Section references of the form §N point at the design spec above, or — where the text says "specification §N" — at the source requirement document it implements.

## Global Constraints

- Target framework `net10.0`; `Nullable` enable; `TreatWarningsAsErrors` true (Directory.Build.props) — a warning fails the build.
- Branch: `member-data-filters-export`, already cut from `main`. Do not create another branch.
- **Migrations are never applied on startup.** Generate them, commit them, and apply with `dotnet ef database update` locally. See README "Running locally".
- Test frameworks are fixed: xunit 2.9.3 + FluentAssertions 7.2.0 (backend), vitest 4 + Testing Library (frontend). Do not add test packages.
- Every new user-facing string must be added to **both** `frontend/src/i18n/locales/en.json` and `ar.json`. `locales.test.ts` enforces key parity and will fail the suite otherwise.
- Every new domain error code must get an `errors.<CODE>` entry in both locale files.
- Arabic test fixtures use real Arabic names (`سليمان`, `داوود`), matching existing tests.
- National ID validation expression, verbatim from specification §4.2: `^[0-9]{9}$`
- E.164 validation expression: `^\+[1-9]\d{7,14}$`
- Contact fields are guarded by `Member.View` / `Member.Edit` — **no new permission is introduced** (spec §1.4).

### Refinement of spec §5.4

The spec's validation table assigns "Phone is E.164 **and agrees with the country dial code**" to the Domain. That is split here, because the dial code lives in the `countries` table and the `FamilyMember` aggregate cannot read the database:

- **Domain** validates E.164 *shape* (`^\+[1-9]\d{7,14}$`).
- **`FamilyMemberService`** validates dial-code *agreement*, after loading the country row.

Both raise the same code, `MEMBER_PHONE_INVALID`, so the split is invisible to clients. No other part of the spec changes.

---

### Task 1: The `Country` reference entity

`Country` deliberately does **not** extend `Entity`. `Entity` supplies a `Guid` id and created/updated timestamps; this is a small static reference table keyed by an `int` identity (spec §2.1) with no edit history worth keeping.

**Files:**
- Create: `src/FamilyTree.Domain/Countries/Country.cs`
- Test: `tests/FamilyTree.Domain.Tests/Countries/CountryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Country.Create(string code, string nameAr, string nameEn, string dialCode) -> Country`; instance properties `int Id`, `string Code`, `string NameAr`, `string NameEn`, `string DialCode`. Used by Tasks 2, 3, 4 and 7.

- [ ] **Step 1: Write the failing test**

Create `tests/FamilyTree.Domain.Tests/Countries/CountryTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.Countries;

namespace FamilyTree.Domain.Tests.Countries;

public class CountryTests
{
    [Fact]
    public void Create_uppercases_the_code_and_keeps_the_names()
    {
        var country = Country.Create("ps", "فلسطين", "Palestine", "+970");

        country.Code.Should().Be("PS");
        country.NameAr.Should().Be("فلسطين");
        country.NameEn.Should().Be("Palestine");
        country.DialCode.Should().Be("+970");
    }

    [Theory]
    [InlineData("P")]
    [InlineData("PSE")]
    [InlineData("P1")]
    [InlineData("")]
    public void Create_rejects_a_code_that_is_not_two_letters(string code)
    {
        var act = () => Country.Create(code, "فلسطين", "Palestine", "+970");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("COUNTRY_CODE_INVALID");
    }

    [Theory]
    [InlineData("970")]
    [InlineData("+")]
    [InlineData("+0")]
    [InlineData("+97a")]
    public void Create_rejects_a_dial_code_that_is_not_plus_digits(string dialCode)
    {
        var act = () => Country.Create("PS", "فلسطين", "Palestine", dialCode);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("COUNTRY_DIAL_CODE_INVALID");
    }

    [Fact]
    public void Create_rejects_a_missing_name()
    {
        var act = () => Country.Create("PS", "  ", "Palestine", "+970");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("COUNTRY_NAME_REQUIRED");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Domain.Tests --filter FullyQualifiedName~CountryTests`

Expected: FAIL — the build cannot resolve `FamilyTree.Domain.Countries.Country`.

- [ ] **Step 3: Write the implementation**

Create `src/FamilyTree.Domain/Countries/Country.cs`:

```csharp
using System.Text.RegularExpressions;
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Countries;

/// <summary>
/// A country of residence. System-level reference data, not tenant-owned — every tenant sees
/// the same list, so this entity carries no TenantId and no global query filter (design §2.1).
///
/// Deliberately NOT an <see cref="Entity"/>: that base supplies a Guid id and created/updated
/// timestamps, and this is a small seeded lookup keyed by an int identity with no edit history
/// worth keeping. The flag emoji is not stored — it is derivable from <see cref="Code"/> by
/// regional-indicator arithmetic, so the client computes it.
/// </summary>
public sealed partial class Country
{
    public const int MaxNameLength = 100;

    private Country() { }

    public int Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string NameAr { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public string DialCode { get; private set; } = null!;

    public static Country Create(string code, string nameAr, string nameEn, string dialCode) =>
        new()
        {
            Code = ValidateCode(code),
            NameAr = ValidateName(nameAr),
            NameEn = ValidateName(nameEn),
            DialCode = ValidateDialCode(dialCode)
        };

    /// <summary>ISO 3166-1 alpha-2, normalized to upper case so a seed list is case-insensitive.</summary>
    private static string ValidateCode(string code)
    {
        var trimmed = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!CodePattern().IsMatch(trimmed))
            throw new DomainException(
                "COUNTRY_CODE_INVALID", "Country code must be two letters (ISO 3166-1 alpha-2).");
        return trimmed;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("COUNTRY_NAME_REQUIRED", "Country name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException(
                "COUNTRY_NAME_TOO_LONG", $"Country name exceeds {MaxNameLength} characters.");
        return trimmed;
    }

    private static string ValidateDialCode(string dialCode)
    {
        var trimmed = dialCode?.Trim() ?? string.Empty;
        if (!DialCodePattern().IsMatch(trimmed))
            throw new DomainException(
                "COUNTRY_DIAL_CODE_INVALID", "Dial code must be '+' followed by 1-4 digits.");
        return trimmed;
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^\+[1-9]\d{0,3}$")]
    private static partial Regex DialCodePattern();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Domain.Tests --filter FullyQualifiedName~CountryTests`

Expected: PASS — 10 tests (xunit counts each `InlineData` case separately: 1 + 4 + 4 + 1).

- [ ] **Step 5: Commit**

```bash
git add src/FamilyTree.Domain/Countries/Country.cs tests/FamilyTree.Domain.Tests/Countries/CountryTests.cs
git commit -m "feat: add Country reference entity"
```

---

### Task 2: Persist and seed the country list

**Files:**
- Create: `src/FamilyTree.Infrastructure/Persistence/Configurations/CountryConfiguration.cs`
- Create: `src/FamilyTree.Infrastructure/Persistence/Seed/CountryCatalog.cs`
- Modify: `src/FamilyTree.Infrastructure/Persistence/ApplicationDbContext.cs` (add the `Countries` DbSet beside the others, around lines 27–37)
- Modify: `src/FamilyTree.Infrastructure/Persistence/Seed/DatabaseSeeder.cs` (add `SeedCountryCatalogAsync`)
- Test: `tests/FamilyTree.Api.IntegrationTests/Persistence/CountrySeedTests.cs`

**Interfaces:**
- Consumes: `Country.Create(...)` from Task 1.
- Produces: `context.Countries` (`DbSet<Country>`); `CountryCatalog.All` (`IReadOnlyList<(string Code, string NameAr, string NameEn, string DialCode)>`). Used by Tasks 3, 4, 5, 6 and 7.

- [ ] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Persistence/CountrySeedTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Persistence;

/// <summary>
/// The catalog is seeded by code, not by id, so running the seeder twice must not duplicate a
/// row — the api container re-runs seeding on every boot.
/// </summary>
public sealed class CountrySeedTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Countries_are_visible_without_a_tenant_in_scope()
    {
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
        await using var context = ContextFor(Guid.Empty);

        var codes = await context.Countries.Select(c => c.Code).ToListAsync();

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().BeEquivalentTo(CountryCatalog.All.Select(entry => entry.Code));
    }
}
```

Read `tests/FamilyTree.Api.IntegrationTests/Fixtures/DatabaseTestBase.cs` before running this: it supplies `ContextFor(Guid tenantId)` and `Now`, and this test relies on the seeder having run against the fixture database. If the base class does not seed, add the seed call the way the neighbouring `Persistence` tests do rather than inventing a new path.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~CountrySeedTests`

Expected: FAIL — `context.Countries` and `CountryCatalog` do not exist (build error). Docker must be running; the fixture starts PostgreSQL in a container.

- [ ] **Step 3: Write the catalog**

Create `src/FamilyTree.Infrastructure/Persistence/Seed/CountryCatalog.cs`:

```csharp
namespace FamilyTree.Infrastructure.Persistence.Seed;

/// <summary>
/// The seeded country list. Not exhaustive by design: Palestine, the Arab world, and the main
/// destinations of the Palestinian diaspora cover where this family actually lives. Adding a
/// country later is one entry here plus a re-run of the seeder, which is idempotent by code.
///
/// Note that DialCode is NOT unique — US and CA both use +1. Only Code is.
/// </summary>
public static class CountryCatalog
{
    public static IReadOnlyList<(string Code, string NameAr, string NameEn, string DialCode)> All { get; } =
    [
        ("PS", "فلسطين", "Palestine", "+970"),
        ("JO", "الأردن", "Jordan", "+962"),
        ("EG", "مصر", "Egypt", "+20"),
        ("SA", "السعودية", "Saudi Arabia", "+966"),
        ("AE", "الإمارات", "United Arab Emirates", "+971"),
        ("KW", "الكويت", "Kuwait", "+965"),
        ("QA", "قطر", "Qatar", "+974"),
        ("BH", "البحرين", "Bahrain", "+973"),
        ("OM", "عُمان", "Oman", "+968"),
        ("LB", "لبنان", "Lebanon", "+961"),
        ("SY", "سوريا", "Syria", "+963"),
        ("IQ", "العراق", "Iraq", "+964"),
        ("YE", "اليمن", "Yemen", "+967"),
        ("LY", "ليبيا", "Libya", "+218"),
        ("TR", "تركيا", "Türkiye", "+90"),
        ("US", "الولايات المتحدة", "United States", "+1"),
        ("CA", "كندا", "Canada", "+1"),
        ("GB", "المملكة المتحدة", "United Kingdom", "+44"),
        ("DE", "ألمانيا", "Germany", "+49"),
        ("SE", "السويد", "Sweden", "+46"),
        ("CL", "تشيلي", "Chile", "+56"),
        ("AU", "أستراليا", "Australia", "+61")
    ];
}
```

- [ ] **Step 4: Write the EF configuration**

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/CountryConfiguration.cs`:

```csharp
using FamilyTree.Domain.Countries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries");
        builder.HasKey(x => x.Id);

        // Identity rather than a client-assigned value: the seeder never supplies an id, and a
        // reference row's identity carries no meaning beyond being stable.
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.Code).IsRequired().HasMaxLength(2);
        builder.Property(x => x.NameAr).IsRequired().HasMaxLength(Country.MaxNameLength);
        builder.Property(x => x.NameEn).IsRequired().HasMaxLength(Country.MaxNameLength);
        builder.Property(x => x.DialCode).IsRequired().HasMaxLength(8);

        // The seeder's idempotency key. Unique so a concurrent double-seed fails loudly rather
        // than silently producing two rows for one country.
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
```

- [ ] **Step 5: Register the DbSet**

In `src/FamilyTree.Infrastructure/Persistence/ApplicationDbContext.cs`, add the using and the DbSet beside the existing ones:

```csharp
using FamilyTree.Domain.Countries;
```

```csharp
    public DbSet<Country> Countries => Set<Country>();
```

Then extend the comment at the end of `OnModelCreating` that explains which entities are unfiltered, so the reasoning stays in one place. Replace:

```csharp
        // Tenant and Permission are deliberately unfiltered: Tenant is the filter's own subject,
        // and the permission catalog is system-level rather than tenant-owned.
```

with:

```csharp
        // Tenant, Permission and Country are deliberately unfiltered: Tenant is the filter's own
        // subject, and the permission catalog and country list are system-level reference data
        // rather than tenant-owned (design §2.1).
```

**Do not** add a `HasQueryFilter` for `Country`. A filter would hide the entire list from every caller and break the member form's dropdown.

- [ ] **Step 6: Seed the catalog**

In `src/FamilyTree.Infrastructure/Persistence/Seed/DatabaseSeeder.cs`, add the using:

```csharp
using FamilyTree.Domain.Countries;
```

Add the call inside `SeedAsync`, immediately after `SeedPermissionCatalogAsync` — both are system-level catalogs and neither depends on a tenant:

```csharp
        await SeedPermissionCatalogAsync(now, ct);
        await SeedCountryCatalogAsync(ct);
```

Add the method beside `SeedPermissionCatalogAsync`, which it deliberately mirrors:

```csharp
    /// <summary>
    /// Idempotent by country code, exactly as the permission catalog is idempotent by permission
    /// code: the api container re-runs seeding on every boot, so this must be a no-op the second
    /// time. Countries already present are left untouched — a name correction shipped in
    /// CountryCatalog will not overwrite a row, which is the conservative choice for reference
    /// data a member row points at.
    /// </summary>
    private async Task SeedCountryCatalogAsync(CancellationToken ct)
    {
        var existing = await context.Countries.Select(c => c.Code).ToListAsync(ct);
        var missing = CountryCatalog.All.Where(entry => !existing.Contains(entry.Code)).ToList();
        if (missing.Count == 0) return;

        context.Countries.AddRange(
            missing.Select(entry => Country.Create(entry.Code, entry.NameAr, entry.NameEn, entry.DialCode)));
        await context.SaveChangesAsync(ct);
    }
```

- [ ] **Step 7: Generate the migration**

```bash
dotnet ef migrations add AddCountries \
  --project src/FamilyTree.Infrastructure \
  --startup-project src/FamilyTree.Api \
  --output-dir Persistence/Migrations
```

Open the generated `*_AddCountries.cs` and confirm `Up` creates the `countries` table with an identity `id`, four text columns, and a unique index on `code`. It must **not** touch any other table. If it does, the model has drifted — stop and investigate rather than editing the migration by hand.

- [ ] **Step 8: Apply the migration and run the test**

```bash
docker compose up -d postgres
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=familytree;Username=familytree;Password=devpassword"
dotnet ef database update --project src/FamilyTree.Infrastructure --startup-project src/FamilyTree.Api
dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~CountrySeedTests
```

Expected: PASS — 2 tests.

- [ ] **Step 9: Commit**

```bash
git add src/FamilyTree.Infrastructure tests/FamilyTree.Api.IntegrationTests/Persistence/CountrySeedTests.cs
git commit -m "feat: seed the country reference catalog"
```

---

### Task 3: Expose `GET /api/v1/countries`

**Files:**
- Create: `src/FamilyTree.Contracts/Countries/CountryResponse.cs`
- Create: `src/FamilyTree.Application/Countries/ICountryService.cs`
- Create: `src/FamilyTree.Infrastructure/Countries/CountryService.cs`
- Create: `src/FamilyTree.Api/Endpoints/Countries/CountryEndpoints.cs`
- Modify: `src/FamilyTree.Infrastructure/DependencyInjection.cs` (register `ICountryService`)
- Modify: `src/FamilyTree.Api/Program.cs` (call `MapCountryEndpoints()` beside the other `Map…Endpoints()` calls)
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/CountryEndpointTests.cs`

**Interfaces:**
- Consumes: `context.Countries` from Task 2.
- Produces: `CountryResponse(int Id, string Code, string NameAr, string NameEn, string DialCode)`; `ICountryService.ListAsync(CancellationToken)` returning `IReadOnlyList<CountryResponse>`. Used by Task 7.

Authenticated only, with **no permission requirement** (spec §5.2): the list is public reference data, and gating it would break the member form's dropdown for a user who can edit but not view members.

- [ ] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/CountryEndpointTests.cs`. Read one existing file in `tests/FamilyTree.Api.IntegrationTests/Endpoints/` first and reuse its client-and-token setup verbatim — the helper below is named for illustration and must be replaced with whatever that fixture actually exposes.

```csharp
using System.Net;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Countries;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

public sealed class CountryEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Countries_are_returned_to_any_authenticated_caller()
    {
        var client = await factory.AuthenticatedClientAsync();

        var countries = await client.GetFromJsonAsync<List<CountryResponse>>("/api/v1/countries");

        countries.Should().NotBeNull();
        countries!.Should().Contain(c => c.Code == "PS" && c.DialCode == "+970");
    }

    [Fact]
    public async Task Countries_are_refused_to_an_anonymous_caller()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/countries");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~CountryEndpointTests`

Expected: FAIL — `CountryResponse` does not exist (build error).

- [ ] **Step 3: Write the contract**

Create `src/FamilyTree.Contracts/Countries/CountryResponse.cs`:

```csharp
namespace FamilyTree.Contracts.Countries;

/// <summary>
/// One country of residence. Both names ship on every row rather than one resolved server-side:
/// the client switches language without refetching, and the same cached response serves both.
///
/// No flag field — the client derives the emoji from <paramref name="Code"/> by
/// regional-indicator arithmetic (design §2.1).
/// </summary>
public sealed record CountryResponse(
    int Id,
    string Code,
    string NameAr,
    string NameEn,
    string DialCode);
```

- [ ] **Step 4: Write the service interface**

Create `src/FamilyTree.Application/Countries/ICountryService.cs`:

```csharp
using FamilyTree.Contracts.Countries;

namespace FamilyTree.Application.Countries;

public interface ICountryService
{
    /// <summary>Every seeded country, ordered by English name. Never tenant-filtered.</summary>
    Task<IReadOnlyList<CountryResponse>> ListAsync(CancellationToken ct = default);
}
```

- [ ] **Step 5: Write the service**

Create `src/FamilyTree.Infrastructure/Countries/CountryService.cs`:

```csharp
using FamilyTree.Application.Countries;
using FamilyTree.Contracts.Countries;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Countries;

/// <summary>
/// Reference data, so no tenant filter applies and none is wanted — every tenant sees the same
/// list. Ordered by English name server-side; the client re-sorts for Arabic, where collation
/// differs from the byte order Postgres would give.
/// </summary>
public sealed class CountryService(ApplicationDbContext context) : ICountryService
{
    public async Task<IReadOnlyList<CountryResponse>> ListAsync(CancellationToken ct = default) =>
        await context.Countries
            .AsNoTracking()
            .OrderBy(c => c.NameEn)
            .Select(c => new CountryResponse(c.Id, c.Code, c.NameAr, c.NameEn, c.DialCode))
            .ToListAsync(ct);
}
```

- [ ] **Step 6: Register the service and map the endpoint**

In `src/FamilyTree.Infrastructure/DependencyInjection.cs`, register it beside the other scoped services:

```csharp
        services.AddScoped<ICountryService, CountryService>();
```

with the usings that file's style requires (`FamilyTree.Application.Countries`, `FamilyTree.Infrastructure.Countries`).

Create `src/FamilyTree.Api/Endpoints/Countries/CountryEndpoints.cs`:

```csharp
using FamilyTree.Application.Countries;

namespace FamilyTree.Api.Endpoints.Countries;

public static class CountryEndpoints
{
    public static IEndpointRouteBuilder MapCountryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/countries").WithTags("Countries");

        // Authenticated but deliberately not permission-guarded (design §5.2): this is public
        // reference data, and requiring Member.View would break the member form's country
        // dropdown for a user who can edit members but not browse the list.
        group.MapGet("/", async (ICountryService countries, CancellationToken ct) =>
            Results.Ok(await countries.ListAsync(ct)))
            .RequireAuthorization();

        return app;
    }
}
```

In `src/FamilyTree.Api/Program.cs`, add the using and the call beside the other `Map…Endpoints()` calls:

```csharp
using FamilyTree.Api.Endpoints.Countries;
```

```csharp
app.MapCountryEndpoints();
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~CountryEndpointTests`

Expected: PASS — 2 tests.

- [ ] **Step 8: Commit**

```bash
git add src/FamilyTree.Contracts/Countries src/FamilyTree.Application/Countries src/FamilyTree.Infrastructure/Countries src/FamilyTree.Infrastructure/DependencyInjection.cs src/FamilyTree.Api tests/FamilyTree.Api.IntegrationTests/Endpoints/CountryEndpointTests.cs
git commit -m "feat: add the countries endpoint"
```

---

### Task 4: Contact details on the `FamilyMember` aggregate

The heart of the plan. Validation must precede mutation so a rejected edit leaves `Version` untouched — the same discipline `ValidateLifeDetails` already follows.

**Files:**
- Create: `src/FamilyTree.Domain/FamilyMembers/ContactDetails.cs`
- Modify: `src/FamilyTree.Domain/FamilyMembers/FamilyMember.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs` (one temporary line, replaced in Task 6)
- Test: `tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberContactTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (the aggregate never reads the `countries` table).
- Produces:
  - `readonly record struct ContactDetails(string? NationalId, string? MobileNumber, string? WhatsAppNumber, int? CountryId)` in namespace `FamilyTree.Domain.FamilyMembers`, plus `ContactDetails.Empty`.
  - `FamilyMember.Create(Guid tenantId, Guid familyTreeId, Guid? parentId, string name, DateTimeOffset now, DateOnly? dateOfBirth = null, DateOnly? dateOfDeath = null, bool isDeceased = false, ContactDetails contact = default)`
  - `FamilyMember.Update(string name, DateOnly? dateOfBirth, DateOnly? dateOfDeath, bool isDeceased, ContactDetails contact, DateTimeOffset now)` — **note the new fifth parameter, before `now`.**
  - Properties `string? NationalId`, `string? MobileNumber`, `string? WhatsAppNumber`, `int? CountryId`.
  - Used by Tasks 5 and 6.

- [ ] **Step 1: Write the failing test**

Create `tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberContactTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Domain.Tests.FamilyMembers;

public class FamilyMemberContactTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember AMember() =>
        FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);

    private static ContactDetails Contact(
        string? nationalId = null,
        string? mobile = null,
        string? whatsApp = null,
        int? countryId = null) => new(nationalId, mobile, whatsApp, countryId);

    [Fact]
    public void A_new_member_has_no_contact_details()
    {
        var member = AMember();

        member.NationalId.Should().BeNull();
        member.MobileNumber.Should().BeNull();
        member.WhatsAppNumber.Should().BeNull();
        member.CountryId.Should().BeNull();
    }

    [Fact]
    public void Update_stores_the_contact_details()
    {
        var member = AMember();

        member.Update(
            "سليمان", null, null, false,
            Contact("123456789", "+970599123456", "+201012345678", 3), Now);

        member.NationalId.Should().Be("123456789");
        member.MobileNumber.Should().Be("+970599123456");
        member.WhatsAppNumber.Should().Be("+201012345678");
        member.CountryId.Should().Be(3);
    }

    [Fact]
    public void Update_carrying_life_and_contact_details_bumps_the_version_exactly_once()
    {
        var member = AMember();
        var before = member.Version;

        member.Update(
            "سليمان", new DateOnly(1950, 1, 1), null, false,
            Contact(nationalId: "123456789", mobile: "+970599123456"), Now);

        member.Version.Should().Be(before + 1);
    }

    [Theory]
    [InlineData("12345678")]     // eight digits
    [InlineData("1234567890")]   // ten digits
    [InlineData("12345ABC9")]    // letters
    [InlineData("12345 678")]    // space
    public void Update_rejects_a_national_id_that_is_not_nine_digits(string nationalId)
    {
        var member = AMember();

        var act = () => member.Update("سليمان", null, null, false, Contact(nationalId), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_NATIONAL_ID_INVALID");
    }

    [Fact]
    public void Update_accepts_a_national_id_with_a_leading_zero_and_preserves_it()
    {
        var member = AMember();

        member.Update("سليمان", null, null, false, Contact("012345678"), Now);

        member.NationalId.Should().Be("012345678");
    }

    [Theory]
    [InlineData("0599123456")]        // no international prefix
    [InlineData("+0599123456")]       // leading zero after the plus
    [InlineData("+97059")]            // too short
    [InlineData("+9705991234567890")] // too long
    [InlineData("+97059912a456")]     // letters
    public void Update_rejects_a_phone_number_that_is_not_e164(string phone)
    {
        var member = AMember();

        var act = () => member.Update("سليمان", null, null, false, Contact(mobile: phone), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_PHONE_INVALID");
    }

    [Fact]
    public void Update_validates_the_whatsapp_number_the_same_way_as_the_mobile()
    {
        var member = AMember();

        var act = () => member.Update("سليمان", null, null, false, Contact(whatsApp: "0599123456"), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_PHONE_INVALID");
    }

    [Fact]
    public void Update_normalizes_spaces_and_dashes_out_of_a_phone_number()
    {
        var member = AMember();

        member.Update("سليمان", null, null, false, Contact(mobile: "+970 599-123 456"), Now);

        member.MobileNumber.Should().Be("+970599123456");
    }

    [Fact]
    public void Update_treats_a_blank_contact_field_as_cleared()
    {
        var member = AMember();
        member.Update("سليمان", null, null, false, Contact("123456789", "+970599123456"), Now);

        member.Update("سليمان", null, null, false, Contact("   ", "  "), Now);

        member.NationalId.Should().BeNull();
        member.MobileNumber.Should().BeNull();
    }

    [Fact]
    public void A_rejected_contact_edit_leaves_the_member_untouched()
    {
        var member = AMember();
        member.Update("سليمان", null, null, false, Contact("123456789"), Now);
        var versionBefore = member.Version;

        // The name and the national ID are both fine; the mobile is not. Nothing may change.
        var act = () => member.Update(
            "داوود", null, null, false, Contact("987654321", "0599123456"), Now);

        act.Should().Throw<DomainException>();
        member.Version.Should().Be(versionBefore);
        member.Name.Should().Be("سليمان");
        member.NationalId.Should().Be("123456789");
    }

    [Fact]
    public void Rename_preserves_the_contact_details()
    {
        var member = AMember();
        member.Update("سليمان", null, null, false, Contact("123456789", "+970599123456", null, 1), Now);

        member.Rename("داوود", Now);

        member.NationalId.Should().Be("123456789");
        member.MobileNumber.Should().Be("+970599123456");
        member.CountryId.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Domain.Tests --filter FullyQualifiedName~FamilyMemberContactTests`

Expected: FAIL — `ContactDetails` does not exist and `Update` has no six-argument overload (build error).

- [ ] **Step 3: Add the `ContactDetails` value type**

Create `src/FamilyTree.Domain/FamilyMembers/ContactDetails.cs`:

```csharp
namespace FamilyTree.Domain.FamilyMembers;

/// <summary>
/// The contact and identification facts of a member, carried as one value so they enter the
/// aggregate through a single parameter rather than four — one edit, one version bump.
///
/// Replace-semantics, like the life details: a null or blank field clears the stored value.
/// That is what makes removing a wrong phone number possible.
/// </summary>
public readonly record struct ContactDetails(
    string? NationalId,
    string? MobileNumber,
    string? WhatsAppNumber,
    int? CountryId)
{
    public static ContactDetails Empty => new(null, null, null, null);
}
```

- [ ] **Step 4: Extend the aggregate**

In `src/FamilyTree.Domain/FamilyMembers/FamilyMember.cs`:

Change the class declaration to `partial` so the generated regexes can live on it:

```csharp
public sealed partial class FamilyMember : Entity, ITenantOwned
```

Add the using at the top:

```csharp
using System.Text.RegularExpressions;
```

Add the four properties after `IsDeceased`:

```csharp
    /// <summary>
    /// Palestinian national identification number: exactly nine digits, stored as text so a
    /// leading zero survives (specification §4). Null when unknown, which is the norm for the
    /// imported tree. Uniqueness is per-tenant and enforced by a filtered database index, not
    /// here — this aggregate cannot see its siblings.
    /// </summary>
    public string? NationalId { get; private set; }

    /// <summary>Normalized E.164, dialing code included. Null when unknown.</summary>
    public string? MobileNumber { get; private set; }

    /// <summary>
    /// Normalized E.164. Deliberately independent of <see cref="MobileNumber"/>: the number a
    /// person uses for WhatsApp is often not the one they answer calls on (specification §6).
    /// </summary>
    public string? WhatsAppNumber { get; private set; }

    /// <summary>
    /// References the system-level countries table. Not a navigation property: the aggregate
    /// has no business reading country names, and the reference is resolved at the read model.
    /// </summary>
    public int? CountryId { get; private set; }
```

Change `Create`'s signature to accept contact details:

```csharp
    public static FamilyMember Create(
        Guid tenantId, Guid familyTreeId, Guid? parentId, string name, DateTimeOffset now,
        DateOnly? dateOfBirth = null, DateOnly? dateOfDeath = null, bool isDeceased = false,
        ContactDetails contact = default)
```

and, inside the method, add the line immediately after `member.ApplyLifeDetails(...)`:

```csharp
        member.ApplyContactDetails(contact);
```

Replace `Update` with the version that carries contact details. Note the ordering: **all** validation runs before **any** mutation.

```csharp
    /// <summary>
    /// The single edit command behind the update endpoint. Name, life details, and contact
    /// details move together because one form submission is one edit: bumping
    /// <see cref="Version"/> more than once for a single save would leave the version returned
    /// to the client already stale against its own write.
    /// </summary>
    public void Update(
        string name, DateOnly? dateOfBirth, DateOnly? dateOfDeath, bool isDeceased,
        ContactDetails contact, DateTimeOffset now)
    {
        // Validate everything before mutating anything: a rejected update must leave the
        // entity exactly as it was, version included.
        var validatedName = ValidateName(name);
        var life = ValidateLifeDetails(dateOfBirth, dateOfDeath, isDeceased, now);
        var validatedContact = ValidateContactDetails(contact);

        Name = validatedName;
        DateOfBirth = life.DateOfBirth;
        DateOfDeath = life.DateOfDeath;
        IsDeceased = life.IsDeceased;
        NationalId = validatedContact.NationalId;
        MobileNumber = validatedContact.MobileNumber;
        WhatsAppNumber = validatedContact.WhatsAppNumber;
        CountryId = validatedContact.CountryId;
        Version++;
        Touch(now);
    }
```

Update `Rename` to carry the current contact details through, so a rename never silently clears them:

```csharp
    /// <summary>
    /// Changes only the name, leaving the life and contact details as they are. A delegate to
    /// <see cref="Update"/> rather than its own validate-then-mutate block, so there is exactly
    /// one path through the member's write rules and no way for the two to drift apart.
    /// </summary>
    public void Rename(string name, DateTimeOffset now) =>
        Update(
            name, DateOfBirth, DateOfDeath, IsDeceased,
            new ContactDetails(NationalId, MobileNumber, WhatsAppNumber, CountryId), now);
```

Add the private helpers beside `ApplyLifeDetails` / `ValidateLifeDetails`:

```csharp
    private void ApplyContactDetails(ContactDetails contact)
    {
        var validated = ValidateContactDetails(contact);
        NationalId = validated.NationalId;
        MobileNumber = validated.MobileNumber;
        WhatsAppNumber = validated.WhatsAppNumber;
        CountryId = validated.CountryId;
    }

    /// <summary>
    /// Validates and normalizes the contact details, returning the value to store. Blank is
    /// normalized to null throughout: a form submits "" for an untouched optional field, and
    /// storing that would make "empty string" and "unknown" two different states of the same
    /// fact.
    ///
    /// Dial-code agreement is NOT checked here. It needs the country's dial code, which lives
    /// in the countries table, and this aggregate cannot read the database —
    /// FamilyMemberService applies that check and raises the same MEMBER_PHONE_INVALID code
    /// (design §5.4, refined).
    /// </summary>
    private static ContactDetails ValidateContactDetails(ContactDetails contact) => new(
        ValidateNationalId(contact.NationalId),
        ValidatePhone(contact.MobileNumber),
        ValidatePhone(contact.WhatsAppNumber),
        contact.CountryId);

    private static string? ValidateNationalId(string? nationalId)
    {
        var trimmed = nationalId?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        if (!NationalIdPattern().IsMatch(trimmed))
            throw new DomainException(
                "MEMBER_NATIONAL_ID_INVALID", "A national ID must be exactly 9 digits.");

        // Returned exactly as matched, never reformatted: specification §4.2 requires the value
        // to be preserved as entered, and a leading zero is meaningful.
        return trimmed;
    }

    private static string? ValidatePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        // Spaces, dashes and parentheses are how people write phone numbers and how a pasted
        // number arrives; E.164 has no room for them. Stripping before validating accepts the
        // human form and stores the canonical one.
        var normalized = PhoneSeparators().Replace(phone.Trim(), string.Empty);

        if (!E164Pattern().IsMatch(normalized))
            throw new DomainException(
                "MEMBER_PHONE_INVALID",
                "A phone number must be in international format, e.g. +970599123456.");

        return normalized;
    }

    [GeneratedRegex("^[0-9]{9}$")]
    private static partial Regex NationalIdPattern();

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Pattern();

    [GeneratedRegex(@"[\s\-()]")]
    private static partial Regex PhoneSeparators();
```

- [ ] **Step 5: Fix the one existing caller the signature change breaks**

`Update`'s new parameter breaks `FamilyMemberService.UpdateAsync`. Task 6 rewrites that call properly; for now, make the build pass by threading the member's current values through — **this is a temporary line that Task 6 replaces, and Task 9 Step 4 checks it is gone.**

In `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`, change the `member.Update(...)` call to:

```csharp
        member.Update(
            request.Name,
            request.DateOfBirth,
            request.DateOfDeath,
            request.IsDeceased,
            new ContactDetails(
                member.NationalId, member.MobileNumber, member.WhatsAppNumber, member.CountryId),
            timeProvider.GetUtcNow());
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Domain.Tests`

Expected: PASS — the new `FamilyMemberContactTests` plus the existing `FamilyMemberTests`, all green. The existing tests must not need editing; if one fails, the change altered behaviour it should not have.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Domain/FamilyMembers src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberContactTests.cs
git commit -m "feat: validate member contact details in the aggregate"
```

---

### Task 5: Persist the contact columns

**Files:**
- Modify: `src/FamilyTree.Infrastructure/Persistence/Configurations/FamilyMemberConfiguration.cs`
- Create: migration `src/FamilyTree.Infrastructure/Persistence/Migrations/*_AddMemberContactDetails.cs` (generated)
- Test: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/MemberContactPersistenceTests.cs`

**Interfaces:**
- Consumes: the four properties from Task 4; the `countries` table from Task 2.
- Produces: the `ux_family_members_tenant_national_id` filtered unique index, which Task 6 relies on by name to detect duplicates.

- [ ] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/MemberContactPersistenceTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FluentAssertions;
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

        var thrown = await act.Should().ThrowAsync<DbUpdateException>();
        thrown.WithInnerException<DbUpdateException, PostgresException>()
              .Which.SqlState.Should().Be("23505");
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~MemberContactPersistenceTests`

Expected: FAIL — the `national_id` column does not exist; EF reports a missing-column error at query time.

- [ ] **Step 3: Configure the columns**

In `src/FamilyTree.Infrastructure/Persistence/Configurations/FamilyMemberConfiguration.cs`, add the using at the top:

```csharp
using FamilyTree.Domain.Countries;
```

Add the third check constraint inside the existing `ToTable` lambda, beside the two life-detail ones:

```csharp
            // Same belt-and-braces argument as the two constraints above: Phase 2.5's bulk
            // import writes members in volume and a CHECK cannot be bypassed by a code path
            // that forgets the aggregate. Uniqueness is a filtered index, not a CHECK — a
            // constraint cannot see other rows.
            table.HasCheckConstraint(
                "ck_member_national_id_digits",
                "national_id IS NULL OR national_id ~ '^[0-9]{9}$'");
```

Add the property configuration after the `IsDeceased` line:

```csharp
        // Text, not a numeric type: specification §4.2 requires the value to survive exactly as
        // entered, and any numeric column would eat a leading zero.
        builder.Property(x => x.NationalId).HasMaxLength(9);
        builder.Property(x => x.MobileNumber).HasMaxLength(20);
        builder.Property(x => x.WhatsAppNumber).HasMaxLength(20);
```

Add the country foreign key beside the other `HasOne` blocks:

```csharp
        // Restrict, not Cascade: a country is reference data, and deleting one must never
        // silently delete the people who live there. In practice countries are never deleted;
        // the constraint is what makes that a guarantee rather than a habit.
        builder.HasOne<Country>()
               .WithMany()
               .HasForeignKey(x => x.CountryId)
               .OnDelete(DeleteBehavior.Restrict);
```

Add the indexes beside the existing ones:

```csharp
        // Design §2.3. Per-tenant, not global: two tenants are unrelated families, and a global
        // unique index would let one tenant's write fail because of a row it cannot see —
        // leaking the existence of that record across the boundary. Filtered on NOT NULL
        // because the overwhelming majority of members have no recorded ID and nulls must not
        // collide with each other.
        builder.HasIndex(x => new { x.TenantId, x.NationalId })
               .HasDatabaseName("ux_family_members_tenant_national_id")
               .IsUnique()
               .HasFilter("national_id IS NOT NULL");

        // Specification §25 — both are filter predicates on the members list.
        builder.HasIndex(x => x.CountryId);
        builder.HasIndex(x => x.IsDeceased);
```

- [ ] **Step 4: Generate the migration**

```bash
dotnet ef migrations add AddMemberContactDetails \
  --project src/FamilyTree.Infrastructure \
  --startup-project src/FamilyTree.Api \
  --output-dir Persistence/Migrations
```

Open the generated file and confirm `Up` adds four columns, one check constraint, one filtered unique index, two plain indexes, and one foreign key to `countries` — and nothing else. Confirm `Down` reverses all of them.

- [ ] **Step 5: Apply the migration and run the tests**

```bash
dotnet ef database update --project src/FamilyTree.Infrastructure --startup-project src/FamilyTree.Api
dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~MemberContactPersistenceTests
```

Expected: PASS — 5 tests.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyTree.Infrastructure tests/FamilyTree.Api.IntegrationTests/FamilyMembers/MemberContactPersistenceTests.cs
git commit -m "feat: persist member contact details"
```

---

### Task 6: Carry contact details through the API

**Files:**
- Modify: `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberResponse.cs`
- Modify: `src/FamilyTree.Contracts/FamilyMembers/CreateFamilyMemberRequest.cs`
- Modify: `src/FamilyTree.Contracts/FamilyMembers/UpdateFamilyMemberRequest.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/MemberContactServiceTests.cs`

**Interfaces:**
- Consumes: `ContactDetails` (Task 4), the `ux_family_members_tenant_national_id` index (Task 5), `context.Countries` (Task 2).
- Produces: `FamilyMemberResponse` with five extra trailing members — `NationalId`, `MobileNumber`, `WhatsAppNumber`, `CountryId`, `CountryCode` — consumed by Tasks 7–8 and by Plans 2–4.

- [ ] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/MemberContactServiceTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.FamilyMembers;

public sealed class MemberContactServiceTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

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
    public async Task A_phone_number_that_contradicts_the_selected_country_is_rejected()
    {
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~MemberContactServiceTests`

Expected: FAIL — `UpdateFamilyMemberRequest` has no `NationalId` parameter (build error).

- [ ] **Step 3: Extend the contracts**

`src/FamilyTree.Contracts/FamilyMembers/FamilyMemberResponse.cs` — append five members. **Append, never reorder:** these are positional records, and inserting a member in the middle silently re-maps every existing call site.

```csharp
namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// A single member as returned by the API. <paramref name="Version"/> must be echoed back on
/// update — it is the optimistic concurrency token (design spec §3.1).
///
/// <paramref name="CountryCode"/> rides along with <paramref name="CountryId"/> so a client can
/// render a flag and a name without joining against the country list it may not have loaded yet.
/// </summary>
public sealed record FamilyMemberResponse(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateOnly? DateOfBirth,
    DateOnly? DateOfDeath,
    bool IsDeceased,
    string? NationalId,
    string? MobileNumber,
    string? WhatsAppNumber,
    int? CountryId,
    string? CountryCode);
```

`src/FamilyTree.Contracts/FamilyMembers/CreateFamilyMemberRequest.cs` — append four optional parameters:

```csharp
public sealed record CreateFamilyMemberRequest(
    string Name,
    Guid? ParentId,
    DateOnly? DateOfBirth = null,
    DateOnly? DateOfDeath = null,
    bool IsDeceased = false,
    string? NationalId = null,
    string? MobileNumber = null,
    string? WhatsAppNumber = null,
    int? CountryId = null);
```

`src/FamilyTree.Contracts/FamilyMembers/UpdateFamilyMemberRequest.cs` — append four optional parameters:

```csharp
public sealed record UpdateFamilyMemberRequest(
    string Name,
    int Version,
    Guid? ParentId = null,
    Guid? TenantId = null,
    Guid? FamilyTreeId = null,
    DateOnly? DateOfBirth = null,
    DateOnly? DateOfDeath = null,
    bool IsDeceased = false,
    string? NationalId = null,
    string? MobileNumber = null,
    string? WhatsAppNumber = null,
    int? CountryId = null);
```

In that file's XML doc, change:

> The life details are replace-semantics, not patch-semantics: omitting a date clears it.

to:

> The life details and contact details are replace-semantics, not patch-semantics: omitting a date, a phone number, or a national ID clears it. That is what makes correcting a mistaken death record — or a wrong phone number — possible.

- [ ] **Step 4: Wire the service**

In `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`:

Add two constants beside `ForeignKeyViolation`:

```csharp
    /// <summary>PostgreSQL SQLSTATE for a unique violation.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>The filtered unique index behind the per-tenant national ID rule.</summary>
    private const string NationalIdIndex = "ux_family_members_tenant_national_id";
```

Add the contact resolution helpers beside `Map`:

```csharp
    /// <summary>
    /// Resolves the contact details against the country list: the country must exist, and a
    /// supplied phone number must start with that country's dialing code.
    ///
    /// The dial-code check lives here rather than in the aggregate because it needs a row the
    /// aggregate cannot read (design §5.4, refined). It raises the same MEMBER_PHONE_INVALID
    /// code the aggregate's shape check does, so the split is invisible to clients.
    ///
    /// With no country selected there is nothing to check against, and the number is accepted on
    /// shape alone — a member living abroad may well keep a number from somewhere else.
    /// </summary>
    private async Task<ContactDetails> ResolveContactAsync(
        string? nationalId, string? mobile, string? whatsApp, int? countryId, CancellationToken ct)
    {
        if (countryId is not { } id)
            return new ContactDetails(nationalId, mobile, whatsApp, null);

        var dialCode = await context.Countries
            .Where(c => c.Id == id)
            .Select(c => c.DialCode)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException(
                "MEMBER_COUNTRY_NOT_FOUND", "The specified country does not exist.");

        EnsureDialCodeAgrees(mobile, dialCode);
        EnsureDialCodeAgrees(whatsApp, dialCode);

        return new ContactDetails(nationalId, mobile, whatsApp, id);
    }

    /// <summary>
    /// Separators are stripped here as well as in the aggregate, because this check runs first
    /// and a number written "+970 599 123 456" must compare against the same canonical form the
    /// aggregate will eventually store.
    /// </summary>
    private static void EnsureDialCodeAgrees(string? phone, string dialCode)
    {
        // Blank is "not supplied"; the aggregate normalizes it to null.
        if (string.IsNullOrWhiteSpace(phone)) return;

        var normalized = phone.Replace(" ", string.Empty)
                              .Replace("-", string.Empty)
                              .Replace("(", string.Empty)
                              .Replace(")", string.Empty);

        if (!normalized.StartsWith(dialCode, StringComparison.Ordinal))
            throw new DomainException(
                "MEMBER_PHONE_INVALID",
                $"This phone number does not match the selected country's dialing code ({dialCode}).");
    }

    /// <summary>
    /// The country code for one member, or null when they have no country. A separate keyed
    /// lookup rather than a navigation property: the aggregate deliberately holds only
    /// CountryId, and one read of a 22-row table is not worth complicating the entity for.
    /// </summary>
    private async Task<FamilyMemberResponse> MapWithCountryAsync(
        FamilyMember member, CancellationToken ct)
    {
        if (member.CountryId is not { } id) return Map(member);

        var code = await context.Countries
            .Where(c => c.Id == id)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(ct);

        return Map(member, code);
    }
```

In `CreateAsync`, resolve the contact details and pass them to the aggregate. Replace the `FamilyMember.Create(...)` call with:

```csharp
        var contact = await ResolveContactAsync(
            request.NationalId, request.MobileNumber, request.WhatsAppNumber, request.CountryId, ct);

        var member = FamilyMember.Create(
            tenant.TenantId, tree.Id, request.ParentId, request.Name, timeProvider.GetUtcNow(),
            request.DateOfBirth, request.DateOfDeath, request.IsDeceased, contact);
```

In `UpdateAsync`, replace the temporary `member.Update(...)` call added in Task 4 Step 5 with the real one:

```csharp
        var contact = await ResolveContactAsync(
            request.NationalId, request.MobileNumber, request.WhatsAppNumber, request.CountryId, ct);

        member.Update(
            request.Name,
            request.DateOfBirth,
            request.DateOfDeath,
            request.IsDeceased,
            contact,
            timeProvider.GetUtcNow());
```

Add this catch clause to the `try/catch` around `SaveChangesAsync` in **both** `CreateAsync` and `UpdateAsync`. In `CreateAsync` it must sit **above** the existing foreign-key clause, because the more specific `when` has to be tested first:

```csharp
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: UniqueViolation } pg
                  && pg.ConstraintName == NationalIdIndex)
        {
            // Caught rather than pre-checked with a SELECT: check-then-insert races, and the
            // index is the only thing that actually holds the invariant. A ConflictException
            // (409) rather than a DomainException (400) because this depends on current state,
            // not on the request being malformed.
            throw new ConflictException(
                "MEMBER_NATIONAL_ID_DUPLICATE",
                "Another member already has this national ID.");
        }
```

Extend `Map` to carry the new fields. It is `static`, so the country code is passed in rather than looked up:

```csharp
    internal static FamilyMemberResponse Map(FamilyMember member, string? countryCode = null) => new(
        member.Id, member.Name, member.ParentId, member.Version, member.CreatedAt, member.UpdatedAt,
        member.DateOfBirth, member.DateOfDeath, member.IsDeceased,
        member.NationalId, member.MobileNumber, member.WhatsAppNumber, member.CountryId, countryCode);
```

Replace `return Map(member);` with `return await MapWithCountryAsync(member, ct);` in `CreateAsync`, `UpdateAsync` and `MoveAsync`.

Replace `GetAsync`'s body with:

```csharp
    public async Task<FamilyMemberResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var member = await context.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        return member is null ? null : await MapWithCountryAsync(member, ct);
    }
```

Replace `ListAsync`'s body with a single query — a per-member lookup here would be 349 round trips:

```csharp
    public async Task<IReadOnlyList<FamilyMemberResponse>> ListAsync(CancellationToken ct = default)
    {
        var rows = await context.FamilyMembers
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                Member = m,
                CountryCode = context.Countries
                    .Where(c => c.Id == m.CountryId)
                    .Select(c => c.Code)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows.Select(row => Map(row.Member, row.CountryCode)).ToList();
    }
```

`ContactDetails` lives in `FamilyTree.Domain.FamilyMembers`, which this file already imports; no new using is needed for it.

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test`

Expected: PASS. The five appended `FamilyMemberResponse` members break any test that constructs one positionally with the old arity — fix those by appending the trailing arguments (`null, null, null, null, null`). **Do not reorder the record** to make a call site compile.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyTree.Contracts src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs tests/FamilyTree.Api.IntegrationTests/FamilyMembers/MemberContactServiceTests.cs
git commit -m "feat: carry member contact details through the API"
```

---

### Task 7: Country reference data on the client

**Files:**
- Create: `frontend/src/features/countries/types.ts`
- Create: `frontend/src/features/countries/countriesApi.ts`
- Create: `frontend/src/features/countries/useCountries.ts`
- Create: `frontend/src/features/countries/flagEmoji.ts`
- Test: `frontend/src/features/countries/flagEmoji.test.ts`
- Test: `frontend/src/features/countries/countriesApi.test.ts`

**Interfaces:**
- Consumes: `GET /api/v1/countries` from Task 3.
- Produces: `Country` interface; `countriesApi.list()`; `useCountriesQuery()`; `flagEmoji(code: string): string`; `countryName(country: Country, language: string): string`. Used by Task 8 and by Plan 3's country filter.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/features/countries/flagEmoji.test.ts`:

```typescript
import { describe, expect, it } from 'vitest'
import { countryName, flagEmoji } from './flagEmoji'

describe('flagEmoji', () => {
  it('maps an alpha-2 code to its regional indicator pair', () => {
    expect(flagEmoji('PS')).toBe('🇵🇸')
    expect(flagEmoji('EG')).toBe('🇪🇬')
  })

  it('accepts a lowercase code', () => {
    expect(flagEmoji('ps')).toBe('🇵🇸')
  })

  it('returns an empty string for anything that is not two letters', () => {
    // A code the API has never sent must not render as mojibake next to a real flag.
    expect(flagEmoji('PSE')).toBe('')
    expect(flagEmoji('')).toBe('')
    expect(flagEmoji('P1')).toBe('')
  })
})

describe('countryName', () => {
  const palestine = { id: 1, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' }

  it('picks the Arabic name for Arabic', () => {
    expect(countryName(palestine, 'ar')).toBe('فلسطين')
  })

  it('picks the English name for anything else', () => {
    expect(countryName(palestine, 'en')).toBe('Palestine')
  })

  it('matches a regional language tag', () => {
    expect(countryName(palestine, 'ar-PS')).toBe('فلسطين')
  })
})
```

Create `frontend/src/features/countries/countriesApi.test.ts`:

```typescript
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { countriesApi } from './countriesApi'
import { tokenStorage } from '../../services/tokenStorage'

const jsonResponse = (body: unknown, status = 200): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

describe('countriesApi', () => {
  beforeEach(() => {
    tokenStorage.write({ accessToken: 'token', refreshToken: 'refresh' })
    vi.restoreAllMocks()
  })

  it('lists countries', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse([{ id: 1, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' }]),
    )
    vi.stubGlobal('fetch', fetchMock)

    const countries = await countriesApi.list()

    expect(fetchMock).toHaveBeenCalledWith('/api/v1/countries', expect.anything())
    expect(countries[0].dialCode).toBe('+970')
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/countries`

Expected: FAIL — cannot resolve `./flagEmoji` or `./countriesApi`.

- [ ] **Step 3: Write the implementation**

Create `frontend/src/features/countries/types.ts`:

```typescript
/** One country of residence, as returned by `GET /api/v1/countries`. */
export interface Country {
  id: number
  /** ISO 3166-1 alpha-2, upper case. The flag emoji is derived from this. */
  code: string
  nameAr: string
  nameEn: string
  /** E.164 dialing code, leading `+`. Not unique — US and CA are both `+1`. */
  dialCode: string
}
```

Create `frontend/src/features/countries/flagEmoji.ts`:

```typescript
import type { Country } from './types'

/** 'A' → U+1F1E6, the first regional indicator. */
const REGIONAL_INDICATOR_A = 0x1f1e6
const LETTER_A = 'A'.charCodeAt(0)

/**
 * The flag for an alpha-2 code, built from the two regional indicator symbols rather than
 * shipped as an asset — every platform that renders flags at all renders these.
 *
 * Returns '' for anything that is not two ASCII letters. A malformed code must render as
 * nothing rather than as two stray boxes beside a real flag.
 */
export const flagEmoji = (code: string): string => {
  const upper = code.toUpperCase()
  if (!/^[A-Z]{2}$/.test(upper)) return ''

  return String.fromCodePoint(
    ...[...upper].map((letter) => REGIONAL_INDICATOR_A + letter.charCodeAt(0) - LETTER_A),
  )
}

/**
 * The country name in the active language. Both names ride on every row, so switching language
 * never refetches — and `startsWith` rather than equality because i18next reports regional tags
 * like 'ar-PS'.
 */
export const countryName = (country: Country, language: string): string =>
  language.startsWith('ar') ? country.nameAr : country.nameEn
```

Create `frontend/src/features/countries/countriesApi.ts`:

```typescript
import { apiFetch } from '../../services/apiClient'
import type { Country } from './types'

const COUNTRIES = '/api/v1/countries'

export const countriesApi = {
  list: (): Promise<Country[]> => apiFetch<Country[]>(COUNTRIES),
}
```

Create `frontend/src/features/countries/useCountries.ts`:

```typescript
import { useQuery } from '@tanstack/react-query'
import { countriesApi } from './countriesApi'
import type { Country } from './types'

export const countryKeys = {
  all: ['countries'] as const,
}

/**
 * Reference data: seeded server-side and changed only by a deploy, so it is cached for the
 * session rather than refetched. Every consumer — the member form here, the country filter in
 * the next plan — shares this one query.
 */
export const useCountriesQuery = () =>
  useQuery<Country[]>({
    queryKey: countryKeys.all,
    queryFn: () => countriesApi.list(),
    staleTime: Infinity,
  })
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/countries`

Expected: PASS — 7 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/countries
git commit -m "feat: add country reference data to the client"
```

---

### Task 8: Contact fields in the member form

**Files:**
- Create: `frontend/src/features/members/contactDetails.ts`
- Create: `frontend/src/features/members/ContactFields.tsx`
- Create: `frontend/src/features/members/PhoneInput.tsx`
- Modify: `frontend/src/features/members/types.ts`
- Modify: `frontend/src/features/members/membersApi.ts`
- Modify: `frontend/src/features/members/useMembers.ts`
- Modify: `frontend/src/features/members/MemberForm.tsx`
- Modify: `frontend/src/features/members/MembersPage.tsx`
- Modify: `frontend/src/i18n/locales/en.json`, `frontend/src/i18n/locales/ar.json`
- Test: `frontend/src/features/members/contactDetails.test.ts`
- Test: `frontend/src/features/members/ContactFields.test.tsx`

**Interfaces:**
- Consumes: `Country`, `useCountriesQuery`, `flagEmoji`, `countryName` (Task 7); the extended `FamilyMemberResponse` (Task 6).
- Produces: `ContactDetails` interface `{ nationalId: string | null; mobileNumber: string | null; whatsAppNumber: string | null; countryId: number | null }`; `EMPTY_CONTACT_DETAILS`; `contactDetailsOf(member)`; `isValidNationalId(value)`; `splitPhone(e164, countries)`; `joinPhone(dialCode, local)`.

`contactDetailsOf` mirrors `lifeDetailsOf` and exists for the reason its doc comment gives: an API deployed a step behind the frontend omits these fields entirely, and they arrive as `undefined`.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/features/members/contactDetails.test.ts`:

```typescript
import { describe, expect, it } from 'vitest'
import {
  EMPTY_CONTACT_DETAILS,
  contactDetailsOf,
  isValidNationalId,
  joinPhone,
  splitPhone,
} from './contactDetails'
import type { Country } from '../countries/types'

const countries: Country[] = [
  { id: 1, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' },
  { id: 2, code: 'EG', nameAr: 'مصر', nameEn: 'Egypt', dialCode: '+20' },
  { id: 3, code: 'GB', nameAr: 'المملكة المتحدة', nameEn: 'United Kingdom', dialCode: '+44' },
]

describe('contactDetailsOf', () => {
  it('normalizes absent fields to null', () => {
    // An API a deploy behind omits these entirely; `undefined` must not reach the inputs.
    expect(contactDetailsOf({})).toEqual(EMPTY_CONTACT_DETAILS)
  })

  it('reads the fields when present', () => {
    expect(
      contactDetailsOf({
        nationalId: '123456789',
        mobileNumber: '+970599123456',
        whatsAppNumber: null,
        countryId: 1,
      }),
    ).toEqual({
      nationalId: '123456789',
      mobileNumber: '+970599123456',
      whatsAppNumber: null,
      countryId: 1,
    })
  })
})

describe('splitPhone', () => {
  it('splits a stored number into its dial code and the rest', () => {
    expect(splitPhone('+970599123456', countries)).toEqual({
      dialCode: '+970',
      local: '599123456',
    })
  })

  it('prefers the longest matching dial code', () => {
    expect(splitPhone('+201012345678', countries)).toEqual({
      dialCode: '+20',
      local: '1012345678',
    })
  })

  it('returns the whole number as local when no dial code matches', () => {
    expect(splitPhone('+998901234567', countries)).toEqual({
      dialCode: '',
      local: '+998901234567',
    })
  })

  it('handles an empty number', () => {
    expect(splitPhone(null, countries)).toEqual({ dialCode: '', local: '' })
  })
})

describe('joinPhone', () => {
  it('concatenates the dial code and the local number', () => {
    expect(joinPhone('+970', '599123456')).toBe('+970599123456')
  })

  it('strips separators from the local number', () => {
    expect(joinPhone('+970', '599 123-456')).toBe('+970599123456')
  })

  it('drops a leading zero from the local number', () => {
    // People write their number as they dial it domestically; the trunk zero has no place
    // in E.164 and leaving it in produces a number that cannot be called.
    expect(joinPhone('+970', '0599123456')).toBe('+970599123456')
  })

  it('returns null when the local number is empty', () => {
    expect(joinPhone('+970', '')).toBeNull()
    expect(joinPhone('+970', '   ')).toBeNull()
  })

  it('returns null when no dial code is chosen', () => {
    expect(joinPhone('', '599123456')).toBeNull()
  })
})

describe('isValidNationalId', () => {
  it('accepts exactly nine digits', () => {
    expect(isValidNationalId('123456789')).toBe(true)
    expect(isValidNationalId('012345678')).toBe(true)
  })

  it('accepts an empty value, which means "not recorded"', () => {
    expect(isValidNationalId('')).toBe(true)
  })

  it('rejects anything else', () => {
    expect(isValidNationalId('12345678')).toBe(false)
    expect(isValidNationalId('1234567890')).toBe(false)
    expect(isValidNationalId('12345ABC9')).toBe(false)
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/members/contactDetails.test.ts`

Expected: FAIL — cannot resolve `./contactDetails`.

- [ ] **Step 3: Write the contact details module**

Create `frontend/src/features/members/contactDetails.ts`:

```typescript
import type { Country } from '../countries/types'

/**
 * The editable contact facts of a member, in the shape the form holds them. Mirrors
 * `LifeDetails`, including its replace-semantics: a null field clears the stored value.
 */
export interface ContactDetails {
  nationalId: string | null
  mobileNumber: string | null
  whatsAppNumber: string | null
  countryId: number | null
}

export const EMPTY_CONTACT_DETAILS: ContactDetails = {
  nationalId: null,
  mobileNumber: null,
  whatsAppNumber: null,
  countryId: null,
}

/**
 * The single normalization point between an API response and the rest of the UI, for exactly
 * the reason `lifeDetailsOf` documents: an API deployed a step behind the frontend omits these
 * fields entirely and they arrive as `undefined`, which is not `null` and would reach the
 * inputs as an uncontrolled-component warning at best.
 */
export const contactDetailsOf = (member: {
  nationalId?: string | null
  mobileNumber?: string | null
  whatsAppNumber?: string | null
  countryId?: number | null
}): ContactDetails => ({
  nationalId: member.nationalId ?? null,
  mobileNumber: member.mobileNumber ?? null,
  whatsAppNumber: member.whatsAppNumber ?? null,
  countryId: member.countryId ?? null,
})

const SEPARATORS = /[\s\-()]/g

/** Mirrors the server's `^[0-9]{9}$`. Empty is valid: the field is optional. */
export const isValidNationalId = (value: string): boolean =>
  value === '' || /^[0-9]{9}$/.test(value)

/**
 * Splits a stored E.164 number into the dial code the picker should show and the local part.
 *
 * Longest match wins: dial codes are not prefix-free (+1 vs +1-something in fuller lists), and
 * picking a shorter prefix would leave a stray digit at the front of the local number. An
 * unrecognised code falls back to showing the whole number, so a member whose country was never
 * seeded is still editable rather than silently truncated.
 */
export const splitPhone = (
  e164: string | null,
  countries: readonly Country[],
): { dialCode: string; local: string } => {
  if (e164 === null || e164 === '') return { dialCode: '', local: '' }

  const match = countries
    .map((country) => country.dialCode)
    .filter((dialCode) => e164.startsWith(dialCode))
    .sort((a, b) => b.length - a.length)[0]

  if (match === undefined) return { dialCode: '', local: e164 }

  return { dialCode: match, local: e164.slice(match.length) }
}

/**
 * Composes the picker's dial code and the typed local number into one E.164 string —
 * specification §5.2's "the system combines the country dialing code and local number and
 * stores +970599123456".
 *
 * The leading trunk zero is dropped: people write their number the way they dial it at home,
 * and '+9700599123456' is not a number anyone can reach.
 */
export const joinPhone = (dialCode: string, local: string): string | null => {
  const digits = local.replace(SEPARATORS, '').replace(/^0+/, '')
  if (digits === '' || dialCode === '') return null

  return `${dialCode}${digits}`
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/members/contactDetails.test.ts`

Expected: PASS — 14 tests.

- [ ] **Step 5: Commit the module**

```bash
git add frontend/src/features/members/contactDetails.ts frontend/src/features/members/contactDetails.test.ts
git commit -m "feat: add contact details helpers to the member form model"
```

- [ ] **Step 6: Add the translation keys**

In `frontend/src/i18n/locales/en.json`, add to the `members` block:

```json
    "contactSection": "Contact details",
    "nationalId": "National ID",
    "nationalIdHint": "Exactly 9 digits.",
    "nationalIdInvalid": "A national ID must be exactly 9 digits.",
    "country": "Country of residence",
    "noCountry": "Not recorded",
    "mobileNumber": "Mobile number",
    "whatsAppNumber": "WhatsApp number",
    "sameAsMobile": "Same as mobile number",
    "localNumber": "Number",
    "dialCode": "Dialing code"
```

and to the `errors` block:

```json
    "MEMBER_NATIONAL_ID_INVALID": "A national ID must be exactly 9 digits.",
    "MEMBER_NATIONAL_ID_DUPLICATE": "Another member already has this national ID.",
    "MEMBER_PHONE_INVALID": "Enter the number in international format, e.g. +970599123456.",
    "MEMBER_COUNTRY_NOT_FOUND": "That country is no longer available. Reload and try again.",
    "COUNTRY_CODE_INVALID": "That country code is not valid.",
    "COUNTRY_NAME_REQUIRED": "A country name is required.",
    "COUNTRY_NAME_TOO_LONG": "That country name is too long (100 characters maximum).",
    "COUNTRY_DIAL_CODE_INVALID": "That dialing code is not valid."
```

In `frontend/src/i18n/locales/ar.json`, add the same keys to the same two blocks:

```json
    "contactSection": "بيانات الاتصال",
    "nationalId": "رقم الهوية",
    "nationalIdHint": "تسعة أرقام بالضبط.",
    "nationalIdInvalid": "يجب أن يتكوّن رقم الهوية من تسعة أرقام بالضبط.",
    "country": "بلد الإقامة",
    "noCountry": "غير مسجّل",
    "mobileNumber": "رقم الجوال",
    "whatsAppNumber": "رقم واتساب",
    "sameAsMobile": "نفس رقم الجوال",
    "localNumber": "الرقم",
    "dialCode": "رمز الاتصال"
```

```json
    "MEMBER_NATIONAL_ID_INVALID": "يجب أن يتكوّن رقم الهوية من تسعة أرقام بالضبط.",
    "MEMBER_NATIONAL_ID_DUPLICATE": "رقم الهوية هذا مسجّل لفرد آخر.",
    "MEMBER_PHONE_INVALID": "أدخل الرقم بالصيغة الدولية، مثال: +970599123456.",
    "MEMBER_COUNTRY_NOT_FOUND": "هذا البلد لم يعد متاحاً. أعد التحميل وحاول مرة أخرى.",
    "COUNTRY_CODE_INVALID": "رمز البلد غير صالح.",
    "COUNTRY_NAME_REQUIRED": "اسم البلد مطلوب.",
    "COUNTRY_NAME_TOO_LONG": "اسم البلد طويل جداً (100 حرف كحد أقصى).",
    "COUNTRY_DIAL_CODE_INVALID": "رمز الاتصال غير صالح."
```

- [ ] **Step 7: Verify key parity**

Run: `cd frontend && npx vitest run src/i18n/locales.test.ts`

Expected: PASS. A failure here means a key exists in one file and not the other — add the missing key rather than deleting the present one.

- [ ] **Step 8: Write the failing component test**

Create `frontend/src/features/members/ContactFields.test.tsx`. Read `frontend/src/features/members/MembersPage.test.tsx` first and reuse its render wrapper (i18n provider setup) rather than building a second one.

```tsx
import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ContactFields } from './ContactFields'
import { EMPTY_CONTACT_DETAILS, type ContactDetails } from './contactDetails'
import type { Country } from '../countries/types'

const countries: Country[] = [
  { id: 1, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' },
  { id: 2, code: 'EG', nameAr: 'مصر', nameEn: 'Egypt', dialCode: '+20' },
]

const renderFields = (value: ContactDetails = EMPTY_CONTACT_DETAILS) => {
  const onChange = vi.fn()
  render(
    <ContactFields
      idPrefix="member"
      value={value}
      countries={countries}
      onChange={onChange}
      labelStyle={{}}
      controlStyle={{}}
    />,
  )
  return onChange
}

describe('ContactFields', () => {
  it('composes the dial code and the local number into one E.164 value', async () => {
    const onChange = renderFields()

    await userEvent.selectOptions(screen.getByLabelText(/dialing code/i), '+970')

    // The select is controlled by the parent, which is a spy here, so the dial code does not
    // persist between events. Assert on the call the select itself produced.
    expect(onChange).toHaveBeenCalled()
  })

  it('flags a national ID that is not nine digits', async () => {
    renderFields({ ...EMPTY_CONTACT_DETAILS, nationalId: '12345' })

    expect(screen.getByText(/must be exactly 9 digits/i)).toBeInTheDocument()
  })

  it('does not complain about a nine digit national ID', () => {
    renderFields({ ...EMPTY_CONTACT_DETAILS, nationalId: '123456789' })

    expect(screen.queryByText(/must be exactly 9 digits/i)).not.toBeInTheDocument()
  })

  it('does not complain about an empty national ID', () => {
    renderFields()

    expect(screen.queryByText(/must be exactly 9 digits/i)).not.toBeInTheDocument()
  })

  it('copies the mobile number to WhatsApp when "same as mobile" is ticked', async () => {
    const onChange = renderFields({
      ...EMPTY_CONTACT_DETAILS,
      mobileNumber: '+970599123456',
    })

    await userEvent.click(screen.getByLabelText(/same as mobile/i))

    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ whatsAppNumber: '+970599123456' }),
    )
  })

  it('disables the WhatsApp fields while they mirror the mobile', () => {
    renderFields({
      ...EMPTY_CONTACT_DETAILS,
      mobileNumber: '+970599123456',
      whatsAppNumber: '+970599123456',
    })

    const [, whatsAppNumberInput] = screen.getAllByLabelText(/^number$/i)
    expect(whatsAppNumberInput).toBeDisabled()
  })
})
```

- [ ] **Step 9: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/features/members/ContactFields.test.tsx`

Expected: FAIL — cannot resolve `./ContactFields`.

- [ ] **Step 10: Write `PhoneInput`**

Create `frontend/src/features/members/PhoneInput.tsx`:

```tsx
import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { countryName, flagEmoji } from '../countries/flagEmoji'
import type { Country } from '../countries/types'
import { joinPhone, splitPhone } from './contactDetails'

interface PhoneInputProps {
  id: string
  label: string
  /** The stored E.164 value, or null when not recorded. */
  value: string | null
  countries: readonly Country[]
  disabled?: boolean
  onChange: (e164: string | null) => void
  labelStyle: CSSProperties
  controlStyle: CSSProperties
}

/**
 * Specification §5.2's picker: `[🇵🇸 +970 ▼] [599123456]`. The two controls are a presentation
 * detail — the value that leaves here is always one composed E.164 string, because §5.1 is
 * explicit that the dialing code is not stored separately.
 *
 * The split is recomputed from the value on every render rather than held as state: a parent
 * that replaces the value (loading a different member into the same form) must not leave a
 * stale dial code behind.
 */
export function PhoneInput({
  id,
  label,
  value,
  countries,
  disabled = false,
  onChange,
  labelStyle,
  controlStyle,
}: PhoneInputProps) {
  const { t, i18n } = useTranslation()
  const { dialCode, local } = splitPhone(value, countries)

  // Deduplicated: +1 is both US and CA, and two identical options in a select is a bug the user
  // can see. Sorted numerically so the list reads the same in both languages.
  const dialCodes = [...new Set(countries.map((country) => country.dialCode))].sort(
    (a, b) => Number(a.slice(1)) - Number(b.slice(1)),
  )

  const labelFor = (code: string): string => {
    const owners = countries.filter((country) => country.dialCode === code)
    const flags = owners.map((country) => flagEmoji(country.code)).join('')
    // One owner: show its name. Several: the flags alone, or the row becomes a paragraph.
    const name = owners.length === 1 ? ` ${countryName(owners[0], i18n.language)}` : ''
    return `${flags} ${code}${name}`
  }

  return (
    <div style={{ marginBottom: 'var(--space-4)' }}>
      <label htmlFor={`${id}-local`} style={labelStyle}>
        {label}
      </label>
      <div style={{ display: 'flex', gap: 8 }}>
        <select
          id={`${id}-dial`}
          aria-label={t('members.dialCode')}
          value={dialCode}
          disabled={disabled}
          onChange={(event) => onChange(joinPhone(event.target.value, local))}
          style={{ ...controlStyle, width: 'auto', flex: '0 0 auto', minWidth: 150 }}
        >
          <option value="">—</option>
          {dialCodes.map((code) => (
            <option key={code} value={code}>
              {labelFor(code)}
            </option>
          ))}
        </select>
        <input
          id={`${id}-local`}
          aria-label={t('members.localNumber')}
          value={local}
          disabled={disabled}
          inputMode="tel"
          autoComplete="tel-national"
          maxLength={15}
          onChange={(event) => onChange(joinPhone(dialCode, event.target.value))}
          style={{ ...controlStyle, flex: 1 }}
        />
      </div>
    </div>
  )
}
```

- [ ] **Step 11: Write `ContactFields`**

Create `frontend/src/features/members/ContactFields.tsx`:

```tsx
import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { countryName, flagEmoji } from '../countries/flagEmoji'
import type { Country } from '../countries/types'
import { isValidNationalId, type ContactDetails } from './contactDetails'
import { PhoneInput } from './PhoneInput'

interface ContactFieldsProps {
  idPrefix: string
  value: ContactDetails
  countries: readonly Country[]
  onChange: (next: ContactDetails) => void
  labelStyle: CSSProperties
  controlStyle: CSSProperties
}

/**
 * The contact half of the member form. Shaped like LifeDetailsFields — a controlled group that
 * owns no state of its own, so the form holds one value and there is one place a save reads
 * from.
 */
export function ContactFields({
  idPrefix,
  value,
  countries,
  onChange,
  labelStyle,
  controlStyle,
}: ContactFieldsProps) {
  const { t, i18n } = useTranslation()

  const nationalId = value.nationalId ?? ''
  // Only complain about what the user has actually typed. An empty field is "not recorded",
  // not an error, and flagging it on an untouched form is noise.
  const nationalIdInvalid = nationalId !== '' && !isValidNationalId(nationalId)

  const sameAsMobile =
    value.mobileNumber !== null && value.whatsAppNumber === value.mobileNumber

  const sorted = [...countries].sort((a, b) =>
    countryName(a, i18n.language).localeCompare(countryName(b, i18n.language), i18n.language),
  )

  return (
    <fieldset style={{ border: 'none', padding: 0, margin: '0 0 var(--space-4)' }}>
      <legend style={{ ...labelStyle, marginBottom: 'var(--space-3)' }}>
        {t('members.contactSection')}
      </legend>

      <div style={{ marginBottom: 'var(--space-4)' }}>
        <label htmlFor={`${idPrefix}-national-id`} style={labelStyle}>
          {t('members.nationalId')}
        </label>
        <input
          id={`${idPrefix}-national-id`}
          value={nationalId}
          inputMode="numeric"
          maxLength={9}
          aria-invalid={nationalIdInvalid}
          aria-describedby={`${idPrefix}-national-id-hint`}
          onChange={(event) =>
            onChange({
              ...value,
              nationalId: event.target.value === '' ? null : event.target.value,
            })
          }
          style={{
            ...controlStyle,
            borderColor: nationalIdInvalid ? 'var(--error)' : 'var(--border-strong)',
          }}
        />
        <p
          id={`${idPrefix}-national-id-hint`}
          style={{
            margin: '6px 0 0',
            fontSize: 12,
            color: nationalIdInvalid ? 'var(--error)' : 'var(--text-3)',
          }}
        >
          {nationalIdInvalid ? t('members.nationalIdInvalid') : t('members.nationalIdHint')}
        </p>
      </div>

      <div style={{ marginBottom: 'var(--space-4)' }}>
        <label htmlFor={`${idPrefix}-country`} style={labelStyle}>
          {t('members.country')}
        </label>
        <select
          id={`${idPrefix}-country`}
          value={value.countryId ?? ''}
          onChange={(event) =>
            onChange({
              ...value,
              countryId: event.target.value === '' ? null : Number(event.target.value),
            })
          }
          style={controlStyle}
        >
          <option value="">{t('members.noCountry')}</option>
          {sorted.map((country) => (
            <option key={country.id} value={country.id}>
              {flagEmoji(country.code)} {countryName(country, i18n.language)}
            </option>
          ))}
        </select>
      </div>

      <PhoneInput
        id={`${idPrefix}-mobile`}
        label={t('members.mobileNumber')}
        value={value.mobileNumber}
        countries={countries}
        onChange={(mobileNumber) =>
          onChange({
            ...value,
            mobileNumber,
            // Keep a mirrored WhatsApp number in step: the checkbox promised they are the same,
            // and letting it fall behind would save a number the user never typed.
            whatsAppNumber: sameAsMobile ? mobileNumber : value.whatsAppNumber,
          })
        }
        labelStyle={labelStyle}
        controlStyle={controlStyle}
      />

      <div style={{ marginBottom: 'var(--space-3)' }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
          <input
            type="checkbox"
            checked={sameAsMobile}
            onChange={(event) =>
              onChange({
                ...value,
                whatsAppNumber: event.target.checked ? value.mobileNumber : null,
              })
            }
          />
          {t('members.sameAsMobile')}
        </label>
      </div>

      <PhoneInput
        id={`${idPrefix}-whatsapp`}
        label={t('members.whatsAppNumber')}
        value={value.whatsAppNumber}
        countries={countries}
        disabled={sameAsMobile}
        onChange={(whatsAppNumber) => onChange({ ...value, whatsAppNumber })}
        labelStyle={labelStyle}
        controlStyle={controlStyle}
      />
    </fieldset>
  )
}
```

- [ ] **Step 12: Run the component test to verify it passes**

Run: `cd frontend && npx vitest run src/features/members/ContactFields.test.tsx`

Expected: PASS — 6 tests.

- [ ] **Step 13: Wire the types, the API client, and the mutations**

`frontend/src/features/members/types.ts` — extend the `FamilyMember` interface:

```typescript
  /** Exactly 9 digits, or null when not recorded. Text, so a leading zero survives. */
  nationalId: string | null
  /** Normalized E.164, dialing code included. Null when not recorded. */
  mobileNumber: string | null
  /** Normalized E.164. Independent of `mobileNumber` — they are often different numbers. */
  whatsAppNumber: string | null
  countryId: number | null
  /** ISO alpha-2 for `countryId`, so a row can render a flag without loading the country list. */
  countryCode: string | null
```

`frontend/src/features/members/membersApi.ts` — add the import and spread the contact details into both writes:

```typescript
import type { ContactDetails } from './contactDetails'
```

```typescript
  create: (
    name: string,
    parentId: string | null,
    life: LifeDetails,
    contact: ContactDetails,
  ): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(MEMBERS, {
      method: 'POST',
      body: JSON.stringify({ name, parentId, ...life, ...contact }),
    }),
```

```typescript
  update: (
    id: string,
    name: string,
    version: number,
    life: LifeDetails,
    contact: ContactDetails,
  ): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(`${MEMBERS}/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name, version, ...life, ...contact }),
    }),
```

In `update`'s doc comment, after the sentence about life details being replace-semantics, add: "The contact details are replace-semantics for the same reason — omitting a cleared phone number would leave the old one in place."

`frontend/src/features/members/useMembers.ts` — add the import and `contact` to both mutations:

```typescript
import type { ContactDetails } from './contactDetails'
```

```typescript
export const useCreateMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: ({
      name,
      parentId,
      life,
      contact,
    }: {
      name: string
      parentId: string | null
      life: LifeDetails
      contact: ContactDetails
    }) => membersApi.create(name, parentId, life, contact),
    onSuccess: invalidate,
  })
}

export const useUpdateMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: ({
      id,
      name,
      version,
      life,
      contact,
    }: {
      id: string
      name: string
      version: number
      life: LifeDetails
      contact: ContactDetails
    }) => membersApi.update(id, name, version, life, contact),
    onSuccess: invalidate,
  })
}
```

- [ ] **Step 14: Wire the form and the page**

`frontend/src/features/members/MemberForm.tsx` — add the imports:

```typescript
import { useCountriesQuery } from '../countries/useCountries'
import { ContactFields } from './ContactFields'
import { EMPTY_CONTACT_DETAILS, contactDetailsOf, type ContactDetails } from './contactDetails'
```

Change the `onSubmit` prop type:

```typescript
  onSubmit: (
    name: string,
    parentId: string | null,
    life: LifeDetails,
    contact: ContactDetails,
  ) => void
```

Add the state and the query beside the existing `useState` calls:

```typescript
  const { data: countries } = useCountriesQuery()
  const [contact, setContact] = useState<ContactDetails>(
    member === undefined ? EMPTY_CONTACT_DETAILS : contactDetailsOf(member),
  )
```

Change the submit handler:

```typescript
  const handleSubmit = (event: FormEvent) => {
    event.preventDefault()
    onSubmit(name, parentId === '' ? null : parentId, life, contact)
  }
```

Render `ContactFields` immediately after `LifeDetailsFields`:

```tsx
      <ContactFields
        idPrefix="member"
        value={contact}
        countries={countries ?? []}
        onChange={setContact}
        labelStyle={labelStyle}
        controlStyle={controlStyle}
      />
```

`frontend/src/features/members/MembersPage.tsx` — add the import:

```typescript
import type { ContactDetails } from './contactDetails'
```

and thread the contact through both handlers:

```typescript
  const handleCreate = (
    name: string,
    parentId: string | null,
    life: LifeDetails,
    contact: ContactDetails,
  ) => {
    setErrorCode(null)
    createMember.mutate(
      { name, parentId, life, contact },
      { onSuccess: close, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const handleUpdate = (
    target: FamilyMember,
    name: string,
    life: LifeDetails,
    contact: ContactDetails,
  ) => {
    setErrorCode(null)
    updateMember.mutate(
      { id: target.id, name, version: target.version, life, contact },
      {
        onSuccess: close,
        onError: (error) => {
          const code = codeOf(error)
          setErrorCode(code)
          // A CONCURRENCY_CONFLICT means the form is holding a stale version — retrying
          // against it just reproduces the same 409, so refetch and close.
          if (code === 'CONCURRENCY_CONFLICT') {
            void queryClient.invalidateQueries({ queryKey: memberKeys.all })
            close()
          }
        },
      },
    )
  }
```

Update the `MemberForm` usages in this file so their `onSubmit` closures accept the fourth argument.

- [ ] **Step 15: Run the whole frontend suite**

Run: `cd frontend && npm test`

Expected: PASS. `MembersPage.test.tsx` renders `MemberForm`, which now calls `useCountriesQuery` — if a test fails on an unmocked request, add `/api/v1/countries` returning `[]` to that file's existing fetch stub rather than changing the component.

- [ ] **Step 16: Lint and type-check**

```bash
cd frontend && npm run lint && npm run build
```

Expected: both clean. `tsc -b` runs as part of `build` and is the real check that every call site was updated.

- [ ] **Step 17: Commit**

```bash
git add frontend/src
git commit -m "feat: capture member contact details in the form"
```

---

### Task 9: Verify the whole plan end to end

**Files:** none created or modified unless a check fails.

- [ ] **Step 1: Run the full backend suite**

```bash
dotnet test
```

Expected: PASS, all four test projects. Docker must be running.

- [ ] **Step 2: Run the full frontend suite, lint, and build**

```bash
cd frontend && npm test && npm run lint && npm run build
```

Expected: all clean.

- [ ] **Step 3: Exercise it by hand**

```bash
docker compose up -d
```

Sign in, open `/members`, and confirm against specification §27's acceptance criteria:

- Add a member with national ID `123456789`, country Palestine, mobile `+970 599123456`. It saves.
- Edit that member and enter `12345` as the national ID. The field turns red and shows "A national ID must be exactly 9 digits."
- Add a second member with the same national ID `123456789`. The save fails with "Another member already has this national ID."
- Select Egypt while keeping the `+970` number. The save fails with the dial-code message.
- Tick "Same as mobile number". The WhatsApp fields mirror the mobile and are disabled.
- Clear the mobile number and save. It is stored as empty, not left at the old value.
- Switch to Arabic. Country names render in Arabic, the layout stays RTL, and every new label is translated.

Any failure here is a bug in a previous task, not a step to skip. Fix it there and re-run that task's tests.

- [ ] **Step 4: Confirm the temporary code is gone**

```bash
grep -n "member.NationalId, member.MobileNumber" src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs
```

Expected: **no match.** A match means Task 6 Step 4 did not replace the placeholder from Task 4 Step 5, and every edit silently preserves the old contact details instead of applying the request's — a bug no test above would catch, because the placeholder returns plausible data.

- [ ] **Step 5: Confirm the branch is clean**

```bash
git status --porcelain
git log --oneline main..HEAD
```

Expected: a clean working tree and nine feature commits on `member-data-filters-export`.

---

## What this plan does not do

Deliberately deferred to Plans 2–4, per spec §9. Do not add them here:

- Branch and generation derivation, the recursive CTE, `FamilyMemberQuery` (Plan 2).
- Filter parameters on any endpoint; the `branches` and `generations` endpoints (Plan 2).
- The shared filter module, `FilterBar`, the responsive filter sheet, and the two root-relative generation labels (Plan 3).
- ClosedXML, the Excel exporter, `export.xlsx` (Plan 4).
- Country and Branch columns on the members table — these arrive with Plan 3's filter work, since Branch is not derivable until Plan 2.
