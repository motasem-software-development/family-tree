# Phase 2 — Family Tree Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the family member entity and the parent-child hierarchy — create, read, update, delete, and a whole-tree read endpoint — enforced by database constraints, tenant-isolated, and reachable from a bilingual RTL-capable management screen.

**Architecture:** A `FamilyMember` aggregate in `Domain` carries its own validation and an application-managed `Version` concurrency token. `Infrastructure` maps it with a composite self-foreign-key that makes a cross-tree parent link physically unrepresentable, and a global query filter that makes a forgotten `WHERE tenant_id` harmless. `FamilyMemberService` asserts ownership and translates rule violations into stable error codes; `FamilyTreeAssembler` in `Application` is a pure function turning a flat member list into a nested DTO with computed generations, so tree shaping is unit-testable without a database. Minimal API endpoints under `/api/v1` declare the permission they need. The React screen is a list — visualization is Phase 3.

**Tech Stack:** .NET 10 / C# 14, ASP.NET Core Minimal APIs, EF Core 10.0.11 + Npgsql 10.0.3, PostgreSQL 17, xUnit 2.9.3, FluentAssertions 7.2.0, Testcontainers 4.14.0, React 19 + TypeScript, TanStack Query, react-i18next, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md` (§3 data model, §4 API and authorization, §6 testing, §8 delivery sequence) and `Family Tree SaaS.md` (§9–§12 entity and schema, §21–§28 API, §42 validation, §43 concurrency, §57 phases). SRS `Family Tree SaaS Platform.md` §10 and §32 fix the generation semantics.

---

## Global Constraints

Every task's requirements implicitly include this section.

- **Target framework** `net10.0`. `Nullable=enable`, `ImplicitUsings=enable`, **`TreatWarningsAsErrors=true`**, `EnforceCodeStyleInBuild=true` (from `Directory.Build.props`). A build warning fails the build — fix the cause, never suppress it. This includes `IDE0005` (unnecessary using directive).
- **FluentAssertions is pinned to `7.2.0`.** Do not upgrade to 8.x — that version is commercially licensed. This applies to all three test projects.
- **The family tree aggregate type is named `FamilyTreeAggregate`, not `FamilyTree`** (`src/FamilyTree.Domain/FamilyTrees/FamilyTree.cs`). `FamilyTree` is the root namespace and would collide.
- **Dependencies point inward only:** `Domain` → nothing; `Application` → `Domain` + `Contracts`; `Infrastructure` → `Application` + `Domain`; `Api` → all. `Contracts` → nothing. `DomainDependencyTests` enforces this; do not add a reference that breaks it.
- **Never use the EF in-memory provider.** Database behavior is verified against real PostgreSQL via Testcontainers (design spec §6).
- **Tenant is resolved server-side from JWT claims only** (`ITenantContext`). Never accept a tenant id from a route, query string, header, or body.
- **Cross-tenant access returns 404, never 403** (design spec §4.4). A 403 confirms the identifier exists.
- **Error responses are RFC 7807 Problem Details with a stable machine-readable `code`.** Message text is not part of the contract — the frontend translates from `code` (design spec §4.8).
- **Name validation:** required, trimmed, 1–200 characters (tech spec §42).
- **Generation is never stored** — computed during tree assembly (design spec §3.6). First-generation members (`ParentId = NULL`) are **Generation 1** (SRS §10).
- **The root family is not a member.** It is the `family_trees` row. Never create a `FamilyMember` named after the family (tech spec §10, BR-003).
- Commit messages follow conventional commits (`feat:`, `fix:`, `test:`, `chore:`, `docs:`, `refactor:`).

### Deviations from the spec, recorded deliberately

1. **The `pg_trgm` GIN index on `name` is deferred to Phase 3.** Design spec §3.4 lists it under the data model, so it would naturally land in this phase's migration. It exists solely to serve fragment search, and the search endpoint is Phase 3 work (design spec §8). Creating a PostgreSQL extension is the single highest-risk statement in the migration — it needs elevated privileges and must work identically in Testcontainers and in the deployed database. Carrying that risk one phase before anything queries the index buys nothing. **Phase 3's planner must add `CREATE EXTENSION IF NOT EXISTS pg_trgm` and the GIN index alongside the search endpoint.**
2. **Member move and cycle detection are not in this phase.** Tech spec §24–§25 describe them; design spec §8 places them in Phase 5. This plan's `PUT` endpoint therefore *rejects* any attempt to change `parentId` rather than implementing the move (design spec §4.6). A member's parent is fixed at creation until Phase 5 ships the move command.
3. **Audit logging is not in this phase.** Design spec §3.7 and §8 place `audit_logs` in Phase 5. Deletion in this phase is a hard delete with no audit row, matching design spec §1.1's "hard delete" decision but leaving the audit trail to Phase 5. Note this is a real gap while it lasts: between Phase 2 and Phase 5, a deleted member leaves no record.

### Execution preamble — read before Task 1

A previous session left the API and the Vite dev server running in the background, and the API's `dotnet run` holds a lock on the build output directory. **The first `dotnet build` will fail with a file-lock error unless those are stopped.** Before starting Task 1:

```bash
# Stop any dotnet run / vite processes holding build outputs.
# On Windows PowerShell:
#   Get-Process dotnet, node -ErrorAction SilentlyContinue | Stop-Process -Force
# Then confirm nothing is listening on the app ports:
netstat -ano | grep -E ":(5000|5173) .*LISTENING" || echo "clear"
```

Docker must be running for the integration tests (Testcontainers).

---

## File Structure

**Domain** — entity and its rules, no dependencies:
- `src/FamilyTree.Domain/FamilyMembers/FamilyMember.cs` — the aggregate: validation, rename, concurrency version.
- `src/FamilyTree.Domain/Common/DomainException.cs` *(modify)* — add `NotFoundException` and `ConflictException` subclasses so the API can map 404 and 409 without the service knowing about HTTP.

**Contracts** — wire types, no dependencies:
- `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberResponse.cs`
- `src/FamilyTree.Contracts/FamilyMembers/CreateFamilyMemberRequest.cs`
- `src/FamilyTree.Contracts/FamilyMembers/UpdateFamilyMemberRequest.cs`
- `src/FamilyTree.Contracts/FamilyTrees/FamilyTreeResponse.cs`
- `src/FamilyTree.Contracts/FamilyTrees/RenameFamilyTreeRequest.cs`
- `src/FamilyTree.Contracts/FamilyTrees/FamilyTreeViewResponse.cs` — the nested view DTO plus its node record.

**Application** — interfaces and pure logic:
- `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`
- `src/FamilyTree.Application/FamilyTrees/IFamilyTreeService.cs`
- `src/FamilyTree.Application/FamilyTrees/FamilyTreeAssembler.cs` — pure static tree shaping; the only place generation is computed.

**Infrastructure** — persistence and service implementations:
- `src/FamilyTree.Infrastructure/Persistence/Configurations/FamilyMemberConfiguration.cs`
- `src/FamilyTree.Infrastructure/Persistence/ApplicationDbContext.cs` *(modify)* — `DbSet` + query filter.
- `src/FamilyTree.Infrastructure/Persistence/Migrations/*_AddFamilyMembers.cs` *(generated)*
- `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`
- `src/FamilyTree.Infrastructure/FamilyTrees/FamilyTreeService.cs`
- `src/FamilyTree.Infrastructure/DependencyInjection.cs` *(modify)* — register both services.

**Api** — endpoints:
- `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs`
- `src/FamilyTree.Api/Endpoints/FamilyTrees/FamilyTreeEndpoints.cs`
- `src/FamilyTree.Api/Errors/ExceptionHandler.cs` *(modify)* — status mapping for the two new exception types.
- `src/FamilyTree.Api/Program.cs` *(modify)* — map the two endpoint groups.

**Frontend**:
- `frontend/src/features/members/types.ts`
- `frontend/src/features/members/membersApi.ts`
- `frontend/src/features/members/useMembers.ts` — TanStack Query hooks.
- `frontend/src/features/members/MembersPage.tsx`
- `frontend/src/features/members/MemberForm.tsx`
- `frontend/src/routes/AppRoutes.tsx` *(modify)*
- `frontend/src/i18n/locales/{ar,en}.json` *(modify)*

**Tests**:
- `tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberTests.cs`
- `tests/FamilyTree.Application.Tests/FamilyTrees/FamilyTreeAssemblerTests.cs`
- `tests/FamilyTree.Api.IntegrationTests/Persistence/FamilyMemberConstraintTests.cs`
- `tests/FamilyTree.Api.IntegrationTests/Persistence/QueryFilterInvariantTests.cs` *(modify)*
- `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberServiceTests.cs`
- `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs`
- `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyTreeEndpointsTests.cs`
- `frontend/src/features/members/membersApi.test.ts`
- `frontend/src/features/members/MembersPage.test.tsx`

---

## Task 1: FamilyMember domain entity

**Files:**
- Create: `src/FamilyTree.Domain/FamilyMembers/FamilyMember.cs`
- Test: `tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberTests.cs`

**Interfaces:**
- Consumes: `Entity` (base class with `Id`, `CreatedAt`, `UpdatedAt`, `InitializeTimestamps`, `Touch`), `ITenantOwned`, `DomainException` — all from `FamilyTree.Domain.Common`.
- Produces:
  - `FamilyMember.MaxNameLength` = `200`
  - `static FamilyMember Create(Guid tenantId, Guid familyTreeId, Guid? parentId, string name, DateTimeOffset now)`
  - `void Rename(string name, DateTimeOffset now)`
  - Properties: `Guid TenantId`, `Guid FamilyTreeId`, `Guid? ParentId`, `string Name`, `int Version`
  - Error codes: `MEMBER_TENANT_REQUIRED`, `MEMBER_TREE_REQUIRED`, `MEMBER_NAME_REQUIRED`, `MEMBER_NAME_TOO_LONG`

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Domain.Tests.FamilyMembers;

public class FamilyMemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    [Fact]
    public void Create_makes_a_first_generation_member_when_no_parent_is_given()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);

        member.Id.Should().NotBeEmpty();
        member.TenantId.Should().Be(TenantId);
        member.FamilyTreeId.Should().Be(TreeId);
        member.ParentId.Should().BeNull();
        member.Name.Should().Be("سليمان");
        member.CreatedAt.Should().Be(Now);
        member.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_links_a_descendant_to_its_parent()
    {
        var parent = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);

        var child = FamilyMember.Create(TenantId, TreeId, parent.Id, "فارس", Now);

        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public void Create_starts_the_concurrency_version_at_one()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);

        member.Version.Should().Be(1);
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_id()
    {
        var act = () => FamilyMember.Create(Guid.Empty, TreeId, null, "سليمان", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_TENANT_REQUIRED");
    }

    [Fact]
    public void Create_rejects_an_empty_family_tree_id()
    {
        var act = () => FamilyMember.Create(TenantId, Guid.Empty, null, "سليمان", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_TREE_REQUIRED");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => FamilyMember.Create(TenantId, TreeId, null, name, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_NAME_REQUIRED");
    }

    [Fact]
    public void Create_rejects_a_name_longer_than_200_characters()
    {
        var act = () => FamilyMember.Create(TenantId, TreeId, null, new string('x', 201), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_NAME_TOO_LONG");
    }

    [Fact]
    public void Create_trims_surrounding_whitespace_from_the_name()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "  فارس  ", Now);

        member.Name.Should().Be("فارس");
    }

    [Fact]
    public void Create_accepts_an_empty_parent_id_as_no_parent()
    {
        // Guid.Empty arriving from a caller means "no parent", not "a parent whose id is zero".
        var member = FamilyMember.Create(TenantId, TreeId, Guid.Empty, "سليمان", Now);

        member.ParentId.Should().BeNull();
    }

    [Fact]
    public void Rename_changes_the_name_and_advances_the_version()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);
        var later = Now.AddDays(1);

        member.Rename("فارس أحمد", later);

        member.Name.Should().Be("فارس أحمد");
        member.Version.Should().Be(2);
        member.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Rename_does_not_change_the_tenant_the_tree_or_the_parent()
    {
        var parent = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);
        var child = FamilyMember.Create(TenantId, TreeId, parent.Id, "فارس", Now);

        child.Rename("فارس أحمد", Now.AddDays(1));

        child.TenantId.Should().Be(TenantId);
        child.FamilyTreeId.Should().Be(TreeId);
        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public void Rename_applies_the_same_name_rules_as_create()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);

        var act = () => member.Rename("   ", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_NAME_REQUIRED");
    }

    [Fact]
    public void Rename_does_not_advance_the_version_when_validation_fails()
    {
        // A rejected rename must leave the entity untouched, or a client that retries
        // after a validation error would find its version stale for no reason.
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);

        var act = () => member.Rename("", Now);

        act.Should().Throw<DomainException>();
        member.Version.Should().Be(1);
        member.Name.Should().Be("فارس");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Domain.Tests -v q`
Expected: FAIL — compile error, `FamilyMember` does not exist in namespace `FamilyTree.Domain.FamilyMembers`.

- [ ] **Step 3: Write the entity**

Create `src/FamilyTree.Domain/FamilyMembers/FamilyMember.cs`:

```csharp
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.FamilyMembers;

