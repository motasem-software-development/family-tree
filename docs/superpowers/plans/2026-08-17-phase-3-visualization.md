# Phase 3 — Visualization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make search truthful and scalable — move it to the server behind a trigram index, give every hit its ancestor path, report the real match count — and window the outline so only visible rows are in the DOM.

**Architecture:** A `GET /api/v1/family-members/search` endpoint runs one recursive CTE that both matches names and walks `parent_id` upward, so each hit arrives with its full root-first ancestor chain and a generation derived from that chain's length. A `pg_trgm` GIN index accelerates the `ILIKE '%…%'` match — the same matching semantics the client used, moved rather than changed. On the frontend, `searchNodes` is deleted in favour of a debounced query hook, and the outline renders a windowed slice of rows computed by a pure `windowRange` function, with the zoom bug fixed by swapping `transform: scale()` for CSS `zoom` so the scroll container's height tracks the scaled content.

**Tech Stack:** .NET 10, EF Core + Npgsql, PostgreSQL with `pg_trgm`, React 19, TanStack Query 5, vitest + Testing Library, xUnit + FluentAssertions + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md` — §5.4 (search, ancestor path, virtualization), §3.4 (indexes), §4.4 (uniform 404), §6 (testing). Note §5's body was superseded on 2026-08-17 by the `design/` handoff bundle; §5.4's search and virtualization requirements survive that supersession, its SVG/orientation requirements do not. Task 10 corrects the spec text.

**Prior obligations this plan discharges:**
- `docs/superpowers/plans/2026-08-16-phase-2-family-tree.md` deviation 1: "Phase 3's planner must add `CREATE EXTENSION IF NOT EXISTS pg_trgm` and the GIN index alongside the search endpoint." → Task 1.
- `docs/superpowers/plans/2026-08-17-phase-2-5-data-import.md` "Input to Phase 3", findings 1–3 → Tasks 7–9 (virtualization), 2–6 (ancestor path and honest counts).

## Global Constraints

- **Tenant isolation is not optional.** Raw SQL bypasses the EF global query filter entirely. Every statement in Task 2 binds `tenant_id` explicitly, and an empty tenant id must return nothing rather than everything (fails closed, matching `QueryFilterInvariantTests`).
- **Uniform 404s.** Design spec §4.4: responses must not reveal whether an id exists in another tenant. Search returns an empty page, never an error, for a query that matches nothing.
- **Stable error codes.** Errors are RFC 7807 Problem Details carrying a `code`; clients translate from the code. Any new code must be added to the README table.
- **Arabic-first, RTL.** All user-facing strings go through i18next with both `ar.json` and `en.json` populated. Arabic is the default language.
- **Immutability.** New objects, never in-place mutation (see `~/.claude/rules/common/coding-style.md`).
- **No `any`** in TypeScript; explicit types on exported functions.
- **Migrations are applied deliberately**, never on application startup.
- **Row height is 44px** and the indent pitch is 28px — both are load-bearing constants in `TreeCanvas.tsx` that the design handoff fixed. Do not change them.
- **Commit after every task**, conventional-commit format, no attribution trailer.

---

## File Structure

**Backend — create:**
- `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberAncestor.cs` — one link in an ancestor chain.
- `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberSearchHit.cs` — one search result with its chain.
- `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberSearchResponse.cs` — the page envelope carrying the true total.
- `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberSearchQuery.cs` — the recursive CTE and its row reader, isolated from `FamilyMemberService`'s EF-based CRUD so the one raw-SQL surface in the codebase is a single reviewable file.
- `src/FamilyTree.Infrastructure/Persistence/Migrations/<stamp>_AddNameTrigramIndex.cs` — extension + GIN index.
- `tests/FamilyTree.Api.IntegrationTests/Persistence/TrigramIndexTests.cs`
- `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberSearchTests.cs`

**Backend — modify:**
- `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs` — add `SearchAsync`.
- `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs` — delegate to `FamilyMemberSearchQuery`.
- `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs` — map `/search`.
- `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs` — endpoint-level cases.

**Frontend — create:**
- `frontend/src/features/tree/useDebouncedValue.ts` + `.test.ts` — generic debounce.
- `frontend/src/features/tree/useSearch.ts` — the search query hook.
- `frontend/src/features/tree/windowRange.ts` + `.test.ts` — pure windowing math.
- `frontend/src/features/tree/useVisibleRange.ts` — measures the DOM, converts to layout units, calls `windowRange`.

**Frontend — modify:**
- `frontend/src/features/members/types.ts` — search DTOs.
- `frontend/src/features/members/membersApi.ts` — `search()`.
- `frontend/src/features/members/useMembers.ts` — `memberKeys.search`.
- `frontend/src/app/AppShell.tsx` — `SearchResult` shape, honest count, searching state.
- `frontend/src/features/tree/TreePage.tsx` — server search, reveal plumbing.
- `frontend/src/features/tree/TreeCanvas.tsx` — windowing, zoom fix, scroll-to-reveal.
- `frontend/src/features/tree/flattenTree.ts` — delete `searchNodes`.
- `frontend/src/i18n/locales/{ar,en}.json` — new keys.

**Docs — modify:** `README.md`, the design spec, this plan (Task 11 findings).

---

## Task 1: The `pg_trgm` extension and GIN index

Discharges Phase 2 deviation 1 verbatim. This is the highest-privilege statement in the codebase — `CREATE EXTENSION` needs rights an application role often lacks — so it lands on its own, before anything queries it, with its own test and its own README note.

**Files:**
- Create: `src/FamilyTree.Infrastructure/Persistence/Migrations/<timestamp>_AddNameTrigramIndex.cs` (generated)
- Create: `tests/FamilyTree.Api.IntegrationTests/Persistence/TrigramIndexTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: nothing.
- Produces: a database index named `ix_family_members_name_trgm` over `family_members (name gin_trgm_ops)`, and the `pg_trgm` extension in the default schema. Task 2's query depends on both existing but does not name them.

- [ ] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Persistence/TrigramIndexTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Persistence;