/// <summary>
/// A person in the family hierarchy. Per BR-003 the root family is NOT a member — it is the
/// <c>family_trees</c> row — so a first-generation member has <c>ParentId = null</c>
/// (technical specification §10).
/// </summary>
public sealed class FamilyMember : Entity, ITenantOwned
{
    public const int MaxNameLength = 200;

    private FamilyMember() { }

    public Guid TenantId { get; private set; }
    public Guid FamilyTreeId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Application-managed optimistic concurrency token (design spec §3.1). Mapped as an EF
    /// concurrency token, so a stale update fails loudly instead of silently overwriting a
    /// concurrent edit (technical specification §43).
    /// </summary>
    public int Version { get; private set; }

    public static FamilyMember Create(
        Guid tenantId, Guid familyTreeId, Guid? parentId, string name, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("MEMBER_TENANT_REQUIRED", "A family member must belong to a tenant.");
        if (familyTreeId == Guid.Empty)
            throw new DomainException("MEMBER_TREE_REQUIRED", "A family member must belong to a family tree.");

        var member = new FamilyMember
        {
            TenantId = tenantId,
            FamilyTreeId = familyTreeId,
            // Guid.Empty is never a real member id, so treat it as "no parent" rather than
            // letting it reach the database and fail a foreign key at insert time.
            ParentId = parentId == Guid.Empty ? null : parentId,
            Version = 1
        };
        member.Name = ValidateName(name);
        member.InitializeTimestamps(now);
        return member;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        // Validate before mutating: a rejected rename must leave the entity exactly as it was.
        var validated = ValidateName(name);

        Name = validated;
        Version++;
        Touch(now);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("MEMBER_NAME_REQUIRED", "Member name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException("MEMBER_NAME_TOO_LONG", $"Member name exceeds {MaxNameLength} characters.");
        return trimmed;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Domain.Tests -v q`
Expected: PASS — 43 tests (30 existing + 13 new).

- [ ] **Step 5: Commit**

```bash
git add src/FamilyTree.Domain/FamilyMembers/FamilyMember.cs tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberTests.cs
git commit -m "feat: add FamilyMember domain entity with concurrency version"
```

---

## Task 2: Persistence — composite foreign key, query filter, migration

**Files:**
- Create: `src/FamilyTree.Infrastructure/Persistence/Configurations/FamilyMemberConfiguration.cs`
- Modify: `src/FamilyTree.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `tests/FamilyTree.Api.IntegrationTests/Persistence/QueryFilterInvariantTests.cs`
- Create: `tests/FamilyTree.Api.IntegrationTests/Persistence/FamilyMemberConstraintTests.cs`
- Generated: `src/FamilyTree.Infrastructure/Persistence/Migrations/*_AddFamilyMembers.cs`

**Interfaces:**
- Consumes: `FamilyMember` (Task 1), `ApplicationDbContext`, `DatabaseTestBase.ContextFor(Guid tenantId)`, `DatabaseTestBase.Now`.
- Produces: `ApplicationDbContext.FamilyMembers` (`DbSet<FamilyMember>`), the `family_members` table, and the alternate key / composite self-foreign-key pair.

**Why the composite key matters (design spec §3.3):** a plain `parent_id → id` foreign key permits a parent in a different tree. The alternate key `(id, family_tree_id)` plus a two-column self-foreign-key `(parent_id, family_tree_id) → (id, family_tree_id)` makes that link unrepresentable — PostgreSQL rejects it even if every layer above has a bug. PostgreSQL's default `MATCH SIMPLE` semantics mean the constraint is satisfied whenever `parent_id` is NULL, which is exactly the first-generation case.

- [ ] **Step 1: Write the failing constraint tests**

Create `tests/FamilyTree.Api.IntegrationTests/Persistence/FamilyMemberConstraintTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Persistence;

/// <summary>
/// Proves the database itself refuses the states the design forbids. Every test here must
/// fail if the corresponding constraint is removed from FamilyMemberConfiguration.
/// </summary>
public sealed class FamilyMemberConstraintTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
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

    [Fact]
    public async Task A_first_generation_member_persists_with_a_null_parent()
    {
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("alpha");

        await using var context = ContextFor(tenantId);
        context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, null, "سليمان", Now));
        await context.SaveChangesAsync();

        var stored = await context.FamilyMembers.SingleAsync();
        stored.ParentId.Should().BeNull();
        stored.Name.Should().Be("سليمان");
    }

    [Fact]
    public async Task A_child_persists_with_its_parent_in_the_same_tree()
    {
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("beta");

        await using var context = ContextFor(tenantId);
        var parent = FamilyMember.Create(tenantId, treeId, null, "سليمان", Now);
        context.FamilyMembers.Add(parent);
        await context.SaveChangesAsync();

        context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, parent.Id, "فارس", Now));
        await context.SaveChangesAsync();

        var child = await context.FamilyMembers.SingleAsync(m => m.Name == "فارس");
        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task The_database_rejects_a_parent_that_belongs_to_another_tree()
    {
        // The load-bearing test for design spec §3.3. If the composite FK is reduced to a
        // single-column parent_id -> id reference, this insert succeeds and the test fails.
        var (tenantA, treeA) = await SeedTenantWithTreeAsync("gamma");
        var (tenantB, treeB) = await SeedTenantWithTreeAsync("delta");

        Guid foreignParentId;
        await using (var contextB = ContextFor(tenantB))
        {
            var foreignParent = FamilyMember.Create(tenantB, treeB, null, "غريب", Now);
            contextB.FamilyMembers.Add(foreignParent);
            await contextB.SaveChangesAsync();
            foreignParentId = foreignParent.Id;
        }

        await using var contextA = ContextFor(tenantA);
        contextA.FamilyMembers.Add(FamilyMember.Create(tenantA, treeA, foreignParentId, "فارس", Now));

        var act = async () => await contextA.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task The_database_rejects_a_parent_id_that_does_not_exist()
    {
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("epsilon");

        await using var context = ContextFor(tenantId);
        context.FamilyMembers.Add(
            FamilyMember.Create(tenantId, treeId, Guid.CreateVersion7(), "فارس", Now));

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task The_database_refuses_to_delete_a_member_that_still_has_children()
    {
        // OnDelete(Restrict) makes "a parent with children cannot be deleted" a database
        // guarantee, not only a service check.
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("zeta");

        await using var context = ContextFor(tenantId);
        var parent = FamilyMember.Create(tenantId, treeId, null, "سليمان", Now);
        context.FamilyMembers.Add(parent);
        await context.SaveChangesAsync();
        context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, parent.Id, "فارس", Now));
        await context.SaveChangesAsync();

        context.FamilyMembers.Remove(parent);
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Members_of_another_tenant_are_invisible_through_the_query_filter()
    {
        var (tenantA, treeA) = await SeedTenantWithTreeAsync("eta");
        var (tenantB, treeB) = await SeedTenantWithTreeAsync("theta");

        await using (var contextA = ContextFor(tenantA))
        {
            contextA.FamilyMembers.Add(FamilyMember.Create(tenantA, treeA, null, "أ", Now));
            await contextA.SaveChangesAsync();
        }

        await using (var contextB = ContextFor(tenantB))
        {
            contextB.FamilyMembers.Add(FamilyMember.Create(tenantB, treeB, null, "ب", Now));
            await contextB.SaveChangesAsync();
        }

        await using var reader = ContextFor(tenantA);
        var visible = await reader.FamilyMembers.ToListAsync();

        visible.Should().ContainSingle().Which.Name.Should().Be("أ");
    }

    [Fact]
    public async Task An_unauthenticated_context_sees_no_members_at_all()
    {
        var (tenantId, treeId) = await SeedTenantWithTreeAsync("iota");

        await using (var context = ContextFor(tenantId))
        {
            context.FamilyMembers.Add(FamilyMember.Create(tenantId, treeId, null, "أ", Now));
            await context.SaveChangesAsync();
        }

        await using var anonymous = ContextFor(Guid.Empty);

        // Guid.Empty is the unauthenticated tenant: the filter must fail closed.
        (await anonymous.FamilyMembers.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_id_is_looked_up_only_within_the_caller_tenant()
    {
        // The IDOR case: tenant A holds a REAL id belonging to tenant B and still finds nothing.
        var (tenantA, _) = await SeedTenantWithTreeAsync("kappa");
        var (tenantB, treeB) = await SeedTenantWithTreeAsync("lambda");

        Guid foreignId;
        await using (var contextB = ContextFor(tenantB))
        {
            var member = FamilyMember.Create(tenantB, treeB, null, "غريب", Now);
            contextB.FamilyMembers.Add(member);
            await contextB.SaveChangesAsync();
            foreignId = member.Id;
        }

        await using var contextA = ContextFor(tenantA);

        (await contextA.FamilyMembers.FirstOrDefaultAsync(m => m.Id == foreignId)).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberConstraintTests -v q`
Expected: FAIL — compile error, `ApplicationDbContext` has no member `FamilyMembers`.

- [ ] **Step 3: Write the EF configuration**

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/FamilyMemberConfiguration.cs`:

```csharp
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> builder)
    {
        builder.ToTable("family_members");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(FamilyMember.MaxNameLength);

        // Optimistic concurrency (design spec §3.1, technical specification §43). EF puts this
        // column in the UPDATE's WHERE clause; a stale value matches no row and raises
        // DbUpdateConcurrencyException, which the service turns into 409 CONCURRENCY_CONFLICT.
        builder.Property(x => x.Version).IsConcurrencyToken();

        // Design spec §3.3 — the pair of constraints that makes a cross-tree parent link
        // physically unrepresentable. The alternate key is what the composite self-reference
        // below points at; it costs one redundant index.
        builder.HasAlternateKey(x => new { x.Id, x.FamilyTreeId });

        builder.HasOne<FamilyMember>()
               .WithMany()
               .HasForeignKey(x => new { x.ParentId, x.FamilyTreeId })
               .HasPrincipalKey(x => new { x.Id, x.FamilyTreeId })
               // Restrict makes "a member with children cannot be deleted" a database
               // guarantee. The service still checks first so the caller gets a clean 409
               // instead of a DbUpdateException.
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FamilyTreeAggregate>()
               .WithMany()
               .HasForeignKey(x => x.FamilyTreeId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tenant>()
               .WithMany()
               .HasForeignKey(x => x.TenantId)
               .OnDelete(DeleteBehavior.Restrict);

        // Technical specification §12. The (family_tree_id, parent_id) index carries tree
        // traversal — "give me the children of this member" — which is the hot path.
        builder.HasIndex(x => x.FamilyTreeId);
        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => new { x.FamilyTreeId, x.ParentId });
        builder.HasIndex(x => new { x.FamilyTreeId, x.Name });
        builder.HasIndex(x => x.TenantId);
    }
}
```

- [ ] **Step 4: Add the DbSet and the query filter**

In `src/FamilyTree.Infrastructure/Persistence/ApplicationDbContext.cs`:

Add the using directive alongside the existing ones:

```csharp
using FamilyTree.Domain.FamilyMembers;
```

Add the `DbSet` immediately after the `FamilyTrees` one:

```csharp
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
```

Add the filter in `OnModelCreating`, next to the existing filters:

```csharp
        builder.Entity<FamilyMember>().HasQueryFilter(x => x.TenantId == _tenantId);
```

- [ ] **Step 5: Create the migration**

```bash
dotnet ef migrations add AddFamilyMembers \
  --project src/FamilyTree.Infrastructure \
  --startup-project src/FamilyTree.Api \
  --output-dir Persistence/Migrations
```

Open the generated `*_AddFamilyMembers.cs` and confirm it contains **all** of:
- `CreateTable(name: "family_members", ...)` with a `version` integer column
- a unique constraint on `(id, family_tree_id)`
- a foreign key whose `columns` are `["parent_id", "family_tree_id"]` and whose `principalColumns` are `["id", "family_tree_id"]`, with `onDelete: ReferentialAction.Restrict`
- the five indexes

If the composite foreign key is missing or single-column, the configuration in Step 3 is wrong — fix it and regenerate rather than hand-editing the migration.

- [ ] **Step 6: Run the constraint tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberConstraintTests -v q`
Expected: PASS — 8 tests.

- [ ] **Step 7: Harden the query filter invariant test**

`QueryFilterInvariantTests` currently passes if the reflection loop finds nothing at all, so a future mistake in the loop itself would go unnoticed. Now that a fifth tenant-owned entity exists, pin the count too.

In `tests/FamilyTree.Api.IntegrationTests/Persistence/QueryFilterInvariantTests.cs`, replace the body of `Every_tenant_owned_entity_has_a_query_filter`:

```csharp
    [Fact]
    public async Task Every_tenant_owned_entity_has_a_query_filter()
    {
        await using var context = ContextFor(Guid.Empty);
        var model = context.Model;

        var offenders = new StringBuilder();
        var inspected = 0;

        foreach (var entityType in model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (!typeof(ITenantOwned).IsAssignableFrom(clrType)) continue;

            inspected++;

            if (entityType.GetDeclaredQueryFilters().Count == 0)
                offenders.AppendLine($"{clrType.FullName} implements ITenantOwned but has no HasQueryFilter().");
        }

        // Without this assertion the test passes vacuously if the reflection above ever stops
        // matching anything — the failure mode would be silence, exactly when it matters most.
        inspected.Should().BeGreaterThanOrEqualTo(5,
            "FamilyTreeAggregate, Role, RefreshToken, ApplicationUser and FamilyMember are all ITenantOwned");

        offenders.Length.Should().Be(0, offenders.ToString());
    }
```

- [ ] **Step 8: Run the full backend suite**

Run: `dotnet test -v q`
Expected: PASS — 43 domain + 6 application + 43 integration.

- [ ] **Step 9: Commit**

```bash
git add src/FamilyTree.Infrastructure tests/FamilyTree.Api.IntegrationTests
git commit -m "feat: persist family members with a composite cross-tree parent constraint"
```

---

## Task 3: Contracts, error types, and the create/read service

**Files:**
- Modify: `src/FamilyTree.Domain/Common/DomainException.cs`
- Modify: `src/FamilyTree.Api/Errors/ExceptionHandler.cs`
- Create: `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberResponse.cs`
- Create: `src/FamilyTree.Contracts/FamilyMembers/CreateFamilyMemberRequest.cs`
- Create: `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`
- Create: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`
- Modify: `src/FamilyTree.Infrastructure/DependencyInjection.cs`
- Create: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberServiceTests.cs`

**Interfaces:**
- Consumes: `FamilyMember.Create` (Task 1), `ApplicationDbContext.FamilyMembers` (Task 2), `ITenantContext` (`TenantId`, `UserId`), `TimeProvider`.
- Produces:
  - `NotFoundException(string code, string message) : DomainException`
  - `ConflictException(string code, string message) : DomainException`
  - `record FamilyMemberResponse(Guid Id, string Name, Guid? ParentId, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)`
  - `record CreateFamilyMemberRequest(string Name, Guid? ParentId)`
  - `IFamilyMemberService.CreateAsync(CreateFamilyMemberRequest, CancellationToken)` → `FamilyMemberResponse`
  - `IFamilyMemberService.GetAsync(Guid id, CancellationToken)` → `FamilyMemberResponse?`
  - `IFamilyMemberService.ListAsync(CancellationToken)` → `IReadOnlyList<FamilyMemberResponse>`
  - Error codes: `FAMILY_TREE_NOT_FOUND`, `MEMBER_PARENT_NOT_FOUND`

- [ ] **Step 1: Write the failing service tests**

Create `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberServiceTests.cs`:

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
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberServiceTests -v q`
Expected: FAIL — compile error, `FamilyMemberService` does not exist.

- [ ] **Step 3: Add the two exception subclasses**

Replace `src/FamilyTree.Domain/Common/DomainException.cs` with:

```csharp
namespace FamilyTree.Domain.Common;

public class DomainException(string code, string message) : Exception(message)
{
    /// <summary>Stable machine-readable code. Surfaces in Problem Details; clients translate from it.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// The requested entity does not exist *for this caller*. Deliberately indistinguishable from
/// "belongs to another tenant" — a 403 would confirm the identifier exists (design spec §4.4).
/// </summary>
public sealed class NotFoundException(string code, string message) : DomainException(code, message);

/// <summary>A rule that depends on current state, not on the request: 409 rather than 400.</summary>
public sealed class ConflictException(string code, string message) : DomainException(code, message);
```

- [ ] **Step 4: Map the new exception types to status codes**

In `src/FamilyTree.Api/Errors/ExceptionHandler.cs`, replace everything from the type check down to the end of the `ProblemDetails` initializer:

```csharp
        if (exception is not DomainException domainException) return false;

        // The subclass decides the status; a bare DomainException is a request-level rule
        // violation and stays a 400.
        var (status, title) = domainException switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Request conflicts with the current state"),
            _ => (StatusCodes.Status400BadRequest, "Request violates a business rule")
        };

        logger.LogWarning("Domain rule violated: {Code}", domainException.Code);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // A 404 must not describe what was missing — the response stays identical whether
            // the id is unknown or owned by another tenant (design spec §4.4).
            Detail = status == StatusCodes.Status404NotFound ? null : domainException.Message,
            Extensions = { ["code"] = domainException.Code }
        };
```

The remainder of the method (`httpContext.Response.StatusCode = problem.Status.Value;` and `WriteAsJsonAsync`) is unchanged.

- [ ] **Step 5: Write the contracts**

Create `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberResponse.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// A single member as returned by the API. <paramref name="Version"/> must be echoed back on
/// update — it is the optimistic concurrency token (design spec §3.1).
/// </summary>
public sealed record FamilyMemberResponse(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```

Create `src/FamilyTree.Contracts/FamilyMembers/CreateFamilyMemberRequest.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// Creates a member. <paramref name="ParentId"/> null means a first-generation member directly
/// under the root family (technical specification §10). The tenant and the family tree are
/// resolved server-side and are never accepted from the client.
/// </summary>
public sealed record CreateFamilyMemberRequest(string Name, Guid? ParentId);
```

- [ ] **Step 6: Write the service interface**

Create `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`:

```csharp
using FamilyTree.Contracts.FamilyMembers;

namespace FamilyTree.Application.FamilyMembers;

public interface IFamilyMemberService
{
    Task<FamilyMemberResponse> CreateAsync(CreateFamilyMemberRequest request, CancellationToken ct = default);

    /// <summary>Returns null when no such member is visible to the caller's tenant.</summary>
    Task<FamilyMemberResponse?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<FamilyMemberResponse>> ListAsync(CancellationToken ct = default);
}
```

- [ ] **Step 7: Write the service**

Create `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`:

```csharp
using FamilyTree.Application.Common;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.FamilyMembers;

/// <summary>
/// Every query here runs through the tenant query filter, so "not found" and "belongs to
/// another tenant" are the same code path — which is what makes the uniform 404 in design
/// spec §4.4 true by construction rather than by discipline.
/// </summary>
public sealed class FamilyMemberService(
    ApplicationDbContext context,
    ITenantContext tenant,
    TimeProvider timeProvider) : IFamilyMemberService
{
    public async Task<FamilyMemberResponse> CreateAsync(
        CreateFamilyMemberRequest request, CancellationToken ct = default)
    {
        var tree = await context.FamilyTrees.FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("FAMILY_TREE_NOT_FOUND", "This tenant has no family tree.");

        if (request.ParentId is { } parentId && parentId != Guid.Empty)
        {
            // Filtered lookup: a parent in another tenant is simply not there.
            var parentExists = await context.FamilyMembers
                .AnyAsync(m => m.Id == parentId && m.FamilyTreeId == tree.Id, ct);

            if (!parentExists)
                throw new DomainException("MEMBER_PARENT_NOT_FOUND", "The specified parent does not exist.");
        }

        var member = FamilyMember.Create(
            tenant.TenantId, tree.Id, request.ParentId, request.Name, timeProvider.GetUtcNow());

        context.FamilyMembers.Add(member);
        await context.SaveChangesAsync(ct);

        return Map(member);
    }

    public async Task<FamilyMemberResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var member = await context.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        return member is null ? null : Map(member);
    }

    public async Task<IReadOnlyList<FamilyMemberResponse>> ListAsync(CancellationToken ct = default)
    {
        var members = await context.FamilyMembers
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

        return members.Select(Map).ToList();
    }

    internal static FamilyMemberResponse Map(FamilyMember member) => new(
        member.Id, member.Name, member.ParentId, member.Version, member.CreatedAt, member.UpdatedAt);
}
```

- [ ] **Step 8: Register the service**

In `src/FamilyTree.Infrastructure/DependencyInjection.cs`, add the using directives and the registration alongside the existing scoped services:

```csharp
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Infrastructure.FamilyMembers;
```

```csharp
        services.AddScoped<IFamilyMemberService, FamilyMemberService>();
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberServiceTests -v q`
Expected: PASS — 8 tests.

- [ ] **Step 10: Commit**

```bash
git add src tests
git commit -m "feat: add family member creation and reads with 404-safe lookups"
```

---

## Task 4: Update with optimistic concurrency

**Files:**
- Create: `src/FamilyTree.Contracts/FamilyMembers/UpdateFamilyMemberRequest.cs`
- Modify: `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`
- Modify: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberServiceTests.cs`

**Interfaces:**
- Consumes: everything from Task 3.
- Produces:
  - `record UpdateFamilyMemberRequest(string Name, int Version, Guid? ParentId = null, Guid? TenantId = null, Guid? FamilyTreeId = null)`
  - `IFamilyMemberService.UpdateAsync(Guid id, UpdateFamilyMemberRequest, CancellationToken)` → `FamilyMemberResponse`
  - Error codes: `MEMBER_NOT_FOUND`, `MEMBER_FIELD_NOT_UPDATABLE`, `CONCURRENCY_CONFLICT`

**The one thing that can silently fail here.** EF compares the concurrency token against the value it read from the database, not against the value the client held. If the service does not overwrite `OriginalValue` with the client's version, the `WHERE` clause always matches and *no conflict is ever detected* — every test that only checks "update works" still passes while concurrency control is a no-op. Step 5 is the line that makes it real, and Step 1's stale-update test is the one that proves it. Step 7 verifies the test would actually catch its absence.

- [ ] **Step 1: Write the failing tests**