/// <summary>
/// Design spec §3.4 and Phase 2 deviation 1. The index exists solely to serve the search
/// endpoint; asserting on it here means a migration that silently fails to create the
/// extension is caught by the test suite rather than by a slow query in production.
/// </summary>
public sealed class TrigramIndexTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var context = ContextFor(Guid.Empty);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await context.Database.OpenConnectionAsync();
        try
        {
            return (T)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task The_pg_trgm_extension_is_installed()
    {
        var count = await ScalarAsync<long>(
            "SELECT count(*) FROM pg_extension WHERE extname = 'pg_trgm';");

        count.Should().Be(1);
    }

    [Fact]
    public async Task A_gin_trigram_index_covers_the_member_name()
    {
        var definition = await ScalarAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_family_members_name_trgm';");

        definition.Should().Contain("gin").And.Contain("gin_trgm_ops").And.Contain("name");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd d:/Work/Motasem/FamilyTree
dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~TrigramIndexTests"
```

Expected: FAIL. `The_pg_trgm_extension_is_installed` reports 0, and `A_gin_trigram_index_covers_the_member_name` throws on the null scalar because no such index exists.

Docker must be running — `PostgresFixture` starts a Testcontainers PostgreSQL instance.

- [ ] **Step 3: Generate the migration**

```bash
cd d:/Work/Motasem/FamilyTree
dotnet ef migrations add AddNameTrigramIndex \
  --project src/FamilyTree.Infrastructure \
  --startup-project src/FamilyTree.Api \
  --output-dir Persistence/Migrations
```

This produces an empty `Up`/`Down` pair, because the index is raw DDL that the EF model does not describe. That is expected.

- [ ] **Step 4: Fill in the migration body**

Replace the generated `Up` and `Down` methods in the new `<timestamp>_AddNameTrigramIndex.cs`:

```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Design spec §3.4, deferred here from Phase 2 (deviation 1) because the index
            // exists only to serve the search endpoint, which ships in this phase.
            //
            // CREATE EXTENSION requires rights beyond those of a plain application role. The
            // Testcontainers image runs as superuser so this is invisible in tests; a deployed
            // database may need a DBA to install pg_trgm out of band. IF NOT EXISTS makes the
            // pre-installed case a no-op rather than a failed deploy. See README.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // GIN over trigrams is what makes an unanchored ILIKE '%…%' indexable at all — a
            // btree index cannot serve a leading wildcard. The existing btree on
            // (family_tree_id, name) stays: it serves ordering and exact lookups, which
            // trigrams do not.
            migrationBuilder.Sql(@"
                CREATE INDEX ix_family_members_name_trgm
                    ON family_members
                    USING gin (name gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_family_members_name_trgm;");

            // The extension is deliberately NOT dropped. Another database object could depend
            // on it, and dropping a shared extension during a rollback is a wider blast radius
            // than the index this migration owns.
        }
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~TrigramIndexTests"
```

Expected: PASS, 2 tests.

- [ ] **Step 6: Document the privilege requirement**

In `README.md`, immediately after the paragraph ending "…start the rest of the stack:" and its code block, add:

```markdown
The `AddNameTrigramIndex` migration runs `CREATE EXTENSION IF NOT EXISTS pg_trgm`, which
requires privileges a plain application role usually lacks. Local Docker and the Testcontainers
test image both run as superuser, so this is invisible in development. On a managed or
least-privilege database, have a superuser run `CREATE EXTENSION pg_trgm;` once before applying
migrations — the `IF NOT EXISTS` guard then makes the migration a no-op.
```

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Infrastructure/Persistence/Migrations tests/FamilyTree.Api.IntegrationTests/Persistence/TrigramIndexTests.cs README.md
git commit -m "feat: add pg_trgm extension and name trigram index"
```

---

## Task 2: The search query — contracts and recursive CTE

The heart of the phase. One recursive CTE both matches and walks upward, so an ancestor path costs no extra round trip and generation falls out of the walk's depth rather than being stored.

**Files:**
- Create: `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberAncestor.cs`
- Create: `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberSearchHit.cs`
- Create: `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberSearchResponse.cs`
- Create: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberSearchQuery.cs`
- Modify: `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberSearchTests.cs`

**Interfaces:**
- Consumes: Task 1's index (implicitly — the query is correct without it, just slower).
- Produces:
  - `record FamilyMemberAncestor(Guid Id, string Name)`
  - `record FamilyMemberSearchHit(Guid Id, string Name, int Generation, IReadOnlyList<FamilyMemberAncestor> Ancestors)`
  - `record FamilyMemberSearchResponse(int Total, IReadOnlyList<FamilyMemberSearchHit> Items)`
  - `IFamilyMemberService.SearchAsync(string query, int limit, int offset, CancellationToken ct = default)`
  - `FamilyMemberSearchQuery.MaxLimit` = 50, `FamilyMemberSearchQuery.DefaultLimit` = 20

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberSearchTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~FamilyMemberSearchTests"
```

Expected: FAIL to **compile** — `SearchAsync`, `FamilyMemberSearchQuery`, and the three contract records do not exist. A compile failure is the correct red for a task that introduces new types.

- [ ] **Step 3: Add the contract records**

Create `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberAncestor.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>One link in a search hit's chain back to the root.</summary>
public sealed record FamilyMemberAncestor(Guid Id, string Name);
```

Create `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberSearchHit.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// A matched member and the path that disambiguates them. Design spec §5.4 calls the ancestor
/// path "required rather than decorative": the imported tree has 39 members named محمد, and
/// generation alone cannot tell a user which one they are looking at.
/// </summary>
/// <param name="Ancestors">Root first, excluding the hit itself. Empty for a root member.</param>
/// <param name="Generation">1-based; equals Ancestors.Count + 1.</param>
public sealed record FamilyMemberSearchHit(
    Guid Id,
    string Name,
    int Generation,
    IReadOnlyList<FamilyMemberAncestor> Ancestors);
```

Create `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberSearchResponse.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// A page of search hits plus the true match count.
/// </summary>
/// <param name="Total">
/// Every match, independent of <c>Items.Count</c>. This field exists because the client
/// previously reported the size of its own truncated list — "8 نتائج" when 39 members matched.
/// </param>
public sealed record FamilyMemberSearchResponse(int Total, IReadOnlyList<FamilyMemberSearchHit> Items);
```

- [ ] **Step 4: Declare the service method**

In `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`, add after `ListAsync`:

```csharp
    /// <summary>
    /// Case-insensitive substring match on name, ordered by (name, id).
    /// Returns an empty page — never an error — for a blank query or one that matches nothing,
    /// so a caller cannot distinguish "no such name here" from "no such name anywhere"
    /// (design spec §4.4).
    /// </summary>
    /// <param name="limit">Clamped to 1..50.</param>
    /// <param name="offset">Negative values are treated as 0.</param>
    Task<FamilyMemberSearchResponse> SearchAsync(
        string query, int limit, int offset, CancellationToken ct = default);
```

- [ ] **Step 5: Write the query**

Create `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberSearchQuery.cs`:

```csharp
using System.Data.Common;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace FamilyTree.Infrastructure.FamilyMembers;

/// <summary>
/// The single raw-SQL surface in the codebase, isolated here so the tenant-safety argument
/// lives in one reviewable file.
///
/// Raw SQL rather than LINQ because EF Core cannot express WITH RECURSIVE, and the ancestor
/// path is the whole point of the endpoint (design spec §5.4). The cost of that choice is
/// that the EF global query filter — layer 1 of the three-layer tenant isolation in design
/// spec §3.2 — does not apply. Every table reference below therefore carries an explicit
/// tenant_id predicate, including inside the recursive term: without it, a walk that starts
/// on a permitted row could climb into another tenant's ancestry.
/// </summary>
internal static class FamilyMemberSearchQuery
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    /// <summary>
    /// LIKE metacharacters survive parameterisation — a parameter binds the pattern, not its
    /// meaning — so a user typing "%" would otherwise match every member. Backslash first, or
    /// it would escape the escapes added after it.
    /// </summary>
    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    private const string CountSql = """
        SELECT count(*)
        FROM family_members
        WHERE tenant_id = @tenant_id
          AND name ILIKE @pattern ESCAPE '\';
        """;

    /// <summary>
    /// `page` selects the requested slice and stamps each hit with its position, so the
    /// ordering survives the join that follows. `chain` starts at each hit (up = 0) and walks
    /// parent_id upward one generation per iteration; the walk terminates naturally at a root,
    /// whose parent_id is null and joins to nothing.
    ///
    /// The final ORDER BY replays the page order, and `up DESC` puts each chain root-first —
    /// so the reader can consume rows in a single forward pass with no sorting in C#.
    /// </summary>
    private const string PageSql = """
        WITH RECURSIVE page AS (
            SELECT id, row_number() OVER (ORDER BY name, id) AS ord
            FROM family_members
            WHERE tenant_id = @tenant_id
              AND name ILIKE @pattern ESCAPE '\'
            ORDER BY name, id
            LIMIT @limit OFFSET @offset
        ),
        chain AS (
            SELECT p.ord, p.id AS hit_id, m.id AS node_id, m.name AS node_name,
                   m.parent_id, 0 AS up
            FROM page p
            JOIN family_members m ON m.id = p.id AND m.tenant_id = @tenant_id
            UNION ALL
            SELECT c.ord, c.hit_id, m.id, m.name, m.parent_id, c.up + 1
            FROM chain c
            JOIN family_members m ON m.id = c.parent_id AND m.tenant_id = @tenant_id
        )
        SELECT ord, hit_id, node_id, node_name, up
        FROM chain
        ORDER BY ord, up DESC;
        """;

    public static async Task<FamilyMemberSearchResponse> ExecuteAsync(
        ApplicationDbContext context,
        Guid tenantId,
        string query,
        int limit,
        int offset,
        CancellationToken ct)
    {
        var term = query.Trim();

        // An empty tenant id is an unauthenticated caller; an empty term would otherwise
        // become '%%' and match the entire tree. Both fail closed, before any SQL runs.
        if (tenantId == Guid.Empty || term.Length == 0)
            return new FamilyMemberSearchResponse(0, []);

        var pattern = $"%{EscapeLikePattern(term)}%";
        var safeLimit = Math.Clamp(limit, 1, MaxLimit);
        var safeOffset = Math.Max(offset, 0);

        await context.Database.OpenConnectionAsync(ct);
        try
        {
            var total = await CountAsync(context, tenantId, pattern, ct);
            if (total == 0) return new FamilyMemberSearchResponse(0, []);

            var items = await ReadPageAsync(context, tenantId, pattern, safeLimit, safeOffset, ct);
            return new FamilyMemberSearchResponse(total, items);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<int> CountAsync(
        ApplicationDbContext context, Guid tenantId, string pattern, CancellationToken ct)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = CountSql;
        AddParameter(command, "tenant_id", NpgsqlDbType.Uuid, tenantId);
        AddParameter(command, "pattern", NpgsqlDbType.Text, pattern);

        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<IReadOnlyList<FamilyMemberSearchHit>> ReadPageAsync(
        ApplicationDbContext context,
        Guid tenantId,
        string pattern,
        int limit,
        int offset,
        CancellationToken ct)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = PageSql;
        AddParameter(command, "tenant_id", NpgsqlDbType.Uuid, tenantId);
        AddParameter(command, "pattern", NpgsqlDbType.Text, pattern);
        AddParameter(command, "limit", NpgsqlDbType.Integer, limit);
        AddParameter(command, "offset", NpgsqlDbType.Integer, offset);

        await using var reader = await command.ExecuteReaderAsync(ct);

        var hits = new List<FamilyMemberSearchHit>();
        var ancestors = new List<FamilyMemberAncestor>();
        Guid? currentHitId = null;
        var currentName = string.Empty;

        while (await reader.ReadAsync(ct))
        {
            var hitId = reader.GetGuid(1);
            var nodeId = reader.GetGuid(2);
            var nodeName = reader.GetString(3);
            var up = reader.GetInt32(4);

            if (currentHitId is { } previous && previous != hitId)
            {
                hits.Add(Build(previous, currentName, ancestors));
                ancestors = [];
            }

            currentHitId = hitId;

            // Rows arrive root-first (up DESC), so up = 0 is the hit itself and closes the
            // chain; everything before it is an ancestor, already in the right order.
            if (up == 0) currentName = nodeName;
            else ancestors.Add(new FamilyMemberAncestor(nodeId, nodeName));
        }

        if (currentHitId is { } last) hits.Add(Build(last, currentName, ancestors));

        return hits;
    }

    private static FamilyMemberSearchHit Build(
        Guid id, string name, IReadOnlyList<FamilyMemberAncestor> ancestors) =>
        // Generation is derived, not stored: the walk's depth IS the generation, so this
        // cannot drift from FamilyTreeAssembler's independently computed value.
        new(id, name, ancestors.Count + 1, ancestors);

    private static void AddParameter(DbCommand command, string name, NpgsqlDbType type, object value)
    {
        var parameter = new NpgsqlParameter(name, type) { Value = value };
        command.Parameters.Add(parameter);
    }
}
```

- [ ] **Step 6: Delegate from the service**

In `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`, add after `ListAsync`:

```csharp
    public Task<FamilyMemberSearchResponse> SearchAsync(
        string query, int limit, int offset, CancellationToken ct = default) =>
        // The only read here that does NOT go through the tenant query filter, because it is
        // raw SQL. FamilyMemberSearchQuery re-establishes the guarantee with an explicit
        // predicate on every table reference — see the class comment there.
        FamilyMemberSearchQuery.ExecuteAsync(context, tenant.TenantId, query, limit, offset, ct);
```

- [ ] **Step 7: Make the constants visible to the test project**

`FamilyMemberSearchQuery` is `internal`, and `An_oversized_limit_is_clamped_rather_than_rejected` asserts on `MaxLimit`. Add to `src/FamilyTree.Infrastructure/FamilyTree.Infrastructure.csproj`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="FamilyTree.Api.IntegrationTests" />
  </ItemGroup>
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~FamilyMemberSearchTests"
```

Expected: PASS, 10 tests.

- [ ] **Step 9: Run the whole backend suite**

```bash
dotnet test
```

Expected: PASS. `IFamilyMemberService` gained a member, so any other implementation or mock would fail to compile — this catches that.

- [ ] **Step 10: Commit**

```bash
git add src/FamilyTree.Contracts src/FamilyTree.Application src/FamilyTree.Infrastructure tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberSearchTests.cs
git commit -m "feat: search members by name with server-computed ancestor paths"
```

---

## Task 3: The search endpoint

**Files:**
- Modify: `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs:13-18`
- Modify: `README.md`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs`

**Interfaces:**
- Consumes: `IFamilyMemberService.SearchAsync`, `FamilyMemberSearchResponse` from Task 2.
- Produces: `GET /api/v1/family-members/search?q=&limit=&offset=`, requiring `Permissions.Member.View`, returning `200` with a `FamilyMemberSearchResponse`.

- [ ] **Step 1: Write the failing tests**

Append to the `FamilyMemberEndpointsTests` class in `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs`, before the closing brace:

```csharp
    [Fact]
    public async Task Search_requires_authentication()
    {
        var response = await _client.GetAsync("/api/v1/family-members/search?q=محمد");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_returns_matches_with_their_ancestor_path()
    {
        await AuthenticateAsync();
        var root = await CreateAsync("داوود");
        var middle = await CreateAsync("سلمان", root.Id);
        var leaf = await CreateAsync("خالد", middle.Id);

        var page = await _client.GetFromJsonAsync<FamilyMemberSearchResponse>(
            "/api/v1/family-members/search?q=خالد");

        page!.Total.Should().Be(1);
        var hit = page.Items.Should().ContainSingle().Subject;
        hit.Id.Should().Be(leaf.Id);
        hit.Ancestors.Select(a => a.Name).Should().Equal("داوود", "سلمان");
        hit.Generation.Should().Be(3);
    }

    [Fact]
    public async Task Search_reports_the_true_total_alongside_a_smaller_page()
    {
        await AuthenticateAsync();
        var root = await CreateAsync("داوود");
        for (var i = 0; i < 4; i++) await CreateAsync("محمد", root.Id);

        var page = await _client.GetFromJsonAsync<FamilyMemberSearchResponse>(
            "/api/v1/family-members/search?q=محمد&limit=2");

        page!.Total.Should().Be(4);
        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_without_a_query_returns_an_empty_page_rather_than_an_error()
    {
        await AuthenticateAsync();
        await CreateAsync("داوود");

        var response = await _client.GetAsync("/api/v1/family-members/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<FamilyMemberSearchResponse>())!;
        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_returns_an_empty_page_for_a_name_that_does_not_exist()
    {
        await AuthenticateAsync();
        await CreateAsync("داوود");

        var response = await _client.GetAsync("/api/v1/family-members/search?q=لايوجد");

        // 200 with nothing in it, not 404: a "not found" here would let a caller probe for
        // which names exist (design spec §4.4).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<FamilyMemberSearchResponse>())!.Total.Should().Be(0);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~FamilyMemberEndpointsTests"
```

Expected: FAIL — the route does not exist, so every one of the five returns 404 (including `Search_requires_authentication`, which expects 401).

- [ ] **Step 3: Map the endpoint**

In `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs`, add immediately after the `var group = ...` line (13):

```csharp
        // Mirrors FamilyMemberSearchQuery.DefaultLimit, which is internal to Infrastructure and
        // must not be referenced from Api. The service clamps to 1..50 regardless of what
        // arrives here.
        const int defaultSearchLimit = 20;
```

Then insert between the list endpoint (ending line 17) and the by-id endpoint:

```csharp
        // Declared before "/{id:guid}" for readability only — the guid route constraint makes
        // the two unambiguous regardless of order.
        group.MapGet("/search", async (
            string? q,
            int? limit,
            int? offset,
            IFamilyMemberService members,
            CancellationToken ct) =>
        {
            // Paging bounds are clamped in the service rather than rejected: a search box
            // sending a stray limit should return sensible results, not a 400 the user cannot
            // act on. The clamp is documented in the README so it is contract, not accident.
            var page = await members.SearchAsync(q ?? string.Empty, limit ?? defaultSearchLimit, offset ?? 0, ct);
            return Results.Ok(page);
        })
            .RequirePermission(Permissions.Member.View);
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~FamilyMemberEndpointsTests"
```

Expected: PASS, all tests in the class.

- [ ] **Step 5: Document the endpoint**

In `README.md`, after the "## API error codes" table, add a new section:

```markdown
## Search

`GET /api/v1/family-members/search?q=<text>&limit=<1-50>&offset=<n>` requires `Member.View` and
returns `{ total, items: [{ id, name, generation, ancestors: [{ id, name }] }] }`.

`total` is every match, independent of the page size — a client must not report `items.length`
as the result count. `ancestors` is root-first and excludes the hit itself; it is what
distinguishes the 39 members named محمد from each other.

Matching is a case-insensitive substring (`ILIKE '%q%'`), accelerated by a trigram GIN index.
`%` and `_` in the query are matched literally. A blank query returns an empty page. `limit` is
clamped to 1..50 and a negative `offset` is treated as 0 — bad paging values are corrected
rather than rejected, so no new error code applies to this endpoint.
```

- [ ] **Step 6: Run the whole backend suite and commit**

```bash
dotnet test
git add src/FamilyTree.Api tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs README.md
git commit -m "feat: expose the member search endpoint"
```

---

## Task 4: Frontend search client

**Files:**
- Modify: `frontend/src/features/members/types.ts`
- Modify: `frontend/src/features/members/membersApi.ts:15-40`
- Modify: `frontend/src/features/members/useMembers.ts:5-8`
- Create: `frontend/src/features/tree/useDebouncedValue.ts`
- Create: `frontend/src/features/tree/useSearch.ts`
- Test: `frontend/src/features/tree/useDebouncedValue.test.ts`
- Test: `frontend/src/features/members/membersApi.test.ts` (existing file, append)

**Interfaces:**
- Consumes: the endpoint from Task 3.
- Produces:
  - `interface MemberAncestor { id: string; name: string }`
  - `interface MemberSearchHit { id: string; name: string; generation: number; ancestors: MemberAncestor[] }`
  - `interface MemberSearchPage { total: number; items: MemberSearchHit[] }`
  - `membersApi.search(query: string, limit: number): Promise<MemberSearchPage>`
  - `memberKeys.search(query: string, limit: number)`
  - `useDebouncedValue<T>(value: T, delayMs: number): T`
  - `useSearch(query: string): SearchState` where `SearchState = { page: MemberSearchPage; isSearching: boolean }`
  - `SEARCH_LIMIT = 8`, `MIN_QUERY_LENGTH = 2`

- [ ] **Step 1: Write the failing debounce test**

Create `frontend/src/features/tree/useDebouncedValue.test.ts`:

```ts
import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useDebouncedValue } from './useDebouncedValue'

describe('useDebouncedValue', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('returns the initial value immediately', () => {
    const { result } = renderHook(() => useDebouncedValue('محمد', 250))

    expect(result.current).toBe('محمد')
  })

  it('withholds a new value until the delay has elapsed', () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 250), {
      initialProps: { value: 'م' },
    })

    rerender({ value: 'محمد' })
    expect(result.current).toBe('م')

    act(() => vi.advanceTimersByTime(250))
    expect(result.current).toBe('محمد')
  })

  it('restarts the delay on every keystroke, so only the last value lands', () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 250), {
      initialProps: { value: 'م' },
    })

    rerender({ value: 'مح' })
    act(() => vi.advanceTimersByTime(200))
    rerender({ value: 'محم' })
    act(() => vi.advanceTimersByTime(200))

    // 400ms have passed but never 250 consecutive on one value.
    expect(result.current).toBe('م')

    act(() => vi.advanceTimersByTime(50))
    expect(result.current).toBe('محم')
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

```bash
cd frontend && npx vitest run src/features/tree/useDebouncedValue.test.ts
```

Expected: FAIL — cannot resolve `./useDebouncedValue`.

- [ ] **Step 3: Write the debounce hook**

Create `frontend/src/features/tree/useDebouncedValue.ts`:

```ts
import { useEffect, useState } from 'react'

/**
 * Holds a value back until it has stopped changing for `delayMs`.
 *
 * Search moved to the server in Phase 3, so every keystroke would otherwise be a request and a
 * recursive CTE. The timer is cleared on each change, so a fast typist issues one query rather
 * than one per character.
 */
export const useDebouncedValue = <T>(value: T, delayMs: number): T => {
  const [settled, setSettled] = useState<T>(value)

  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return settled
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
cd frontend && npx vitest run src/features/tree/useDebouncedValue.test.ts
```

Expected: PASS, 3 tests.

- [ ] **Step 5: Add the search DTOs**

In `frontend/src/features/members/types.ts`, append:

```ts
export interface MemberAncestor {
  id: string
  name: string
}

export interface MemberSearchHit {
  id: string
  name: string
  generation: number
  /** Root first, excluding the hit itself. Empty for a first-generation member. */
  ancestors: MemberAncestor[]
}

export interface MemberSearchPage {
  /** Every match on the server, not the length of `items`. */
  total: number
  items: MemberSearchHit[]
}
```

- [ ] **Step 6: Write the failing api-client test**

Read the top of `frontend/src/features/members/membersApi.test.ts` first and reuse whatever it already names its fetch mock and JSON helper — `fetchMock` and `jsonResponse` below stand in for that file's existing conventions. If it has no JSON helper, build the response inline as `{ ok: true, status: 200, json: async () => page } as unknown as Response`.

Append inside the existing top-level `describe`:

```ts
  it('sends the query and limit as search parameters', async () => {
    const page = { total: 39, items: [] }
    fetchMock.mockResolvedValue(jsonResponse(page))

    const result = await membersApi.search('محمد', 8)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/family-members/search?q=%D9%85%D8%AD%D9%85%D8%AF&limit=8',
      expect.anything(),
    )
    expect(result).toEqual(page)
  })
```

- [ ] **Step 7: Run it to verify it fails**

```bash
cd frontend && npx vitest run src/features/members/membersApi.test.ts
```

Expected: FAIL — `membersApi.search is not a function`.

- [ ] **Step 8: Add the api method**

In `frontend/src/features/members/membersApi.ts`, add `MemberSearchPage` to the type import and add this method to the `membersApi` object after `remove`:

```ts
  /**
   * `URLSearchParams` rather than string concatenation: Arabic queries need percent-encoding,
   * and a name containing `&` would otherwise split into two parameters.
   */
  search: (query: string, limit: number): Promise<MemberSearchPage> => {
    const params = new URLSearchParams({ q: query, limit: String(limit) })
    return apiFetch<MemberSearchPage>(`${MEMBERS}/search?${params}`)
  },
```

- [ ] **Step 9: Run it to verify it passes**

```bash
cd frontend && npx vitest run src/features/members/membersApi.test.ts
```

Expected: PASS.

- [ ] **Step 10: Add the query key and the search hook**

In `frontend/src/features/members/useMembers.ts`, extend `memberKeys`:

```ts
export const memberKeys = {
  all: ['members'] as const,
  tree: (params?: TreeQueryParams) => ['members', 'tree', params ?? {}] as const,
  // Nested under 'members' so a create/edit/delete invalidation refreshes search results too.
  search: (query: string, limit: number) => ['members', 'search', query, limit] as const,
}
```

Create `frontend/src/features/tree/useSearch.ts`:

```ts
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { membersApi } from '../members/membersApi'
import type { MemberSearchPage } from '../members/types'
import { memberKeys } from '../members/useMembers'
import { useDebouncedValue } from './useDebouncedValue'

/** How many hits the dropdown shows. The server reports the true total separately. */
export const SEARCH_LIMIT = 8

/** One Arabic character is too broad to be a useful query and matches most of the tree. */
export const MIN_QUERY_LENGTH = 2

const DEBOUNCE_MS = 250

const EMPTY: MemberSearchPage = { total: 0, items: [] }

export interface SearchState {
  page: MemberSearchPage
  /** A request is in flight for the current query — distinct from "no matches". */
  isSearching: boolean
}

/**
 * Server-side search (design spec §5.4), replacing the client-side `searchNodes` that could
 * only see the tree already loaded and could only report the size of its own truncated list.
 */
export const useSearch = (query: string): SearchState => {
  const trimmed = query.trim()
  const debounced = useDebouncedValue(trimmed, DEBOUNCE_MS)
  const enabled = debounced.length >= MIN_QUERY_LENGTH

  const { data, isFetching } = useQuery<MemberSearchPage>({
    queryKey: memberKeys.search(debounced, SEARCH_LIMIT),
    queryFn: () => membersApi.search(debounced, SEARCH_LIMIT),
    enabled,
    // Holding the previous page while the next one loads stops the dropdown collapsing to
    // "no results" between keystrokes.
    placeholderData: keepPreviousData,
  })

  return {
    page: enabled ? (data ?? EMPTY) : EMPTY,
    // The user has typed past the threshold but the settled query has not caught up yet: still
    // searching, even though no request has been issued.
    isSearching: trimmed.length >= MIN_QUERY_LENGTH && (isFetching || debounced !== trimmed),
  }
}
```

- [ ] **Step 11: Type-check, lint, and commit**

```bash
cd frontend && npx tsc -b && npx vitest run && npm run lint
git add frontend/src/features
git commit -m "feat: add the frontend search client and debounce hook"
```

---

## Task 5: Honest result counts in the shell

Fixes Phase 2.5 finding 2 — the label that read "8 نتائج" when 39 members matched. Also decouples `AppShell` from `FamilyTreeNode`: a search result is now an id, a name, and a caption, which is all the shell ever needed.

**Files:**
- Modify: `frontend/src/app/AppShell.tsx:5, 52-66, 331-394`
- Modify: `frontend/src/i18n/locales/ar.json`
- Modify: `frontend/src/i18n/locales/en.json`
- Test: `frontend/src/app/AppShell.test.tsx`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `interface SearchResult { id: string; name: string; meta: string }`, and `AppShellProps` gains `resultTotal?: number` and `isSearching?: boolean`. `onSelectResult` keeps its `(id: string) => void` signature.

- [ ] **Step 1: Write the failing tests**

Read `frontend/src/app/AppShell.test.tsx` first and reuse its existing render helper — `renderShell(props)` below stands in for whatever it already provides.

Append inside its top-level `describe`:

```tsx
  it('says how many results are shown out of the true total', async () => {
    renderShell({
      query: 'محمد',
      results: [
        { id: 'a', name: 'محمد', meta: 'داوود ‹ سلمان' },
        { id: 'b', name: 'محمد', meta: 'داوود ‹ علي' },
      ],
      resultTotal: 39,
    })

    // The old label reported results.length and would have said "2".
    expect(await screen.findByText(/39/)).toBeInTheDocument()
  })

  it('reports a plain count when the page holds every match', async () => {
    renderShell({
      query: 'خالد',
      results: [{ id: 'a', name: 'خالد', meta: 'داوود ‹ سلمان' }],
      resultTotal: 1,
    })

    expect(await screen.findByText('1 نتيجة')).toBeInTheDocument()
  })

  it('shows a searching state rather than "no results" while a request is in flight', async () => {
    renderShell({ query: 'محمد', results: [], resultTotal: 0, isSearching: true })

    expect(await screen.findByText('جارٍ البحث…')).toBeInTheDocument()
    expect(screen.queryByText('لا يوجد أفراد مطابقون.')).not.toBeInTheDocument()
  })
```

The two Arabic assertions must match `ar.json` exactly — `tree.resultCount_one` and `tree.noResults`. Read those values from the file rather than trusting the strings above if the test fails on an exact-match assertion.

- [ ] **Step 2: Run them to verify they fail**

```bash
cd frontend && npx vitest run src/app/AppShell.test.tsx
```

Expected: FAIL — `resultTotal` and `isSearching` are not props, the results have no `node` field so nothing renders, and the searching text does not exist.

- [ ] **Step 3: Add the i18n keys**

In `frontend/src/i18n/locales/en.json`, inside `"tree"`, add after `"resultCount_other"`:

```json
    "resultCountPartial": "Showing {{shown}} of {{total}} results",
    "searching": "Searching…",
```

In `frontend/src/i18n/locales/ar.json`, inside `"tree"`, add:

```json
    "resultCountPartial": "عرض {{shown}} من {{total}} نتيجة",
    "searching": "جارٍ البحث…",
```

`resultCountPartial` deliberately has no plural variants: with `shown` and `total` both interpolated, Arabic's plural category would have to agree with `total`, and i18next selects on `count` only. A single form that reads correctly for every number is more honest than a plural rule that is right for some and wrong for others.

- [ ] **Step 4: Reshape `SearchResult` and the label**

In `frontend/src/app/AppShell.tsx`:

Delete the now-unused import on line 5 (`import type { FamilyTreeNode } from '../features/members/types'`).

Replace the interface at lines 52-55:

```ts
/**
 * A search hit as the shell sees it: an id to select, a name to show, and a caption that
 * disambiguates. The shell no longer takes a FamilyTreeNode — results come from the server and
 * may name members that are not in the loaded tree at all.
 */
export interface SearchResult {
  id: string
  name: string
  meta: string
}
```

Add to `AppShellProps` after `results`:

```ts
  /** Every server-side match, which may exceed `results.length`. */
  resultTotal?: number
  isSearching?: boolean
```

Add both to the destructured parameters with defaults `resultTotal = 0` and `isSearching = false`.

Replace the count line (currently line 356):

```tsx
                  {results.length < resultTotal
                    ? t('tree.resultCountPartial', { shown: results.length, total: resultTotal })
                    : t('tree.resultCount', { count: resultTotal })}
```

Replace the results map (lines 358-380) so it reads from the flat shape — only the `key`, `onClick`, and the two text nodes change:

```tsx
                {results.map((result) => (
                  <button
                    key={result.id}
                    type="button"
                    onClick={() => onSelectResult?.(result.id)}
                    style={{
                      display: 'block',
                      width: '100%',
                      textAlign: 'start',
                      padding: '9px 12px',
                      border: 'none',
                      borderBottom: '1px solid var(--divider)',
                      background: 'transparent',
                      fontFamily: 'inherit',
                      cursor: 'pointer',
                    }}
                  >
                    <div style={{ fontSize: 13, fontWeight: 500 }}>{result.name}</div>
                    <div style={{ fontSize: 11, color: 'var(--text-3)', marginTop: 2 }}>
                      {result.meta}
                    </div>
                  </button>
                ))}
```

Replace the empty state (lines 381-392):

```tsx
                {results.length === 0 && (
                  <div
                    style={{
                      padding: '18px 12px',
                      textAlign: 'center',
                      fontSize: 13,
                      color: 'var(--text-2)',
                    }}
                  >
                    {/* "No matches" is a claim about the data; while a request is in flight it
                        is not yet true. Saying so would be wrong for the ~250ms debounce plus
                        round trip on every single query. */}
                    {isSearching ? t('tree.searching') : t('tree.noResults')}
                  </div>
                )}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd frontend && npx vitest run src/app/AppShell.test.tsx
```

Expected: PASS. `TreePage.test.tsx` and `tsc -b` will now fail because `TreePage` still passes the old shape — Task 6 fixes that. Do not fix it here.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/AppShell.tsx frontend/src/app/AppShell.test.tsx frontend/src/i18n/locales
git commit -m "feat: report the true search result total in the shell"
```

---

## Task 6: Wire the tree page to server search

Fixes Phase 2.5 finding 3 — results distinguished only by "الجيل 8" become results distinguished by their ancestry.

**Files:**
- Modify: `frontend/src/features/tree/TreePage.tsx:15-22, 90-97, 215-223`
- Modify: `frontend/src/features/tree/flattenTree.ts:112-123` (delete)
- Modify: `frontend/src/features/tree/flattenTree.test.ts` (delete the `searchNodes` tests)
- Test: `frontend/src/features/tree/TreePage.test.tsx`

**Interfaces:**
- Consumes: `useSearch` (Task 4); `SearchResult` (Task 5).
- Produces: nothing new for later tasks; Task 9 adds the reveal props.

- [ ] **Step 1: Write the failing test**

Add `vi.mocked(membersApi.search).mockResolvedValue({ total: 0, items: [] })` to the existing `beforeEach` in `frontend/src/features/tree/TreePage.test.tsx`, so every other test in the file has a stub.

Append inside the top-level `describe`:

```tsx
  it('shows the ancestor path for each search hit', async () => {
    const user = userEvent.setup()
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 39,
      items: [
        {
          id: 'f1',
          name: 'محمد',
          generation: 3,
          ancestors: [
            { id: 's1', name: 'داوود' },
            { id: 's2', name: 'سلمان' },
          ],
        },
      ],
    })
    renderPage()

    await user.type(await screen.findByLabelText(/بحث|Search/i), 'محمد')

    // Generation alone was the old caption and could not tell 39 محمدs apart.
    expect(await screen.findByText('داوود ‹ سلمان')).toBeInTheDocument()
    expect(await screen.findByText(/39/)).toBeInTheDocument()
  })

  it('asks the server rather than filtering the loaded tree', async () => {
    const user = userEvent.setup()
    vi.mocked(membersApi.search).mockResolvedValue({ total: 0, items: [] })
    renderPage()

    await user.type(await screen.findByLabelText(/بحث|Search/i), 'فارس')

    await waitFor(() => expect(membersApi.search).toHaveBeenCalledWith('فارس', 8))
  })
```

The search input's accessible name comes from `tree.searchPlaceholder`; the regex above matches either locale.

- [ ] **Step 2: Run it to verify it fails**

```bash
cd frontend && npx vitest run src/features/tree/TreePage.test.tsx
```

Expected: FAIL — `membersApi.search` is never called, and the shell still receives `{ node, meta }`.

- [ ] **Step 3: Rewire `TreePage`**

In `frontend/src/features/tree/TreePage.tsx`:

Change the `flattenTree` import to drop `searchNodes`:

```ts
import { ancestorIds, findNode, flattenTree, treeStats, type ExpandedMap } from './flattenTree'
```

Add:

```ts
import { useSearch } from './useSearch'
```

Replace the `results` memo (lines 90-97):

```tsx
  const { page: searchPage, isSearching } = useSearch(query)

  const results = useMemo<SearchResult[]>(
    () =>
      searchPage.items.map((hit) => ({
        id: hit.id,
        name: hit.name,
        // The path is the whole point: 39 members are named محمد, and their ancestry is the
        // only thing that tells them apart (design spec §5.4). A root member has no path, so
        // fall back to the generation rather than showing an empty caption.
        meta:
          hit.ancestors.length > 0
            ? hit.ancestors.map((ancestor) => ancestor.name).join(' ‹ ')
            : `${t('tree.gen')} ${hit.generation}`,
      })),
    [searchPage, t],
  )
```

Pass the new props to `AppShell` (lines 216-223):

```tsx
    <AppShell
      familyName={familyName}
      statLine={statLine}
      query={query}
      results={results}
      resultTotal={searchPage.total}
      isSearching={isSearching}
      onQueryChange={setQuery}
      onSelectResult={revealResult}
    >
```

- [ ] **Step 4: Delete the dead client-side search**

In `frontend/src/features/tree/flattenTree.ts`, delete lines 112-123 — the `searchNodes` export and its doc comment. The `normalize` helper stays: `flattenTree` still uses it for the outline's dim/highlight, which remains client-side because it describes rows already on screen.

In `frontend/src/features/tree/flattenTree.test.ts`, delete the `searchNodes` tests and remove it from the import.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd frontend && npx tsc -b && npx vitest run
```

Expected: PASS across the whole frontend suite.

`revealResult` still calls `ancestorIds` against the loaded tree, which returns `[]` for an id that is not there — so a hit outside the loaded tree would select nothing. The tree endpoint currently returns every member, so this cannot happen; leave it rather than adding a guard for a state the API cannot produce.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/features/tree
git commit -m "feat: show ancestor paths in search results from the server"
```

---

## Task 7: Windowing arithmetic

A pure function, separated from the DOM so it is testable at all — jsdom reports every element as 0px tall, so nothing about windowing can be verified through the rendered component.

**Files:**
- Create: `frontend/src/features/tree/windowRange.ts`
- Test: `frontend/src/features/tree/windowRange.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `interface WindowRange { startIndex: number; endIndex: number; padStart: number; padEnd: number }` — `endIndex` is exclusive; `padStart`/`padEnd` are spacer heights in **layout** pixels (unzoomed).
  - `windowRange(scrollTop: number, viewportHeight: number, rowHeight: number, count: number, overscan?: number): WindowRange`
  - `ROW_HEIGHT = 44`, `OVERSCAN = 6`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/features/tree/windowRange.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { ROW_HEIGHT, windowRange } from './windowRange'

describe('windowRange', () => {
  it('renders every row when the viewport has not been measured', () => {
    // jsdom reports 0 for every height, and so does the very first paint before layout. Both
    // must degrade to "render everything" — a windowing bug that hid all rows in tests would
    // otherwise be invisible until a human opened the app.
    const range = windowRange(0, 0, ROW_HEIGHT, 349)

    expect(range).toEqual({ startIndex: 0, endIndex: 349, padStart: 0, padEnd: 0 })
  })

  it('renders every row when there are none to window', () => {
    expect(windowRange(0, 800, ROW_HEIGHT, 0)).toEqual({
      startIndex: 0,
      endIndex: 0,
      padStart: 0,
      padEnd: 0,
    })
  })

  it('covers the viewport plus overscan at the top of the list', () => {
    const range = windowRange(0, 440, ROW_HEIGHT, 349, 6)

    expect(range.startIndex).toBe(0)
    // 440/44 = 10 visible, plus 6 overscan each way.
    expect(range.endIndex).toBe(22)
    expect(range.padStart).toBe(0)
    expect(range.padEnd).toBe((349 - 22) * ROW_HEIGHT)
  })

  it('moves the window and pads the space it left behind', () => {
    const range = windowRange(44 * 100, 440, ROW_HEIGHT, 349, 6)

    expect(range.startIndex).toBe(94)
    expect(range.endIndex).toBe(116)
    expect(range.padStart).toBe(94 * ROW_HEIGHT)
    expect(range.padEnd).toBe((349 - 116) * ROW_HEIGHT)
  })

  it('never overruns the end of the list', () => {
    const range = windowRange(44 * 348, 440, ROW_HEIGHT, 349, 6)

    expect(range.endIndex).toBe(349)
    expect(range.padEnd).toBe(0)
  })

  it('treats a negative scroll position as the top', () => {
    // Overscroll bounce on macOS and iOS reports a negative scroll offset.
    const range = windowRange(-120, 440, ROW_HEIGHT, 349, 6)

    expect(range.startIndex).toBe(0)
    expect(range.padStart).toBe(0)
  })

  it('keeps padStart + rendered height + padEnd equal to the full list height', () => {
    // The invariant that stops the scrollbar jumping as the window moves.
    const count = 349
    for (const scrollTop of [0, 500, 5000, 15000]) {
      const range = windowRange(scrollTop, 600, ROW_HEIGHT, count, 6)
      const rendered = (range.endIndex - range.startIndex) * ROW_HEIGHT

      expect(range.padStart + rendered + range.padEnd).toBe(count * ROW_HEIGHT)
    }
  })
})
```

- [ ] **Step 2: Run them to verify they fail**

```bash
cd frontend && npx vitest run src/features/tree/windowRange.test.ts
```

Expected: FAIL — cannot resolve `./windowRange`.

- [ ] **Step 3: Write the function**

Create `frontend/src/features/tree/windowRange.ts`:

```ts
/** Row height fixed by the design handoff. The rail gradient and elbow geometry assume it. */
export const ROW_HEIGHT = 44

/** Rows kept rendered beyond each edge, so a flick does not expose blank space mid-scroll. */
export const OVERSCAN = 6

export interface WindowRange {
  startIndex: number
  /** Exclusive. */
  endIndex: number
  /** Spacer height above the window, in layout pixels. */
  padStart: number
  /** Spacer height below the window, in layout pixels. */
  padEnd: number
}

/**
 * Which slice of a uniform-height list intersects the viewport (design spec §5.4).
 *
 * Pure and DOM-free by design: jsdom gives every element a height of 0, so a component test
 * cannot tell a correct window from a broken one. All measurement lives in `useVisibleRange`,
 * and every unit here is a LAYOUT pixel — the caller divides out any CSS zoom before calling.
 *
 * An unmeasured viewport (height 0) renders everything rather than nothing. Failing toward
 * "too many rows" costs a slow first paint; failing toward "no rows" is a blank screen.
 */
export const windowRange = (
  scrollTop: number,
  viewportHeight: number,
  rowHeight: number,
  count: number,
  overscan: number = OVERSCAN,
): WindowRange => {
  if (count === 0) return { startIndex: 0, endIndex: 0, padStart: 0, padEnd: 0 }
  if (viewportHeight <= 0 || rowHeight <= 0) {
    return { startIndex: 0, endIndex: count, padStart: 0, padEnd: 0 }
  }

  const firstVisible = Math.floor(Math.max(0, scrollTop) / rowHeight)
  const startIndex = Math.max(0, firstVisible - overscan)
  const spanned = Math.ceil(viewportHeight / rowHeight) + overscan * 2
  const endIndex = Math.min(count, startIndex + spanned)

  return {
    startIndex,
    endIndex,
    padStart: startIndex * rowHeight,
    padEnd: (count - endIndex) * rowHeight,
  }
}
```

- [ ] **Step 4: Run them to verify they pass**

```bash
cd frontend && npx vitest run src/features/tree/windowRange.test.ts
```

Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/tree/windowRange.ts frontend/src/features/tree/windowRange.test.ts
git commit -m "feat: add pure windowing arithmetic for the outline"
```

---

## Task 8: Measure the viewport

The DOM half of windowing, kept separate from the arithmetic. It also owns the zoom conversion, so `windowRange` never has to know that CSS zoom exists.

**Files:**
- Create: `frontend/src/features/tree/useVisibleRange.ts`
- Modify: `frontend/vitest.setup.ts`

**Interfaces:**
- Consumes: `windowRange`, `ROW_HEIGHT`, `WindowRange` (Task 7).
- Produces: `useVisibleRange(scrollRef: RefObject<HTMLDivElement | null>, listRef: RefObject<HTMLDivElement | null>, count: number, zoom: number): WindowRange`

- [ ] **Step 1: Write the hook**

There is no separate red step here: the hook is measurement plumbing whose arithmetic is already covered by Task 7 and whose DOM reads all return 0 in jsdom. Task 9's component tests exercise it end to end.

Create `frontend/src/features/tree/useVisibleRange.ts`:

```ts
import { useCallback, useEffect, useState, type RefObject } from 'react'
import { ROW_HEIGHT, windowRange, type WindowRange } from './windowRange'

const allRows = (count: number): WindowRange => ({
  startIndex: 0,
  endIndex: count,
  padStart: 0,
  padEnd: 0,
})

/**
 * Tracks which rows intersect the scroll viewport.
 *
 * Measurement uses getBoundingClientRect on both elements rather than scrollTop and offsetTop,
 * because the content is CSS-zoomed: offsetTop is reported in the zoomed element's own
 * coordinate space while scrollTop is in the scroll container's, and mixing the two drifts
 * further off the more the user zooms. Two rects are in one space by construction.
 *
 * Both measurements are then divided by `zoom` to convert visual pixels into layout pixels,
 * which is the only unit `windowRange` and the spacer divs understand.
 */
export const useVisibleRange = (
  scrollRef: RefObject<HTMLDivElement | null>,
  listRef: RefObject<HTMLDivElement | null>,
  count: number,
  zoom: number,
): WindowRange => {
  const [range, setRange] = useState<WindowRange>(() => allRows(count))

  const measure = useCallback(() => {
    const scroller = scrollRef.current
    const list = listRef.current
    if (scroller === null || list === null || zoom <= 0) {
      setRange(allRows(count))
      return
    }

    const scrollerRect = scroller.getBoundingClientRect()
    const listRect = list.getBoundingClientRect()

    // How far the list's top has travelled above the viewport's top. Negative while the list
    // is still below the fold, which windowRange clamps to the start of the list.
    const scrolledPast = (scrollerRect.top - listRect.top) / zoom
    const viewportHeight = scrollerRect.height / zoom

    setRange(windowRange(scrolledPast, viewportHeight, ROW_HEIGHT, count))
  }, [scrollRef, listRef, count, zoom])

  useEffect(() => {
    measure()

    const scroller = scrollRef.current
    if (scroller === null) return

    scroller.addEventListener('scroll', measure, { passive: true })
    const observer = new ResizeObserver(measure)
    observer.observe(scroller)

    return () => {
      scroller.removeEventListener('scroll', measure)
      observer.disconnect()
    }
  }, [measure, scrollRef])

  return range
}
```

- [ ] **Step 2: Stub `ResizeObserver` for jsdom**

`ResizeObserver` does not exist in jsdom and the hook constructs one unconditionally. Add to `frontend/vitest.setup.ts`:

```ts
// jsdom has no ResizeObserver. The outline observes its scroll container to re-window on
// resize; in tests every element measures 0 and the window degrades to "render all rows", so a
// no-op stub is faithful rather than merely convenient.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver
```

- [ ] **Step 3: Verify nothing regressed**

```bash
cd frontend && npx tsc -b && npx vitest run
```

Expected: PASS — the hook is not yet used, so this only confirms it compiles and the setup stub is harmless.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/tree/useVisibleRange.ts frontend/vitest.setup.ts
git commit -m "feat: measure the outline viewport for windowing"
```

---

## Task 9: Window the outline, fix zoom, and scroll to revealed rows

Three changes that must land together: windowing breaks scroll-to-reveal (the target row may not be rendered), and the zoom fix changes the pitch that windowing measures against.

**Files:**
- Modify: `frontend/src/features/tree/TreeCanvas.tsx:1, 25-43, 96-192, 279-321`
- Modify: `frontend/src/features/tree/TreePage.tsx:44-55, 116-127, 224-252`
- Test: `frontend/src/features/tree/TreePage.test.tsx`

**Interfaces:**
- Consumes: `useVisibleRange` (Task 8), `ROW_HEIGHT` (Task 7).
- Produces: `TreeCanvasProps` gains `revealId: string | null` and `onRevealed: () => void`; `TreeRowViewProps` gains `setSize: number` and `posInSet: number`.

- [ ] **Step 1: Write the failing tests**

Append inside the top-level `describe` in `frontend/src/features/tree/TreePage.test.tsx`:

```tsx
  it('labels each row with its position in the whole outline', async () => {
    // Windowing removes rows from the DOM, so a screen reader can no longer count them. Without
    // setsize/posinset it would announce "1 of 2" on a 349-member tree.
    renderPage()

    const rows = await screen.findAllByRole('treeitem')
    expect(rows[0]).toHaveAttribute('aria-setsize', String(rows.length))
    expect(rows[0]).toHaveAttribute('aria-posinset', '1')
  })

  it('scrolls a revealed search hit into view', async () => {
    const user = userEvent.setup()
    const scrollTo = vi.fn()
    Element.prototype.scrollTo = scrollTo as unknown as typeof Element.prototype.scrollTo

    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 'f1', name: 'فارس', generation: 2, ancestors: [{ id: 's1', name: 'سليمان' }] }],
    })
    renderPage()

    await user.type(await screen.findByLabelText(/بحث|Search/i), 'فارس')
    // Click the result's caption — the ancestor path is unique to the dropdown, while the
    // hit's own name also appears in the outline behind it.
    await user.click(await screen.findByText('سليمان'))

    // Virtualized, the row may not be in the DOM at all — expanding its ancestors is no longer
    // enough to put it in front of the user.
    await waitFor(() => expect(scrollTo).toHaveBeenCalled())
  })
```

- [ ] **Step 2: Run them to verify they fail**

```bash
cd frontend && npx vitest run src/features/tree/TreePage.test.tsx
```

Expected: FAIL — no `aria-setsize` attribute, and `scrollTo` is never called.

- [ ] **Step 3: Window the canvas and fix zoom**

In `frontend/src/features/tree/TreeCanvas.tsx`:

Replace the React import on line 1 and add the two new module imports:

```ts
import { useEffect, useRef, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { Direction } from '../../i18n/useDirection'
import type { TreeRow } from './flattenTree'
import { useVisibleRange } from './useVisibleRange'
import { ROW_HEIGHT } from './windowRange'
```

Add to `TreeCanvasProps` after `isLoading`:

```ts
  /** A search hit to scroll to once its branch is open. Cleared via onRevealed. */
  revealId: string | null
  onRevealed: () => void
```

Add both to the destructured parameters.

Inside the component, after `const rtl = direction === 'rtl'`:

```tsx
  const scrollRef = useRef<HTMLDivElement>(null)
  const listRef = useRef<HTMLDivElement>(null)
  const range = useVisibleRange(scrollRef, listRef, rows.length, zoom)

  useEffect(() => {
    if (revealId === null) return

    const index = rows.findIndex((row) => row.id === revealId)
    const scroller = scrollRef.current
    const list = listRef.current

    // The row is not in the outline — a still-collapsed ancestor, or a hit from a result set
    // that has since changed. Clear the request rather than leaving it pending forever.
    if (index === -1 || scroller === null || list === null) {
      onRevealed()
      return
    }

    // The row's offset from the list's top in layout pixels, converted to the scroll
    // container's visual pixels — the same coordinate mapping useVisibleRange performs, in
    // reverse. Centred rather than top-aligned: a family member is only meaningful next to
    // their siblings and parent.
    const listTop = list.getBoundingClientRect().top - scroller.getBoundingClientRect().top
    const rowTop = scroller.scrollTop + listTop + index * ROW_HEIGHT * zoom
    scroller.scrollTo({ top: Math.max(0, rowTop - scroller.clientHeight / 2), behavior: 'smooth' })

    onRevealed()
  }, [revealId, rows, zoom, onRevealed])
```

Attach `ref={scrollRef}` to the outermost scrolling `<div>` (currently line 101).

Replace the zoom styling on the content wrapper (currently lines 139-147):

```tsx
      <div
        style={{
          padding: '36px 40px 120px',
          minHeight: '100%',
          // CSS `zoom`, not `transform: scale()`. A transform is purely visual: it never grows
          // the scroll container's scrollHeight, so zooming past 1.0 pushed content outside the
          // scrollable area with no way to reach it. `zoom` participates in layout, so the
          // scrollbar tracks the scaled content.
          //
          // The cost is the transition the design handoff had on `transform` — `zoom` is not
          // reliably animatable across browsers, so zoom steps are now instant. An unreachable
          // bottom of the tree is the worse defect.
          zoom,
        }}
      >
```

Replace the tree list (currently lines 180-192):

```tsx
        <div role="tree" aria-label={t('tree.treeLabel')} ref={listRef}>
          {/* Spacers stand in for the rows outside the window, so the scrollbar reflects the
              whole outline rather than only what is rendered. */}
          <div aria-hidden="true" style={{ height: range.padStart }} />
          {rows.slice(range.startIndex, range.endIndex).map((row, offset) => (
            <TreeRowView
              key={row.id}
              row={row}
              rtl={rtl}
              selected={selectedId === row.id}
              // Windowing removes rows from the DOM; without these a screen reader would
              // announce the size of the window instead of the size of the family.
              setSize={rows.length}
              posInSet={range.startIndex + offset + 1}
              onToggle={onToggle}
              onSelect={onSelect}
              onMenu={onMenu}
            />
          ))}
          <div aria-hidden="true" style={{ height: range.padEnd }} />
        </div>
```

Add to `TreeRowViewProps`:

```ts
  setSize: number
  posInSet: number
```

Add both to `TreeRowView`'s destructured parameters, and add the attributes to its root `<div role="treeitem">` (currently lines 315-320):

```tsx
      aria-setsize={setSize}
      aria-posinset={posInSet}
```

`aria-setsize` and `aria-posinset` describe the flattened outline rather than each row's sibling group. That matches the existing markup, which already presents every row as a flat list of `treeitem`s under a single `role="tree"` with no intervening `role="group"`.

- [ ] **Step 4: Plumb reveal through the page**

In `frontend/src/features/tree/TreePage.tsx`, add state alongside the others (near line 50):

```tsx
  const [revealId, setRevealId] = useState<string | null>(null)
```

Add `setRevealId(id)` inside `revealResult`:

```tsx
  /** Reveal a search hit: open every branch above it, scroll to it, then select it. */
  const revealResult = (id: string) => {
    const opened: Record<string, boolean> = { ...expanded }
    ancestorIds(roots, id).forEach((ancestor) => {
      opened[ancestor] = true
    })
    setRootOpen(true)
    setExpanded(opened)
    setSelectedId(id)
    setPanelOpen(true)
    // Expanding ancestors used to be enough — the row was always in the DOM. Windowed, it may
    // not be, so the canvas is asked to scroll to it once this render settles.
    setRevealId(id)
    setQuery('')
  }
```

Add the two props to `TreeCanvas`, after `isLoading`:

```tsx
        revealId={revealId}
        onRevealed={clearReveal}
```

and declare the callback with the other hooks, so the reveal effect's dependency is stable:

```tsx
  const clearReveal = useCallback(() => setRevealId(null), [])
```

`useCallback` is already imported in this file.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd frontend && npx tsc -b && npx vitest run
```

Expected: PASS across the frontend suite. Existing `TreePage` tests still find their rows because jsdom measures 0 and `windowRange` renders everything.

- [ ] **Step 6: Lint and commit**

```bash
cd frontend && npm run lint
git add frontend/src/features/tree
git commit -m "feat: window the outline, fix zoom scrolling, and scroll to revealed hits"
```

---

## Task 10: Reconcile the documentation

The design spec currently says Phase 3 builds an SVG renderer with an orientation toggle. It does not, and has not since the 2026-08-17 supersession. Leaving both statements in the repository means the next reader has to work out which is true.

**Files:**
- Modify: `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md` — §5's supersession note and §8's table
- Modify: `README.md:6, 39`

**Interfaces:** none.

- [ ] **Step 1: Correct the delivery sequence**

In `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md`, replace the Phase 3 row of the §8 table:

```markdown
| 3 — Visualization | **Scope corrected 2026-08-17 — see §5's supersession note.** Delivered: server-side search (`pg_trgm` index, recursive-CTE ancestor paths, true result totals), outline virtualization, and the zoom scroll fix. Not delivered, because the 2026-08-17 design decision removed them: layout engine, SVG renderer, orientation toggle. Expand/collapse, zoom, and node actions shipped early with the design handoff during Phase 2. |
```

- [ ] **Step 2: Record what §5.4 still binds**

Append to the supersession note in §5, after the paragraph beginning "§5.5's node component contract still holds in spirit":

```markdown
> **§5.4 after the supersession (recorded 2026-08-17, Phase 3).** Three of its four requirements
> survive and were built in Phase 3: server-side search against the trigram index, the ancestor
> path as a *required* discriminator, and viewport virtualization — the last one demoted to "a
> later concern" by the outline's cost model, then built anyway once the imported tree made 349
> simultaneous rows real. What does not survive is zoom and pan as a transform on a root `<g>`:
> the outline scrolls and CSS-zooms instead. Memoized layout does not apply — there is no layout
> step left to memoize.
```

- [ ] **Step 3: Update the README**

Change line 6 to:

```markdown
- **Current phase:** Phase 3 — Visualization
```

Replace line 39:

```markdown
The members screen is at `/members` once signed in, and the tree outline is at `/`. Search runs
server-side and returns each hit's ancestor path; the outline renders only the rows in view.
```

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-08-16-family-tree-saas-design.md README.md
git commit -m "docs: reconcile the spec and README with Phase 3's delivered scope"
```

---

## Task 11: Verify against the imported family

Phase 2.5 established the pattern: the findings that motivated this phase came from a human driving the real 349-member tree, not from tests. The same exercise closes it.

**Files:**
- Modify: `docs/superpowers/plans/2026-08-17-phase-3-visualization.md` (this file — append findings)

**Interfaces:** none.

- [ ] **Step 1: Run the full suite**

```bash
cd d:/Work/Motasem/FamilyTree
dotnet test
cd frontend && npx tsc -b && npx vitest run && npm run lint
```

Expected: all green. Record any failure here rather than working around it.

- [ ] **Step 2: Apply migrations and start the stack**

```bash
cd d:/Work/Motasem/FamilyTree
docker compose up -d postgres

export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=familytree;Username=familytree;Password=devpassword"
dotnet ef database update --project src/FamilyTree.Infrastructure --startup-project src/FamilyTree.Api

dotnet run --project src/FamilyTree.Api --no-launch-profile --urls http://localhost:5000
cd frontend && npm run dev
```

- [ ] **Step 3: Search the most frequent name**

Sign in as the seeded admin and search محمد — the name Phase 2.5 measured at 39 occurrences.

Record: does the label report 39 rather than 8? Are the 8 shown hits distinguishable by their ancestor paths? Compare directly against the Phase 2.5 finding, which read "الجيل 8 / الجيل 9 / الجيل 8 / الجيل 8" for four different people.

- [ ] **Step 4: Confirm windowing**

Expand every branch, then in the browser console:

```js
document.querySelectorAll('[role="treeitem"]').length
```

Phase 2.5 measured 349. Record the new number, and the number again after scrolling to the bottom. Also confirm `aria-setsize` still reads 349 on a rendered row — the count the user is told must not shrink with the window.

- [ ] **Step 5: Confirm the zoom fix**

Zoom in past 1.0 and scroll to the deepest row. Before this phase the content was clipped with no way to reach it. Record whether the bottom of the tree is now reachable at maximum zoom, and note that zoom steps are no longer animated.

- [ ] **Step 6: Select a search hit deep in the tree**

Search a name that lives at generation 8 or deeper, select it from the dropdown, and confirm the outline expands its ancestors, scrolls it into view, and selects it.

- [ ] **Step 7: Record findings and commit**

Append a "## Task 11 — verification findings (recorded YYYY-MM-DD)" section to this plan with a table of results, following the format of Phase 2.5's Task 8 section. Include anything that did not work.

```bash
git commit -am "docs: record Phase 3 verification findings"
```

---

## Self-review

**Spec coverage.** §5.4's four requirements: server-side trigram search → Tasks 1–3; ancestor path as required discriminator → Tasks 2, 6; viewport virtualization → Tasks 7–9; memoized layout and root-`<g>` zoom/pan → not applicable after the supersession, recorded in Task 10. §3.4's `pg_trgm` index → Task 1. §4.4's uniform non-disclosure → Task 2 (blank and no-match both return an empty page) and Task 3 (200, not 404). §6's standing cross-tenant requirement → Task 2, `Another_tenants_members_are_never_matched` and `An_unauthenticated_tenant_context_matches_nothing`, load-bearing here because raw SQL bypasses the query filter. Phase 2 deviation 1 → Task 1 verbatim. Phase 2.5 findings 1, 2, 3 → Tasks 7–9, 5, and 6 respectively.

**Deliberate deviations.**

1. **No fuzzy matching.** Trigram similarity would find `ممد` when a user types `محمد`, which is tempting given the two typos the import reproduced faithfully. It changes what search *means* rather than how it is implemented, and it deserves its own decision. The index supports it whenever that decision is made.
2. **Paging bounds are clamped, not rejected.** `limit` outside 1..50 and a negative `offset` are corrected silently. This departs from the "fail fast with clear error messages" instinct in the coding rules, and is chosen because the alternative adds an error code to a public contract for a condition no legitimate client can produce and no user can act on. Documented in the README so it is contract rather than accident.
3. **No Playwright coverage.** Design spec §6 calls for an E2E suite covering login → add member → move member → public link. No Playwright harness exists in the repo — Phase 2 did not build one, and `frontend/package.json` carries no dependency on it. Standing one up is its own piece of work belonging with Phase 7 (Hardening, which owns integration testing and CI/CD), not a rider on this phase. Task 11 is manual verification in its place, following Phase 2.5's precedent.
4. **Hand-rolled windowing instead of `@tanstack/react-virtual`.** Chosen against the "prefer battle-tested libraries" rule, for three specific reasons: jsdom measures every element at 0px, so a measuring virtualizer renders nothing and would silently break the existing `TreePage` suite; the CSS-zoom fix means the pitch conversion has to be explicit either way; and perfectly uniform rows remove the library's main advantage. The arithmetic is pure and covered by seven unit tests.
5. **Zoom transitions are lost.** Fixing the scroll bug means moving from `transform: scale()` to CSS `zoom`, which is not reliably animatable. An unreachable bottom of the tree is the worse defect, and this tradeoff was accepted when the fix was approved.

**Open risk.** One item cannot be settled from here: whether `CREATE EXTENSION pg_trgm` succeeds on the target deployed database. Testcontainers runs as superuser, so the test suite cannot discover this. Task 1's README note states the requirement and the `IF NOT EXISTS` guard makes a pre-installed extension a no-op, but a least-privilege database with no `pg_trgm` installed will fail the migration — loudly, at deploy time, which is the right place for it to fail.

**Type consistency.** `FamilyMemberSearchResponse(Total, Items)` in Task 2 is what Task 3's endpoint returns and what Task 4's `MemberSearchPage { total, items }` deserializes. `FamilyMemberSearchHit.Ancestors` (root-first, hit excluded) becomes `MemberSearchHit.ancestors`, consumed by Task 6's `join(' ‹ ')`. `SearchResult { id, name, meta }` is defined in Task 5 and constructed in Task 6. `WindowRange` and `ROW_HEIGHT` are defined in Task 7 and consumed by Task 8's `useVisibleRange` and Task 9's reveal effect. `revealId` / `onRevealed` are declared in Task 9's `TreeCanvasProps` and supplied in the same task. `setSize` / `posInSet` are declared and passed within Task 9.