Append inside the `FamilyMemberServiceTests` class:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberServiceTests -v q`
Expected: FAIL — compile error, `UpdateFamilyMemberRequest` does not exist.

- [ ] **Step 3: Write the request contract**

Create `src/FamilyTree.Contracts/FamilyMembers/UpdateFamilyMemberRequest.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// Renames a member. <paramref name="Version"/> is the value from the last read and is
/// required — omitting it is a stale write by definition.
///
/// The three trailing properties exist ONLY so the API can reject them explicitly. Design
/// spec §4.6 requires that an attempt to change parentId, tenantId, or familyTreeId fail
/// loudly rather than be silently dropped; a client that believed it had re-parented a member
/// would corrupt the operator's mental model of the tree. Re-parenting is the dedicated move
/// command in Phase 5.
/// </summary>
public sealed record UpdateFamilyMemberRequest(
    string Name,
    int Version,
    Guid? ParentId = null,
    Guid? TenantId = null,
    Guid? FamilyTreeId = null);
```

- [ ] **Step 4: Extend the interface**

In `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`, add:

```csharp
    Task<FamilyMemberResponse> UpdateAsync(
        Guid id, UpdateFamilyMemberRequest request, CancellationToken ct = default);
```

- [ ] **Step 5: Implement the update**

Add to `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`:

```csharp
    public async Task<FamilyMemberResponse> UpdateAsync(
        Guid id, UpdateFamilyMemberRequest request, CancellationToken ct = default)
    {
        if (request.ParentId is not null || request.TenantId is not null || request.FamilyTreeId is not null)
            throw new DomainException(
                "MEMBER_FIELD_NOT_UPDATABLE",
                "Parent, tenant, and family tree cannot be changed through this endpoint.");

        // Tracked (not AsNoTracking): SaveChanges needs the entity in the change tracker.
        var member = await context.FamilyMembers.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        member.Rename(request.Name, timeProvider.GetUtcNow());

        // Load-bearing. EF builds `UPDATE ... WHERE id = @id AND version = @original`, and
        // `@original` defaults to the value it just READ — which always matches, making the
        // concurrency token inert. Substituting the version the CLIENT held is what turns a
        // stale write into a conflict instead of a silent overwrite.
        context.Entry(member).Property(m => m.Version).OriginalValue = request.Version;

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "CONCURRENCY_CONFLICT", "This member was changed by someone else. Reload and try again.");
        }

        return Map(member);
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberServiceTests -v q`
Expected: PASS — 16 tests.

- [ ] **Step 7: Verify the concurrency check is genuinely load-bearing**

Temporarily comment out the `OriginalValue` line from Step 5 and re-run:

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~UpdateAsync_rejects_a_stale_update -v q`
Expected: **FAIL.** If it still passes, the test proves nothing — stop and fix the test before restoring the line.

Restore the line and re-run to confirm PASS.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat: add member rename with optimistic concurrency and immutable-field rejection"
```

---

## Task 5: Delete, blocked by children

**Files:**
- Modify: `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`
- Modify: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberServiceTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3–4.
- Produces: `IFamilyMemberService.DeleteAsync(Guid id, CancellationToken)`; error codes `MEMBER_NOT_FOUND`, `MEMBER_HAS_CHILDREN`.

Deletion is a hard delete (design spec §1.1). The database already refuses to orphan children via `OnDelete(Restrict)` (Task 2); the service checks first so the caller gets a clean 409 with a stable code instead of a raw `DbUpdateException` (technical specification §26).

- [ ] **Step 1: Write the failing tests**

Append inside the `FamilyMemberServiceTests` class:

```csharp
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

        (await service.ListAsync(default)).Should().BeEmpty();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberServiceTests -v q`
Expected: FAIL — compile error, `IFamilyMemberService` has no `DeleteAsync`.

- [ ] **Step 3: Extend the interface**

In `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`, add:

```csharp
    Task DeleteAsync(Guid id, CancellationToken ct = default);
```

- [ ] **Step 4: Implement the delete**

Add to `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`:

```csharp
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var member = await context.FamilyMembers.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        // The FK's OnDelete(Restrict) would also stop this, but a DbUpdateException carries no
        // stable code for the client. Checking first is what makes the 409 contractual
        // (technical specification §26).
        var hasChildren = await context.FamilyMembers.AnyAsync(m => m.ParentId == id, ct);
        if (hasChildren)
            throw new ConflictException(
                "MEMBER_HAS_CHILDREN", "This member cannot be deleted because they have children.");

        context.FamilyMembers.Remove(member);
        await context.SaveChangesAsync(ct);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberServiceTests -v q`
Expected: PASS — 21 tests.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add member deletion blocked by existing children"
```

---

## Task 6: Family member endpoints

**Files:**
- Create: `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs`
- Modify: `src/FamilyTree.Api/Program.cs`
- Create: `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs`

**Interfaces:**
- Consumes: `IFamilyMemberService` (Tasks 3–5), `RequirePermission` (`FamilyTree.Api.Authorization.EndpointExtensions`), `Permissions.Member.*`.
- Produces: `MapFamilyMemberEndpoints(this IEndpointRouteBuilder)` and the routes `POST /api/v1/family-members`, `GET /api/v1/family-members`, `GET /api/v1/family-members/{id:guid}`, `PUT /api/v1/family-members/{id:guid}`, `DELETE /api/v1/family-members/{id:guid}`.

- [ ] **Step 1: Write the failing endpoint tests**

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class FamilyMemberEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private async Task<FamilyMemberResponse> CreateAsync(string name, Guid? parentId = null)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest(name, parentId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem is not null && problem.TryGetValue("code", out var code) ? code.ToString() : null;
    }

    [Theory]
    [InlineData("GET", "/api/v1/family-members")]
    [InlineData("POST", "/api/v1/family-members")]
    public async Task Endpoints_require_authentication(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new CreateFamilyMemberRequest("فارس", null));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_creates_a_first_generation_member_and_returns_its_location()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest("سليمان", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        created.Name.Should().Be("سليمان");
        created.ParentId.Should().BeNull();
        response.Headers.Location!.ToString().Should().EndWith($"/api/v1/family-members/{created.Id}");
    }

    [Fact]
    public async Task Post_creates_a_descendant_under_an_existing_parent()
    {
        await AuthenticateAsync();
        var parent = await CreateAsync("سليمان");

        var child = await CreateAsync("فارس", parent.Id);

        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task Post_rejects_a_blank_name_with_a_stable_code()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest("   ", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("MEMBER_NAME_REQUIRED");
    }

    [Fact]
    public async Task Post_rejects_an_unknown_parent()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest("فارس", Guid.CreateVersion7()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("MEMBER_PARENT_NOT_FOUND");
    }

    [Fact]
    public async Task Get_returns_the_member()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("سليمان");

        var response = await _client.GetAsync($"/api/v1/family-members/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!.Name.Should().Be("سليمان");
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_id()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync($"/api/v1/family-members/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_list_returns_every_member_of_the_tenant()
    {
        await AuthenticateAsync();
        await CreateAsync("سليمان");
        await CreateAsync("عمر");

        var members = await _client.GetFromJsonAsync<List<FamilyMemberResponse>>("/api/v1/family-members");

        members.Should().HaveCount(2);
    }

    [Fact]
    public async Task Put_renames_a_member()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("فارس");

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest("فارس أحمد", created.Version));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        updated.Name.Should().Be("فارس أحمد");
        updated.Version.Should().Be(created.Version + 1);
    }

    [Fact]
    public async Task Put_returns_409_for_a_stale_version()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("أحمد");
        await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest("أحمد علي", created.Version));

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest("أحمد محمد", created.Version));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task Put_rejects_an_attempt_to_change_the_parent()
    {
        await AuthenticateAsync();
        var parent = await CreateAsync("سليمان");
        var child = await CreateAsync("فارس");

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{child.Id}",
            new UpdateFamilyMemberRequest("فارس", child.Version, ParentId: parent.Id));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("MEMBER_FIELD_NOT_UPDATABLE");
    }

    [Fact]
    public async Task Put_returns_404_for_an_unknown_id()
    {
        await AuthenticateAsync();

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{Guid.CreateVersion7()}",
            new UpdateFamilyMemberRequest("فارس", 1));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_removes_a_leaf_member()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("فارس");

        var response = await _client.DeleteAsync($"/api/v1/family-members/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/v1/family-members/{created.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_returns_409_when_the_member_has_children()
    {
        await AuthenticateAsync();
        var parent = await CreateAsync("سليمان");
        await CreateAsync("فارس", parent.Id);

        var response = await _client.DeleteAsync($"/api/v1/family-members/{parent.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("MEMBER_HAS_CHILDREN");
    }

    [Fact]
    public async Task Delete_returns_404_for_an_unknown_id()
    {
        await AuthenticateAsync();

        var response = await _client.DeleteAsync($"/api/v1/family-members/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_404_body_carries_no_information_about_which_id_was_missing()
    {
        // Design spec §4.4 — two different unknown ids must produce byte-identical bodies,
        // so a caller cannot probe for the existence of another tenant's member.
        await AuthenticateAsync();

        var first = await _client.GetAsync($"/api/v1/family-members/{Guid.CreateVersion7()}");
        var second = await _client.GetAsync($"/api/v1/family-members/{Guid.CreateVersion7()}");

        first.StatusCode.Should().Be(second.StatusCode);
        (await first.Content.ReadAsStringAsync()).Should().Be(await second.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberEndpointsTests -v q`
Expected: FAIL — every authenticated call returns 404 because the routes do not exist.

- [ ] **Step 3: Write the endpoints**

Create `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs`:

```csharp
using FamilyTree.Api.Authorization;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.FamilyMembers;

public static class FamilyMemberEndpoints
{
    public static IEndpointRouteBuilder MapFamilyMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/family-members").WithTags("FamilyMembers");

        group.MapGet("/", async (IFamilyMemberService members, CancellationToken ct) =>
            Results.Ok(await members.ListAsync(ct)))
            .RequirePermission(Permissions.Member.View);

        group.MapGet("/{id:guid}", async (Guid id, IFamilyMemberService members, CancellationToken ct) =>
        {
            var member = await members.GetAsync(id, ct);
            // Null covers both "no such member" and "belongs to another tenant" — the query
            // filter has already made them the same thing (design spec §4.4).
            return member is null ? Results.NotFound() : Results.Ok(member);
        })
            .RequirePermission(Permissions.Member.View);

        group.MapPost("/", async (
            CreateFamilyMemberRequest request, IFamilyMemberService members, CancellationToken ct) =>
        {
            var created = await members.CreateAsync(request, ct);
            return Results.Created($"/api/v1/family-members/{created.Id}", created);
        })
            .RequirePermission(Permissions.Member.Create);

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateFamilyMemberRequest request, IFamilyMemberService members, CancellationToken ct) =>
            Results.Ok(await members.UpdateAsync(id, request, ct)))
            .RequirePermission(Permissions.Member.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id, IFamilyMemberService members, CancellationToken ct) =>
        {
            await members.DeleteAsync(id, ct);
            return Results.NoContent();
        })
            .RequirePermission(Permissions.Member.Delete);

        return app;
    }
}
```

Rule violations propagate as `DomainException` subclasses and are turned into Problem Details by `DomainExceptionHandler` (Task 3, Step 4) — endpoints deliberately contain no try/catch.

- [ ] **Step 4: Map the group**

In `src/FamilyTree.Api/Program.cs`, add the using directive:

```csharp
using FamilyTree.Api.Endpoints.FamilyMembers;
```

and map the group next to the existing ones:

```csharp
app.MapFamilyMemberEndpoints();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberEndpointsTests -v q`
Expected: PASS — 17 tests.

- [ ] **Step 6: Run the full backend suite**

Run: `dotnet test -v q`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat: expose family member CRUD endpoints behind permission policies"
```

---

## Task 7: Tree assembly

**Files:**
- Create: `src/FamilyTree.Contracts/FamilyTrees/FamilyTreeViewResponse.cs`
- Create: `src/FamilyTree.Application/FamilyTrees/FamilyTreeAssembler.cs`
- Create: `tests/FamilyTree.Application.Tests/FamilyTrees/FamilyTreeAssemblerTests.cs`

**Interfaces:**
- Consumes: `FamilyMember` (Task 1).
- Produces:
  - `record FamilyTreeNodeResponse(Guid Id, string Name, Guid? ParentId, int Generation, bool HasMoreChildren, IReadOnlyList<FamilyTreeNodeResponse> Children)`
  - `record FamilyTreeViewResponse(Guid Id, string Name, IReadOnlyList<FamilyTreeNodeResponse> RootMembers)`
  - `static IReadOnlyList<FamilyTreeNodeResponse> FamilyTreeAssembler.Assemble(IReadOnlyList<FamilyMember> members, Guid? rootId, int? maxDepth)`

Pure logic, no database: the whole point is that generation arithmetic and depth truncation are unit-testable in milliseconds. Generation counts from **1** for members with no parent (SRS §10). When `rootId` is supplied the subtree root keeps its true generation, so a caller fetching a subtree still knows how deep it sits.

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Application.Tests/FamilyTrees/FamilyTreeAssemblerTests.cs`:

```csharp
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.FamilyTrees;

public class FamilyTreeAssemblerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(string name, Guid? parentId = null) =>
        FamilyMember.Create(TenantId, TreeId, parentId, name, Now);

    /// <summary>سليمان → فارس → محمود, plus a second first-generation member عمر.</summary>
    private static (FamilyMember Suleiman, FamilyMember Faris, FamilyMember Mahmoud, FamilyMember Omar)
        ThreeGenerations()
    {
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);
        var omar = Member("عمر");
        return (suleiman, faris, mahmoud, omar);
    }

    [Fact]
    public void Assemble_returns_an_empty_list_for_an_empty_tree()
    {
        FamilyTreeAssembler.Assemble([], null, null).Should().BeEmpty();
    }

    [Fact]
    public void Assemble_puts_parentless_members_at_the_top_level()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], null, null);

        roots.Select(n => n.Name).Should().BeEquivalentTo(["سليمان", "عمر"]);
    }

    [Fact]
    public void Assemble_nests_children_under_their_parent()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], null, null);

        var suleimanNode = roots.Single(n => n.Name == "سليمان");
        var farisNode = suleimanNode.Children.Should().ContainSingle().Subject;
        farisNode.Name.Should().Be("فارس");
        farisNode.Children.Should().ContainSingle().Which.Name.Should().Be("محمود");
    }

    [Fact]
    public void Assemble_numbers_the_first_generation_from_one()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], null, null);

        var suleimanNode = roots.Single(n => n.Name == "سليمان");
        suleimanNode.Generation.Should().Be(1);
        suleimanNode.Children[0].Generation.Should().Be(2);
        suleimanNode.Children[0].Children[0].Generation.Should().Be(3);
    }

    [Fact]
    public void Assemble_orders_siblings_by_name()
    {
        var suleiman = Member("سليمان");
        var zayd = Member("زيد", suleiman.Id);
        var ahmad = Member("أحمد", suleiman.Id);

        var roots = FamilyTreeAssembler.Assemble([suleiman, zayd, ahmad], null, null);

        roots[0].Children.Select(c => c.Name).Should().ContainInOrder("أحمد", "زيد");
    }

    [Fact]
    public void Assemble_returns_only_the_requested_subtree_when_a_root_id_is_given()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], faris.Id, null);

        var only = roots.Should().ContainSingle().Subject;
        only.Name.Should().Be("فارس");
        only.Children.Should().ContainSingle().Which.Name.Should().Be("محمود");
    }

    [Fact]
    public void Assemble_keeps_the_true_generation_of_a_subtree_root()
    {
        // A caller who fetched a subtree still needs to know how deep it sits in the family.
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], faris.Id, null);

        roots[0].Generation.Should().Be(2);
        roots[0].Children[0].Generation.Should().Be(3);
    }

    [Fact]
    public void Assemble_returns_nothing_for_an_unknown_root_id()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], Guid.CreateVersion7(), null)
            .Should().BeEmpty();
    }

    [Fact]
    public void Assemble_truncates_at_max_depth_and_flags_the_cut()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], null, 2);

        var suleimanNode = roots.Single(n => n.Name == "سليمان");
        suleimanNode.HasMoreChildren.Should().BeFalse();

        var farisNode = suleimanNode.Children.Should().ContainSingle().Subject;
        farisNode.Children.Should().BeEmpty("depth 2 is the last level returned");
        farisNode.HasMoreChildren.Should().BeTrue("محمود exists but was not returned");
    }

    [Fact]
    public void Assemble_does_not_flag_a_childless_leaf_at_the_depth_limit()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], null, 1);

        roots.Single(n => n.Name == "عمر").HasMoreChildren.Should()
             .BeFalse("عمر has no children at all, truncated or otherwise");
        roots.Single(n => n.Name == "سليمان").HasMoreChildren.Should().BeTrue();
    }

    [Fact]
    public void Assemble_treats_a_max_depth_of_one_as_the_top_level_only()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], null, 1);

        roots.Should().HaveCount(2);
        roots.Should().OnlyContain(n => n.Children.Count == 0);
    }

    [Fact]
    public void Assemble_ignores_a_max_depth_below_one()
    {
        // A zero or negative depth is a client error that must not silently return an empty
        // tree, which would look like "this family has no members".
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], null, 0);

        roots.Should().HaveCount(2);
        roots.Single(n => n.Name == "سليمان").Children.Should().ContainSingle();
    }

    [Fact]
    public void Assemble_drops_members_whose_parent_is_absent_from_the_input()
    {
        // Defensive: a partial fetch must never promote a descendant to first generation,
        // which would misrepresent the family.
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);

        var roots = FamilyTreeAssembler.Assemble([suleiman, mahmoud], null, null);

        roots.Should().ContainSingle().Which.Name.Should().Be("سليمان");
        roots[0].Children.Should().BeEmpty();
    }

    [Fact]
    public void Assemble_handles_a_wide_generation()
    {
        var suleiman = Member("سليمان");
        var children = Enumerable.Range(0, 500)
            .Select(i => Member($"ابن {i:D3}", suleiman.Id))
            .ToList();

        var roots = FamilyTreeAssembler.Assemble([suleiman, .. children], null, null);

        roots.Should().ContainSingle().Which.Children.Should().HaveCount(500);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Application.Tests -v q`
Expected: FAIL — compile error, `FamilyTreeAssembler` does not exist.

- [ ] **Step 3: Write the view contracts**

Create `src/FamilyTree.Contracts/FamilyTrees/FamilyTreeViewResponse.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyTrees;

/// <summary>
/// One node of the nested tree. <paramref name="Generation"/> is computed during assembly and
/// never stored (design spec §3.6, SRS §32). <paramref name="HasMoreChildren"/> is true when
/// this node has children that were not returned because of a depth limit — the flag is what
/// lets the client show an expander without guessing (design spec §4.5).
/// </summary>
public sealed record FamilyTreeNodeResponse(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Generation,
    bool HasMoreChildren,
    IReadOnlyList<FamilyTreeNodeResponse> Children);

/// <summary>
/// The root family plus its first-generation members. The root family is the tree itself, not
/// a member (technical specification §10, BR-003).
/// </summary>
public sealed record FamilyTreeViewResponse(
    Guid Id,
    string Name,
    IReadOnlyList<FamilyTreeNodeResponse> RootMembers);
```

- [ ] **Step 4: Write the assembler**

Create `src/FamilyTree.Application/FamilyTrees/FamilyTreeAssembler.cs`:

```csharp
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.FamilyTrees;

/// <summary>
/// Turns a flat member list into the nested view DTO. Pure and synchronous on purpose: tree
/// shaping and generation arithmetic are the parts most likely to be wrong, and keeping them
/// free of EF makes them testable in milliseconds (design spec §6).
/// </summary>
public static class FamilyTreeAssembler
{
    public static IReadOnlyList<FamilyTreeNodeResponse> Assemble(
        IReadOnlyList<FamilyMember> members, Guid? rootId, int? maxDepth)
    {
        // One pass to index children by parent; the build below is then linear in the input.
        var childrenByParent = members
            .Where(m => m.ParentId is not null)
            .GroupBy(m => m.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Name, StringComparer.Ordinal).ToList());

        // A depth of zero or less is meaningless; treat it as "no limit" rather than returning
        // an empty tree, which a client would render as "this family has no members".
        var effectiveDepth = maxDepth is > 0 ? maxDepth : null;

        if (rootId is { } id)
        {
            var subtreeRoot = members.FirstOrDefault(m => m.Id == id);
            if (subtreeRoot is null) return [];

            // The subtree root keeps its real generation, so the caller still knows how deep
            // this fragment sits in the family.
            var generation = GenerationOf(subtreeRoot, members);
            return [Build(subtreeRoot, generation, 1, effectiveDepth, childrenByParent)];
        }

        return members
            .Where(m => m.ParentId is null)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => Build(m, 1, 1, effectiveDepth, childrenByParent))
            .ToList();
    }

    private static FamilyTreeNodeResponse Build(
        FamilyMember member,
        int generation,
        int level,
        int? maxDepth,
        IReadOnlyDictionary<Guid, List<FamilyMember>> childrenByParent)
    {
        var hasChildren = childrenByParent.TryGetValue(member.Id, out var children);

        if (maxDepth is { } limit && level >= limit)
            return new FamilyTreeNodeResponse(
                member.Id, member.Name, member.ParentId, generation, hasChildren, []);

        var built = hasChildren
            ? children!.Select(c => Build(c, generation + 1, level + 1, maxDepth, childrenByParent)).ToList()
            : [];

        return new FamilyTreeNodeResponse(
            member.Id, member.Name, member.ParentId, generation, false, built);
    }

    /// <summary>
    /// Walks upward to find how deep a member sits. Bounded by the input size so a malformed
    /// parent chain cannot loop forever — cycles are impossible by construction until the
    /// Phase 5 move command exists, and that command validates them with a recursive CTE.
    /// </summary>
    private static int GenerationOf(FamilyMember member, IReadOnlyList<FamilyMember> members)
    {
        var byId = members.ToDictionary(m => m.Id);
        var generation = 1;
        var current = member;

        while (current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
        {
            generation++;
            current = parent;
            if (generation > members.Count) break;
        }

        return generation;
    }
}
```

Note the orphan case: `Build` descends only through `childrenByParent`, and the top-level query takes only `ParentId is null`, so a member whose parent is absent from the input never appears — it is neither promoted to the first generation nor silently attached elsewhere.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests -v q`
Expected: PASS — 20 tests (6 existing + 14 new).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add pure tree assembler with computed generations and depth truncation"
```

---

## Task 8: Family tree endpoints

**Files:**
- Create: `src/FamilyTree.Contracts/FamilyTrees/FamilyTreeResponse.cs`
- Create: `src/FamilyTree.Contracts/FamilyTrees/RenameFamilyTreeRequest.cs`
- Create: `src/FamilyTree.Application/FamilyTrees/IFamilyTreeService.cs`
- Create: `src/FamilyTree.Infrastructure/FamilyTrees/FamilyTreeService.cs`
- Modify: `src/FamilyTree.Infrastructure/DependencyInjection.cs`
- Create: `src/FamilyTree.Api/Endpoints/FamilyTrees/FamilyTreeEndpoints.cs`
- Modify: `src/FamilyTree.Api/Program.cs`
- Create: `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyTreeEndpointsTests.cs`

**Interfaces:**
- Consumes: `FamilyTreeAssembler.Assemble` (Task 7), `FamilyTreeAggregate.Rename`, `ApplicationDbContext`, `TimeProvider`.
- Produces:
  - `record FamilyTreeResponse(Guid Id, string Name, int MemberCount)`
  - `record RenameFamilyTreeRequest(string Name)`
  - `IFamilyTreeService.GetAsync(CancellationToken)` → `FamilyTreeResponse`
  - `IFamilyTreeService.RenameAsync(RenameFamilyTreeRequest, CancellationToken)` → `FamilyTreeResponse`
  - `IFamilyTreeService.GetViewAsync(Guid? rootId, int? maxDepth, CancellationToken)` → `FamilyTreeViewResponse`
  - Routes `GET /api/v1/family-tree`, `PUT /api/v1/family-tree`, `GET /api/v1/family-tree/view`

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyTreeEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class FamilyTreeEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private async Task<FamilyMemberResponse> CreateAsync(string name, Guid? parentId = null)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest(name, parentId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
    }

    [Fact]
    public async Task Get_requires_authentication()
    {
        (await _client.GetAsync("/api/v1/family-tree")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task View_requires_authentication()
    {
        (await _client.GetAsync("/api/v1/family-tree/view")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_returns_the_seeded_root_family()
    {
        await AuthenticateAsync();

        var tree = await _client.GetFromJsonAsync<FamilyTreeResponse>("/api/v1/family-tree");

        tree!.Name.Should().Be("عائلة السقا");
        tree.MemberCount.Should().Be(0);
    }

    [Fact]
    public async Task Get_counts_the_members()
    {
        await AuthenticateAsync();
        await CreateAsync("سليمان");
        await CreateAsync("عمر");

        var tree = await _client.GetFromJsonAsync<FamilyTreeResponse>("/api/v1/family-tree");

        tree!.MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task Put_renames_the_root_family()
    {
        await AuthenticateAsync();

        var response = await _client.PutAsJsonAsync(
            "/api/v1/family-tree", new RenameFamilyTreeRequest("عائلة السقا الكرام"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<FamilyTreeResponse>())!
            .Name.Should().Be("عائلة السقا الكرام");
    }

    [Fact]
    public async Task Put_rejects_a_blank_name()
    {
        await AuthenticateAsync();

        var response = await _client.PutAsJsonAsync(
            "/api/v1/family-tree", new RenameFamilyTreeRequest("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task View_returns_the_root_family_with_no_members_when_the_tree_is_empty()
    {
        await AuthenticateAsync();

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>("/api/v1/family-tree/view");

        view!.Name.Should().Be("عائلة السقا");
        view.RootMembers.Should().BeEmpty();
    }

    [Fact]
    public async Task View_returns_the_nested_hierarchy_with_generations()
    {
        await AuthenticateAsync();
        var suleiman = await CreateAsync("سليمان");
        var faris = await CreateAsync("فارس", suleiman.Id);
        await CreateAsync("محمود", faris.Id);

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>("/api/v1/family-tree/view");

        var root = view!.RootMembers.Should().ContainSingle().Subject;
        root.Name.Should().Be("سليمان");
        root.Generation.Should().Be(1);
        root.Children[0].Name.Should().Be("فارس");
        root.Children[0].Generation.Should().Be(2);
        root.Children[0].Children[0].Name.Should().Be("محمود");
        root.Children[0].Children[0].Generation.Should().Be(3);
    }

    [Fact]
    public async Task View_honours_max_depth_and_flags_truncated_nodes()
    {
        await AuthenticateAsync();
        var suleiman = await CreateAsync("سليمان");
        var faris = await CreateAsync("فارس", suleiman.Id);
        await CreateAsync("محمود", faris.Id);

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>(
            "/api/v1/family-tree/view?maxDepth=2");

        var farisNode = view!.RootMembers[0].Children.Should().ContainSingle().Subject;
        farisNode.Children.Should().BeEmpty();
        farisNode.HasMoreChildren.Should().BeTrue();
    }

    [Fact]
    public async Task View_honours_root_id()
    {
        await AuthenticateAsync();
        var suleiman = await CreateAsync("سليمان");
        var faris = await CreateAsync("فارس", suleiman.Id);
        await CreateAsync("محمود", faris.Id);

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>(
            $"/api/v1/family-tree/view?rootId={faris.Id}");

        var root = view!.RootMembers.Should().ContainSingle().Subject;
        root.Name.Should().Be("فارس");
        root.Generation.Should().Be(2);
    }

    [Fact]
    public async Task View_returns_an_empty_member_list_for_an_unknown_root_id()
    {
        await AuthenticateAsync();
        await CreateAsync("سليمان");

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>(
            $"/api/v1/family-tree/view?rootId={Guid.CreateVersion7()}");

        view!.RootMembers.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyTreeEndpointsTests -v q`
Expected: FAIL — compile error, `FamilyTreeResponse` does not exist.

- [ ] **Step 3: Write the contracts**

Create `src/FamilyTree.Contracts/FamilyTrees/FamilyTreeResponse.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyTrees;

public sealed record FamilyTreeResponse(Guid Id, string Name, int MemberCount);
```

Create `src/FamilyTree.Contracts/FamilyTrees/RenameFamilyTreeRequest.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyTrees;

public sealed record RenameFamilyTreeRequest(string Name);
```

- [ ] **Step 4: Write the service interface**

Create `src/FamilyTree.Application/FamilyTrees/IFamilyTreeService.cs`:

```csharp
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.FamilyTrees;

public interface IFamilyTreeService
{
    Task<FamilyTreeResponse> GetAsync(CancellationToken ct = default);

    Task<FamilyTreeResponse> RenameAsync(RenameFamilyTreeRequest request, CancellationToken ct = default);

    /// <summary>
    /// The whole tree by default. <paramref name="rootId"/> and <paramref name="maxDepth"/>
    /// exist from the start so the growth path to incremental loading is real rather than
    /// aspirational (design spec §4.5).
    /// </summary>
    Task<FamilyTreeViewResponse> GetViewAsync(
        Guid? rootId, int? maxDepth, CancellationToken ct = default);
}
```

- [ ] **Step 5: Write the service**

Create `src/FamilyTree.Infrastructure/FamilyTrees/FamilyTreeService.cs`:

```csharp
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.FamilyTrees;

public sealed class FamilyTreeService(
    ApplicationDbContext context,
    TimeProvider timeProvider) : IFamilyTreeService
{
    public async Task<FamilyTreeResponse> GetAsync(CancellationToken ct = default)
    {
        var tree = await LoadTreeAsync(tracked: false, ct);
        var memberCount = await context.FamilyMembers.CountAsync(ct);

        return new FamilyTreeResponse(tree.Id, tree.Name, memberCount);
    }

    public async Task<FamilyTreeResponse> RenameAsync(
        RenameFamilyTreeRequest request, CancellationToken ct = default)
    {
        var tree = await LoadTreeAsync(tracked: true, ct);

        tree.Rename(request.Name, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(ct);

        var memberCount = await context.FamilyMembers.CountAsync(ct);
        return new FamilyTreeResponse(tree.Id, tree.Name, memberCount);
    }

    public async Task<FamilyTreeViewResponse> GetViewAsync(
        Guid? rootId, int? maxDepth, CancellationToken ct = default)
    {
        var tree = await LoadTreeAsync(tracked: false, ct);

        // V1 loads the whole tree and shapes it in memory (design spec §4.5). The parameters
        // are honoured server-side so switching to a windowed query later changes only this
        // method, never the contract.
        var members = await context.FamilyMembers.AsNoTracking().ToListAsync(ct);

        return new FamilyTreeViewResponse(
            tree.Id, tree.Name, FamilyTreeAssembler.Assemble(members, rootId, maxDepth));
    }

    private async Task<FamilyTreeAggregate> LoadTreeAsync(bool tracked, CancellationToken ct)
    {
        var query = tracked ? context.FamilyTrees : context.FamilyTrees.AsNoTracking();

        // Filtered: a caller whose tenant has no tree gets the same 404 as an unknown one.
        return await query.FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("FAMILY_TREE_NOT_FOUND", "This tenant has no family tree.");
    }
}
```

- [ ] **Step 6: Register the service**

In `src/FamilyTree.Infrastructure/DependencyInjection.cs`, add:

```csharp
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Infrastructure.FamilyTrees;
```

```csharp
        services.AddScoped<IFamilyTreeService, FamilyTreeService>();
```

- [ ] **Step 7: Write the endpoints**

Create `src/FamilyTree.Api/Endpoints/FamilyTrees/FamilyTreeEndpoints.cs`:

```csharp
using FamilyTree.Api.Authorization;
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.FamilyTrees;

public static class FamilyTreeEndpoints
{
    public static IEndpointRouteBuilder MapFamilyTreeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/family-tree").WithTags("FamilyTree");

        group.MapGet("/", async (IFamilyTreeService trees, CancellationToken ct) =>
            Results.Ok(await trees.GetAsync(ct)))
            .RequirePermission(Permissions.FamilyTree.View);

        group.MapPut("/", async (
            RenameFamilyTreeRequest request, IFamilyTreeService trees, CancellationToken ct) =>
            Results.Ok(await trees.RenameAsync(request, ct)))
            .RequirePermission(Permissions.FamilyTree.Edit);

        group.MapGet("/view", async (
            Guid? rootId, int? maxDepth, IFamilyTreeService trees, CancellationToken ct) =>
            Results.Ok(await trees.GetViewAsync(rootId, maxDepth, ct)))
            .RequirePermission(Permissions.FamilyTree.View);

        return app;
    }
}
```

- [ ] **Step 8: Map the group**

In `src/FamilyTree.Api/Program.cs`, add the using directive:

```csharp
using FamilyTree.Api.Endpoints.FamilyTrees;
```

and:

```csharp
app.MapFamilyTreeEndpoints();
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyTreeEndpointsTests -v q`
Expected: PASS — 12 tests.

- [ ] **Step 10: Run the full backend suite**

Run: `dotnet test -v q`
Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add src tests
git commit -m "feat: add family tree read, rename, and nested view endpoints"
```

---

## Task 9: Frontend member API layer

**Files:**
- Create: `frontend/src/features/members/types.ts`
- Create: `frontend/src/features/members/membersApi.ts`
- Create: `frontend/src/features/members/useMembers.ts`
- Create: `frontend/src/features/members/membersApi.test.ts`

**Interfaces:**
- Consumes: `apiFetch<T>(path, init?)` and `ApiError` from `frontend/src/services/apiClient.ts`.
- Produces:
  - `interface FamilyMember { id, name, parentId, version, createdAt, updatedAt }`
  - `interface FamilyTreeNode { id, name, parentId, generation, hasMoreChildren, children }`
  - `interface FamilyTreeView { id, name, rootMembers }`
  - `membersApi.list() / .create(name, parentId) / .update(id, name, version) / .remove(id) / .summary() / .tree(params?)`
  - `useMembersQuery()`, `useTreeQuery(params?)`, `useCreateMember()`, `useUpdateMember()`, `useDeleteMember()`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/features/members/membersApi.test.ts`:

```typescript
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { membersApi } from './membersApi'
import { tokenStorage } from '../../services/tokenStorage'
import { ApiError } from '../../services/apiClient'

const jsonResponse = (body: unknown, status = 200): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

describe('membersApi', () => {
  beforeEach(() => {
    tokenStorage.write({ accessToken: 'token', refreshToken: 'refresh' })
    vi.restoreAllMocks()
  })

  it('lists members', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse([{ id: 'a', name: 'سليمان', parentId: null, version: 1 }]),
    )
    vi.stubGlobal('fetch', fetchMock)

    const members = await membersApi.list()

    expect(fetchMock).toHaveBeenCalledWith('/api/v1/family-members', expect.anything())
    expect(members).toHaveLength(1)
    expect(members[0].name).toBe('سليمان')
  })

  it('creates a first-generation member with a null parent', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 'a', name: 'سليمان', parentId: null, version: 1 }, 201),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.create('سليمان', null)

    const [, init] = fetchMock.mock.calls[0]
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ name: 'سليمان', parentId: null })
  })

  it('sends the version when updating so the server can detect a stale write', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 'a', name: 'فارس أحمد', parentId: null, version: 2 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.update('a', 'فارس أحمد', 1)

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/family-members/a')
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body as string)).toEqual({ name: 'فارس أحمد', version: 1 })
  })

  it('never sends parentId on update, because the server rejects it', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 'a', version: 2 }))
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.update('a', 'فارس', 1)

    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string)
    expect(body).not.toHaveProperty('parentId')
  })

  it('deletes a member', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.remove('a')

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/family-members/a')
    expect(init.method).toBe('DELETE')
  })

  it('surfaces the server error code so the UI can translate it', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ code: 'MEMBER_HAS_CHILDREN' }, 409))
    vi.stubGlobal('fetch', fetchMock)

    await expect(membersApi.remove('a')).rejects.toBeInstanceOf(ApiError)
    await expect(membersApi.remove('a')).rejects.toMatchObject({
      code: 'MEMBER_HAS_CHILDREN',
      status: 409,
    })
  })

  it('fetches the tree without parameters by default', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 't', name: 'عائلة السقا', rootMembers: [] }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.tree()

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/family-tree/view')
  })

  it('passes rootId and maxDepth through as query parameters', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 't', name: 'عائلة السقا', rootMembers: [] }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.tree({ rootId: 'abc', maxDepth: 2 })

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/family-tree/view?rootId=abc&maxDepth=2')
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/members/membersApi.test.ts`
Expected: FAIL — cannot resolve `./membersApi`.

- [ ] **Step 3: Write the types**

Create `frontend/src/features/members/types.ts`:

```typescript
export interface FamilyMember {
  id: string
  name: string
  parentId: string | null
  /** Optimistic concurrency token. Echo it back on update or the server rejects the write. */
  version: number
  createdAt: string
  updatedAt: string
}

export interface FamilyTreeNode {
  id: string
  name: string
  parentId: string | null
  generation: number
  /** True when children exist but were not returned because of a depth limit. */
  hasMoreChildren: boolean
  children: FamilyTreeNode[]
}

export interface FamilyTreeView {
  id: string
  name: string
  rootMembers: FamilyTreeNode[]
}

export interface FamilyTreeSummary {
  id: string
  name: string
  memberCount: number
}

export interface TreeQueryParams {
  rootId?: string
  maxDepth?: number
}
```

- [ ] **Step 4: Write the API module**

Create `frontend/src/features/members/membersApi.ts`:

```typescript
import { apiFetch } from '../../services/apiClient'
import type { FamilyMember, FamilyTreeSummary, FamilyTreeView, TreeQueryParams } from './types'

const MEMBERS = '/api/v1/family-members'
const TREE = '/api/v1/family-tree'

const treePath = (params?: TreeQueryParams): string => {
  const query = new URLSearchParams()
  if (params?.rootId) query.set('rootId', params.rootId)
  if (params?.maxDepth !== undefined) query.set('maxDepth', String(params.maxDepth))
  const suffix = query.toString()
  return suffix ? `${TREE}/view?${suffix}` : `${TREE}/view`
}

export const membersApi = {
  list: (): Promise<FamilyMember[]> => apiFetch<FamilyMember[]>(MEMBERS),

  create: (name: string, parentId: string | null): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(MEMBERS, {
      method: 'POST',
      body: JSON.stringify({ name, parentId }),
    }),

  /**
   * Sends only name and version. parentId is deliberately absent: the server rejects it
   * outright (design spec §4.6), and re-parenting is the Phase 5 move command.
   */
  update: (id: string, name: string, version: number): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(`${MEMBERS}/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name, version }),
    }),

  remove: (id: string): Promise<void> => apiFetch<void>(`${MEMBERS}/${id}`, { method: 'DELETE' }),

  summary: (): Promise<FamilyTreeSummary> => apiFetch<FamilyTreeSummary>(TREE),

  tree: (params?: TreeQueryParams): Promise<FamilyTreeView> =>
    apiFetch<FamilyTreeView>(treePath(params)),
}
```

- [ ] **Step 5: Write the query hooks**

Create `frontend/src/features/members/useMembers.ts`:

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { membersApi } from './membersApi'
import type { FamilyMember, FamilyTreeView, TreeQueryParams } from './types'

export const memberKeys = {
  all: ['members'] as const,
  tree: (params?: TreeQueryParams) => ['members', 'tree', params ?? {}] as const,
}

export const useMembersQuery = () =>
  useQuery<FamilyMember[]>({ queryKey: memberKeys.all, queryFn: () => membersApi.list() })

export const useTreeQuery = (params?: TreeQueryParams) =>
  useQuery<FamilyTreeView>({
    queryKey: memberKeys.tree(params),
    queryFn: () => membersApi.tree(params),
  })

/**
 * Every mutation invalidates the whole members namespace. A create or delete changes both the
 * flat list and the nested tree, and an update changes the version every other view holds —
 * so partial invalidation would leave stale versions that fail the next write with a spurious
 * CONCURRENCY_CONFLICT.
 */
const useInvalidateMembers = () => {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: memberKeys.all })
  }
}

export const useCreateMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: ({ name, parentId }: { name: string; parentId: string | null }) =>
      membersApi.create(name, parentId),
    onSuccess: invalidate,
  })
}

export const useUpdateMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: ({ id, name, version }: { id: string; name: string; version: number }) =>
      membersApi.update(id, name, version),
    onSuccess: invalidate,
  })
}

export const useDeleteMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: (id: string) => membersApi.remove(id),
    onSuccess: invalidate,
  })
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/members/membersApi.test.ts`
Expected: PASS — 8 tests.

- [ ] **Step 7: Type-check**

Run: `cd frontend && npx tsc -b`
Expected: exit code 0, no output.

**Do not use `npx tsc --noEmit` here.** `frontend/tsconfig.json` has `"files": []` with project references, so bare `tsc` type-checks an empty program and exits 0 no matter what is broken. Only `tsc -b` walks the referenced projects.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/features/members
git commit -m "feat: add frontend family member API layer and query hooks"
```

---

## Task 10: Members management screen

**Files:**
- Create: `frontend/src/features/members/MemberForm.tsx`
- Create: `frontend/src/features/members/MembersPage.tsx`
- Create: `frontend/src/features/members/MembersPage.test.tsx`
- Modify: `frontend/src/routes/AppRoutes.tsx`
- Modify: `frontend/src/i18n/locales/en.json`
- Modify: `frontend/src/i18n/locales/ar.json`

**Interfaces:**
- Consumes: `useMembersQuery`, `useCreateMember`, `useUpdateMember`, `useDeleteMember` (Task 9); `useAuth().hasPermission`; `useTranslation` from `react-i18next`.
- Produces: route `/members` rendering `MembersPage`.

This is a list, not a visualization — the SVG tree is Phase 3. It exists so member CRUD is exercisable by a human and so the RTL requirement in the definition of done is met for these screens.

- [ ] **Step 1: Add the translation keys**

In `frontend/src/i18n/locales/en.json`, add a `members` block:

```json
  "members": {
    "title": "Family members",
    "empty": "No members yet. Add the first generation to begin.",
    "add": "Add member",
    "name": "Name",
    "parent": "Parent",
    "noParent": "First generation (no parent)",
    "save": "Save",
    "saving": "Saving…",
    "cancel": "Cancel",
    "edit": "Edit",
    "delete": "Delete",
    "deleting": "Deleting…",
    "confirmDelete": "Delete {{name}}?",
    "loading": "Loading members…"
  },
```

and extend the existing `errors` block with:

```json
    "MEMBER_NAME_REQUIRED": "A name is required.",
    "MEMBER_NAME_TOO_LONG": "That name is too long (200 characters maximum).",
    "MEMBER_PARENT_NOT_FOUND": "That parent no longer exists. Reload and try again.",
    "MEMBER_HAS_CHILDREN": "This member cannot be deleted because they have children.",
    "MEMBER_FIELD_NOT_UPDATABLE": "Only the name can be changed here.",
    "MEMBER_NOT_FOUND": "That member no longer exists. Reload and try again.",
    "CONCURRENCY_CONFLICT": "Someone else changed this member. Reload and try again.",
    "FAMILY_TREE_NOT_FOUND": "This family tree could not be found."
```

In `frontend/src/i18n/locales/ar.json`, add the matching `members` block:

```json
  "members": {
    "title": "أفراد العائلة",
    "empty": "لا يوجد أفراد بعد. أضف الجيل الأول للبدء.",
    "add": "إضافة فرد",
    "name": "الاسم",
    "parent": "الأب",
    "noParent": "الجيل الأول (بدون أب)",
    "save": "حفظ",
    "saving": "جارٍ الحفظ…",
    "cancel": "إلغاء",
    "edit": "تعديل",
    "delete": "حذف",
    "deleting": "جارٍ الحذف…",
    "confirmDelete": "حذف {{name}}؟",
    "loading": "جارٍ تحميل الأفراد…"
  },
```

and extend its `errors` block with:

```json
    "MEMBER_NAME_REQUIRED": "الاسم مطلوب.",
    "MEMBER_NAME_TOO_LONG": "الاسم طويل جدًا (٢٠٠ حرف كحد أقصى).",
    "MEMBER_PARENT_NOT_FOUND": "هذا الأب لم يعد موجودًا. أعد التحميل وحاول مجددًا.",
    "MEMBER_HAS_CHILDREN": "لا يمكن حذف هذا الفرد لأن له أبناء.",
    "MEMBER_FIELD_NOT_UPDATABLE": "يمكن تغيير الاسم فقط هنا.",
    "MEMBER_NOT_FOUND": "هذا الفرد لم يعد موجودًا. أعد التحميل وحاول مجددًا.",
    "CONCURRENCY_CONFLICT": "قام شخص آخر بتعديل هذا الفرد. أعد التحميل وحاول مجددًا.",
    "FAMILY_TREE_NOT_FOUND": "تعذر العثور على شجرة العائلة."
```

`src/i18n/locales.test.ts` already asserts the two files have identical key sets — it fails if either block is incomplete.

- [ ] **Step 2: Write the failing component tests**

Create `frontend/src/features/members/MembersPage.test.tsx`:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { MembersPage } from './MembersPage'
import { membersApi } from './membersApi'
import type { FamilyMember } from './types'
import { ApiError } from '../../services/apiClient'

vi.mock('./membersApi')
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))

const member = (over: Partial<FamilyMember> = {}): FamilyMember => ({
  id: 'a',
  name: 'سليمان',
  parentId: null,
  version: 1,
  createdAt: '2026-08-16T12:00:00Z',
  updatedAt: '2026-08-16T12:00:00Z',
  ...over,
})

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MembersPage />
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

describe('MembersPage', () => {
  beforeEach(() => {
    vi.mocked(membersApi.list).mockResolvedValue([member()])
    vi.mocked(membersApi.create).mockResolvedValue(member({ id: 'b', name: 'فارس' }))
    vi.mocked(membersApi.update).mockResolvedValue(member({ name: 'سليمان أحمد', version: 2 }))
    vi.mocked(membersApi.remove).mockResolvedValue(undefined)
  })

  it('lists the members returned by the API', async () => {
    renderPage()

    expect(await screen.findByText('سليمان')).toBeInTheDocument()
  })

  it('shows an empty state when the family has no members', async () => {
    vi.mocked(membersApi.list).mockResolvedValue([])
    renderPage()

    expect(await screen.findByText(i18n.t('members.empty'))).toBeInTheDocument()
  })

  it('creates a first-generation member when no parent is chosen', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.add') }))
    await user.type(screen.getByLabelText(i18n.t('members.name')), 'عمر')
    await user.click(screen.getByRole('button', { name: i18n.t('members.save') }))

    await waitFor(() => expect(membersApi.create).toHaveBeenCalledWith('عمر', null))
  })

  it('creates a child under the selected parent', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.add') }))
    await user.type(screen.getByLabelText(i18n.t('members.name')), 'فارس')
    await user.selectOptions(screen.getByLabelText(i18n.t('members.parent')), 'a')
    await user.click(screen.getByRole('button', { name: i18n.t('members.save') }))

    await waitFor(() => expect(membersApi.create).toHaveBeenCalledWith('فارس', 'a'))
  })

  it('sends the current version when renaming', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.edit') }))
    const nameField = screen.getByLabelText(i18n.t('members.name'))
    await user.clear(nameField)
    await user.type(nameField, 'سليمان أحمد')
    await user.click(screen.getByRole('button', { name: i18n.t('members.save') }))

    await waitFor(() => expect(membersApi.update).toHaveBeenCalledWith('a', 'سليمان أحمد', 1))
  })

  it('does not offer a parent selector when editing', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.edit') }))

    expect(screen.queryByLabelText(i18n.t('members.parent'))).not.toBeInTheDocument()
  })

  it('deletes a member', async () => {
    const user = userEvent.setup()
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.delete') }))

    await waitFor(() => expect(membersApi.remove).toHaveBeenCalledWith('a'))
  })

  it('does not delete when the confirmation is declined', async () => {
    const user = userEvent.setup()
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.delete') }))

    expect(membersApi.remove).not.toHaveBeenCalled()
  })

  it('translates a server error code instead of showing it raw', async () => {
    const user = userEvent.setup()
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    vi.mocked(membersApi.remove).mockRejectedValue(new ApiError('MEMBER_HAS_CHILDREN', 409))
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.delete') }))

    expect(await screen.findByText(i18n.t('errors.MEMBER_HAS_CHILDREN'))).toBeInTheDocument()
  })
})
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/members/MembersPage.test.tsx`
Expected: FAIL — cannot resolve `./MembersPage`.

- [ ] **Step 4: Write the form component**

Create `frontend/src/features/members/MemberForm.tsx`:

```tsx
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { FamilyMember } from './types'

interface MemberFormProps {
  /** Present when editing; absent when adding. */
  member?: FamilyMember
  /** Candidate parents — excludes the member being edited. */
  parents: FamilyMember[]
  isSaving: boolean
  onSubmit: (name: string, parentId: string | null) => void
  onCancel: () => void
}

export function MemberForm({ member, parents, isSaving, onSubmit, onCancel }: MemberFormProps) {
  const { t } = useTranslation()
  const [name, setName] = useState(member?.name ?? '')
  const [parentId, setParentId] = useState(member?.parentId ?? '')

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault()
    onSubmit(name, parentId === '' ? null : parentId)
  }

  return (
    <form onSubmit={handleSubmit}>
      <label htmlFor="member-name">{t('members.name')}</label>
      <input
        id="member-name"
        value={name}
        onChange={(event) => setName(event.target.value)}
        maxLength={200}
        required
      />

      {/* Parent is fixed at creation: the server rejects a parent change on update, and
          re-parenting is the Phase 5 move command. */}
      {member === undefined && (
        <>
          <label htmlFor="member-parent">{t('members.parent')}</label>
          <select
            id="member-parent"
            value={parentId}
            onChange={(event) => setParentId(event.target.value)}
          >
            <option value="">{t('members.noParent')}</option>
            {parents.map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {candidate.name}
              </option>
            ))}
          </select>
        </>
      )}

      <button type="submit" disabled={isSaving}>
        {isSaving ? t('members.saving') : t('members.save')}
      </button>
      <button type="button" onClick={onCancel}>
        {t('members.cancel')}
      </button>
    </form>
  )
}
```

- [ ] **Step 5: Write the page**

Create `frontend/src/features/members/MembersPage.tsx`:

```tsx
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../../services/apiClient'
import { MemberForm } from './MemberForm'
import { useCreateMember, useDeleteMember, useMembersQuery, useUpdateMember } from './useMembers'
import type { FamilyMember } from './types'

type Editing = { mode: 'none' } | { mode: 'add' } | { mode: 'edit'; member: FamilyMember }

const codeOf = (error: unknown): string => (error instanceof ApiError ? error.code : 'UNKNOWN')

export function MembersPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const { data: members, isLoading } = useMembersQuery()
  const createMember = useCreateMember()
  const updateMember = useUpdateMember()
  const deleteMember = useDeleteMember()

  const [editing, setEditing] = useState<Editing>({ mode: 'none' })
  const [errorCode, setErrorCode] = useState<string | null>(null)

  const close = () => setEditing({ mode: 'none' })

  const handleCreate = (name: string, parentId: string | null) => {
    setErrorCode(null)
    createMember.mutate(
      { name, parentId },
      { onSuccess: close, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const handleUpdate = (target: FamilyMember, name: string) => {
    setErrorCode(null)
    updateMember.mutate(
      { id: target.id, name, version: target.version },
      { onSuccess: close, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const handleDelete = (target: FamilyMember) => {
    if (!window.confirm(t('members.confirmDelete', { name: target.name }))) return
    setErrorCode(null)
    deleteMember.mutate(target.id, { onError: (error) => setErrorCode(codeOf(error)) })
  }

  if (isLoading) return <p>{t('members.loading')}</p>

  const all = members ?? []

  return (
    <section>
      <h1>{t('members.title')}</h1>

      {/* Error text comes from the stable server code, never from the server's message —
          the UI is bilingual and message text is not part of the contract. */}
      {errorCode !== null && <p role="alert">{t(`errors.${errorCode}`, t('errors.UNKNOWN'))}</p>}

      {hasPermission('Member.Create') && editing.mode === 'none' && (
        <button type="button" onClick={() => setEditing({ mode: 'add' })}>
          {t('members.add')}
        </button>
      )}

      {editing.mode === 'add' && (
        <MemberForm
          parents={all}
          isSaving={createMember.isPending}
          onSubmit={handleCreate}
          onCancel={close}
        />
      )}

      {editing.mode === 'edit' && (
        <MemberForm
          member={editing.member}
          parents={all.filter((candidate) => candidate.id !== editing.member.id)}
          isSaving={updateMember.isPending}
          onSubmit={(name) => handleUpdate(editing.member, name)}
          onCancel={close}
        />
      )}

      {all.length === 0 ? (
        <p>{t('members.empty')}</p>
      ) : (
        <ul>
          {all.map((current) => (
            <li key={current.id}>
              <span>{current.name}</span>
              {hasPermission('Member.Edit') && (
                <button type="button" onClick={() => setEditing({ mode: 'edit', member: current })}>
                  {t('members.edit')}
                </button>
              )}
              {hasPermission('Member.Delete') && (
                <button type="button" onClick={() => handleDelete(current)}>
                  {deleteMember.isPending ? t('members.deleting') : t('members.delete')}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
```

- [ ] **Step 6: Add the route**

In `frontend/src/routes/AppRoutes.tsx`, add the import and the route inside `<Routes>`, **above** the catch-all:

```tsx
import { MembersPage } from '../features/members/MembersPage'
```

```tsx
    <Route path="/members" element={<ProtectedRoute><MembersPage /></ProtectedRoute>} />
```

- [ ] **Step 7: Run the frontend suite**

Run: `cd frontend && npm test`
Expected: PASS — 37 tests (20 existing + 8 from `membersApi.test.ts` + 9 from `MembersPage.test.tsx`).

- [ ] **Step 8: Type-check and build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: both exit 0. `npm run build` must actually be run — `tsc -b` alone does not catch a bundler failure.

- [ ] **Step 9: Commit**

```bash
git add frontend
git commit -m "feat: add bilingual family members management screen"
```

---

## Task 11: Verification and documentation

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Run every test**

```bash
dotnet test -v q
cd frontend && npm test && npx tsc -b && npm run build
```

Expected: all green. Record the counts.

- [ ] **Step 2: Verify the API by hand against a real database**

```bash
docker compose up -d postgres
dotnet ef database update --project src/FamilyTree.Infrastructure --startup-project src/FamilyTree.Api
```

Start the API (`ASPNETCORE_URLS=http://localhost:5000 dotnet run --project src/FamilyTree.Api --no-launch-profile`), log in, then confirm:

- `POST /api/v1/family-members` with `{"name":"سليمان","parentId":null}` → 201
- `POST` a child with that id as `parentId` → 201
- `GET /api/v1/family-tree/view` → nested, generations 1 and 2
- `DELETE` the parent → 409 with `"code":"MEMBER_HAS_CHILDREN"`
- `PUT` the parent twice with the same `version` → second call 409 with `"code":"CONCURRENCY_CONFLICT"`

Stop the API afterwards so it does not hold build outputs.

- [ ] **Step 3: Document the error codes**

Add to `README.md` under a new `## API error codes` heading:

```markdown
Errors are RFC 7807 Problem Details carrying a stable `code`. Clients translate from the code;
message text is not part of the contract.

| Code | Status | Meaning |
|---|---|---|
| `MEMBER_NAME_REQUIRED` | 400 | Name missing or whitespace |
| `MEMBER_NAME_TOO_LONG` | 400 | Name exceeds 200 characters |
| `MEMBER_PARENT_NOT_FOUND` | 400 | Parent id unknown within this family tree |
| `MEMBER_FIELD_NOT_UPDATABLE` | 400 | Attempt to change parent, tenant, or tree via PUT |
| `MEMBER_NOT_FOUND` | 404 | No such member for this tenant |
| `FAMILY_TREE_NOT_FOUND` | 404 | This tenant has no family tree |
| `MEMBER_HAS_CHILDREN` | 409 | Cannot delete a member who has children |
| `CONCURRENCY_CONFLICT` | 409 | The member changed since it was read |
| `INVALID_CREDENTIALS` | 401 | Login failed |
| `INVALID_REFRESH_TOKEN` | 401 | Refresh token unknown, rotated, or revoked |
```

Also add under "Running locally":

```markdown
The members screen is at `/members` once signed in. Tree visualization is Phase 3.
```

Update the "Current phase" line to `Phase 2 — Family Tree`.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: document Phase 2 API error codes and the members screen"
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|---|---|
| Tech §9 `FamilyMember` entity and self-reference | 1, 2 |
| Tech §10 root family is not a member; `ParentId = NULL` is first generation | 1, 7 |
| Tech §11 schema, §12 indexes | 2 |
| Design §3.1 `version` concurrency field | 1, 2, 4 |
| Design §3.2 three isolation layers | 2 (filter + constraints), 3–5 (service assertions) |
| Design §3.3 composite parent FK | 2 |
| Design §3.4 `pg_trgm` | **Deferred to Phase 3 — deviation 1** |
| Design §3.5 cycle detection | Phase 5 — deviation 2 |
| Design §3.6 generation never stored | 7 |
| Design §3.7 audit | Phase 5 — deviation 3 |
| Tech §21 endpoint shapes | 6, 8 |
| Tech §22 add-member validation (6 rules) | 1 (name), 3 (parent exists / tenant / tree), 6 (`Member.Create`) |
| Tech §23 update cannot change tenant/tree/id | 4, 6 |
| Tech §24 move as a dedicated command | Phase 5 — PUT rejects instead (deviation 2) |
| Tech §26 delete → 409 `MEMBER_HAS_CHILDREN` | 5, 6 |
| Tech §27–§28 tree DTO and `/family-tree/view` | 7, 8 |
| Tech §42 name 1–200 | 1 |
| Tech §43 optimistic concurrency → 409 | 4 |
| Design §4.3 permission-based authorization | 6, 8 |
| Design §4.4 cross-tenant → 404 | 3, 5, 6 |
| Design §4.5 `rootId` / `maxDepth` / `hasMoreChildren` | 7, 8 |
| Design §4.8 Problem Details with stable code | 3, 11 |
| Design §6 real PostgreSQL, no in-memory provider | 2, 3, 6, 8 |
| Definition of done — RTL UI, bilingual | 10 |

**Placeholder scan:** no TBD, no "add appropriate error handling", no "similar to Task N". Every code step carries actual code.

**Type consistency:** `FamilyMember.Create(tenantId, familyTreeId, parentId, name, now)` — identical in Tasks 1, 2, 3, 7. `FamilyMemberResponse` has six members, constructed only in `FamilyMemberService.Map`. `FamilyTreeNodeResponse` has six members, constructed only in `FamilyTreeAssembler.Build`; the frontend `FamilyTreeNode` mirrors it field for field. `Assemble(members, rootId, maxDepth)` — identical in Tasks 7 and 8. `membersApi.update(id, name, version)` — identical in Tasks 9 and 10. `FamilyTreeAggregate` (never `FamilyTree`) throughout.
