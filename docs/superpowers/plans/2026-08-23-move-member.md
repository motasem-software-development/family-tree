# Move Member Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a curator re-parent a family member — including promoting them to first generation — without deleting and re-creating them, and without ever forming a cycle.

**Architecture:** The rule splits across two layers because the halves know different things. `FamilyMember.MoveTo` owns self-parenting and the version bump; a recursive CTE inside the move transaction owns the ancestor walk, because only the database can see the chain. The transaction takes a per-tenant advisory lock first, so two concurrent moves cannot each pass their own check and jointly close a loop. The SPA gets a search-and-pick dialog that disables invalid targets as a courtesy while the server stays the authority.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core with Npgsql, PostgreSQL, xUnit + FluentAssertions + Testcontainers; React 19, TanStack Query, react-i18next, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-08-23-move-member-design.md`

## Global Constraints

- **Error codes are contract; message text is not.** Every failure carries a stable code in the Problem Details `code` field. This work introduces exactly one new code: `MOVE_CREATES_CYCLE`. It reuses `MEMBER_NOT_FOUND` and `CONCURRENCY_CONFLICT`.
- **Cross-tenant is 404, never 403** (design spec §4.4). A 403 confirms the id exists.
- **Tenant isolation is three-layered** (design spec §3.2): the EF global query filter, an explicit ownership assertion in the service, and an explicit `tenant_id` predicate on every table reference in raw SQL — including inside a recursive term.
- **Permission:** `Permissions.Member.Move` (`"Member.Move"`). The constant and its membership in the four system roles were seeded in Phase 1. Do not add seed rows.
- **Optimistic concurrency:** every write sets `context.Entry(member).Property(m => m.Version).OriginalValue` from the client's value. Without it EF compares the version it just read, and the token is inert.
- **Time:** never `DateTimeOffset.UtcNow`. Inject `TimeProvider` and call `timeProvider.GetUtcNow()`.
- **Ids:** `Guid.CreateVersion7()`.
- **Frontend i18n:** every user-visible string is a key present in BOTH `frontend/src/i18n/locales/ar.json` and `en.json`. `locales.test.ts` fails the build on a key in one file but not the other.
- **Audit is deliberately absent.** Design spec §4.6 asks for an audit insert inside the move transaction; no `audit_logs` table exists. The transaction is built anyway so the insert can be added later without restructuring. Do not invent an audit table.

---

### Task 1: The domain command

**Files:**
- Modify: `src/FamilyTree.Domain/FamilyMembers/FamilyMember.cs`
- Test: `tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `void FamilyMember.MoveTo(Guid? newParentId, DateTimeOffset now)` — sets `ParentId`, increments `Version`, calls `Touch(now)`. Throws `DomainException` with code `MOVE_CREATES_CYCLE` when `newParentId == Id`.

- [x] **Step 1: Write the failing tests**

Append to `tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberTests.cs`, inside the existing `FamilyMemberTests` class. The fixtures `Now`, `TenantId`, and `TreeId` are already declared at the top of that file.

```csharp
    [Fact]
    public void MoveTo_reattaches_the_member_to_a_new_parent()
    {
        var member = FamilyMember.Create(TenantId, TreeId, Guid.CreateVersion7(), "فارس", Now);
        var newParent = Guid.CreateVersion7();

        member.MoveTo(newParent, Now);

        member.ParentId.Should().Be(newParent);
    }

    [Fact]
    public void MoveTo_null_promotes_the_member_to_first_generation()
    {
        var member = FamilyMember.Create(TenantId, TreeId, Guid.CreateVersion7(), "فارس", Now);

        member.MoveTo(null, Now);

        member.ParentId.Should().BeNull();
    }

    [Fact]
    public void MoveTo_treats_an_empty_guid_as_no_parent()
    {
        // Create already normalizes Guid.Empty; a move that did not would send it to the
        // database and fail a foreign key instead of recording a first-generation member.
        var member = FamilyMember.Create(TenantId, TreeId, Guid.CreateVersion7(), "فارس", Now);

        member.MoveTo(Guid.Empty, Now);

        member.ParentId.Should().BeNull();
    }

    [Fact]
    public void MoveTo_bumps_the_version_and_the_timestamp()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);
        var later = Now.AddHours(1);

        member.MoveTo(Guid.CreateVersion7(), later);

        member.Version.Should().Be(2);
        member.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void MoveTo_refuses_to_make_a_member_their_own_parent()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);

        var act = () => member.MoveTo(member.Id, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MOVE_CREATES_CYCLE");
    }

    [Fact]
    public void MoveTo_leaves_the_member_untouched_when_it_refuses()
    {
        var original = Guid.CreateVersion7();
        var member = FamilyMember.Create(TenantId, TreeId, original, "فارس", Now);

        var act = () => member.MoveTo(member.Id, Now.AddHours(1));

        act.Should().Throw<DomainException>();
        member.ParentId.Should().Be(original);
        member.Version.Should().Be(1);
        member.UpdatedAt.Should().Be(Now);
    }
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Domain.Tests --filter FullyQualifiedName~FamilyMemberTests`
Expected: FAIL to compile — `FamilyMember` does not contain a definition for `MoveTo`.

- [x] **Step 3: Implement the command**

Add to `src/FamilyTree.Domain/FamilyMembers/FamilyMember.cs`, immediately after `Rename`:

```csharp
    /// <summary>
    /// Re-parents the member. A null <paramref name="newParentId"/> promotes them to first
    /// generation, attached to the family tree rather than to a member (BR-003).
    ///
    /// Only the self-loop is caught here. A deeper cycle needs the ancestor chain, which this
    /// entity cannot see — Infrastructure's recursive CTE owns those (design §3.1). Validation
    /// precedes mutation, so a refused move leaves the member exactly as it was.
    /// </summary>
    public void MoveTo(Guid? newParentId, DateTimeOffset now)
    {
        // Same normalization as Create: Guid.Empty is never a real member id, and letting it
        // through would fail a foreign key at write time instead of recording "no parent".
        var parentId = newParentId == Guid.Empty ? null : newParentId;

        if (parentId == Id)
            throw new DomainException(
                "MOVE_CREATES_CYCLE", "A member cannot be their own parent.");

        ParentId = parentId;
        Version++;
        Touch(now);
    }
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Domain.Tests --filter FullyQualifiedName~FamilyMemberTests`
Expected: PASS — the six new tests plus every existing one.

- [x] **Step 5: Commit**

```bash
git add src/FamilyTree.Domain/FamilyMembers/FamilyMember.cs tests/FamilyTree.Domain.Tests/FamilyMembers/FamilyMemberTests.cs
git commit -m "feat: add the MoveTo command to FamilyMember

Owns the half of the rule the entity can see: self-parenting, the
Guid.Empty normalization Create already does, and the version bump.
Deeper cycles need the ancestor chain and belong to the database."
```

---

### Task 2: The cycle-detection query

**Files:**
- Create: `src/FamilyTree.Infrastructure/FamilyMembers/CycleCheckQuery.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/CycleCheckQueryTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`.
- Produces: `internal static Task<bool> CycleCheckQuery.WouldCreateCycleAsync(ApplicationDbContext context, Guid tenantId, Guid memberId, Guid proposedParentId, CancellationToken ct)` — true when `memberId` is `proposedParentId` or one of its ancestors.

> Verify before writing: the integration test project must be able to see `internal` types of `FamilyTree.Infrastructure` (the existing `FamilyMemberSearchQuery` is `internal` and is not directly tested, so this may be the first such need). Check for an `InternalsVisibleTo` in `src/FamilyTree.Infrastructure/FamilyTree.Infrastructure.csproj` or an `AssemblyInfo.cs`. If absent, add to the csproj:
>
> ```xml
>   <ItemGroup>
>     <InternalsVisibleTo Include="FamilyTree.Api.IntegrationTests" />
>   </ItemGroup>
> ```

- [x] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/CycleCheckQueryTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.FamilyMembers;

/// <summary>
/// The CTE is the only thing standing between a mis-click and a tree that no longer
/// terminates, so it is tested directly rather than only through the service.
/// </summary>
public sealed class CycleCheckQueryTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    /// <summary>Seeds a tenant, a tree, and a chain of members, returning their ids root-first.</summary>
    private async Task<(Guid TenantId, Guid[] Chain)> SeedChainAsync(string slug, params string[] names)
    {
        await using var context = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var tree = FamilyTreeAggregate.Create(tenant.Id, $"Tree {slug}", Now);
        context.FamilyTrees.Add(tree);
        await context.SaveChangesAsync();

        var ids = new List<Guid>();
        Guid? parentId = null;
        foreach (var name in names)
        {
            var member = FamilyMember.Create(tenant.Id, tree.Id, parentId, name, Now);
            context.FamilyMembers.Add(member);
            await context.SaveChangesAsync();
            ids.Add(member.Id);
            parentId = member.Id;
        }

        return (tenant.Id, [.. ids]);
    }

    [Fact]
    public async Task Reports_a_cycle_when_the_target_is_a_direct_child()
    {
        var (tenantId, chain) = await SeedChainAsync("cyc-a", "سليمان", "فارس");
        await using var context = ContextFor(tenantId);

        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, tenantId, chain[0], chain[1], default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Reports_a_cycle_when_the_target_is_a_distant_descendant()
    {
        var (tenantId, chain) = await SeedChainAsync("cyc-b", "سليمان", "فارس", "عمر", "خالد");
        await using var context = ContextFor(tenantId);

        // Moving the root under its own great-grandchild.
        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, tenantId, chain[0], chain[3], default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Reports_a_cycle_when_the_target_is_the_member_itself()
    {
        var (tenantId, chain) = await SeedChainAsync("cyc-c", "سليمان");
        await using var context = ContextFor(tenantId);

        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, tenantId, chain[0], chain[0], default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Reports_no_cycle_for_a_move_into_an_unrelated_branch()
    {
        var (tenantId, chain) = await SeedChainAsync("cyc-d", "سليمان", "فارس", "عمر");
        await using var context = ContextFor(tenantId);

        // Moving the deepest member under the root: walking upward from the root, the chain
        // never reaches the member being moved.
        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, tenantId, chain[2], chain[0], default);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Reports_no_cycle_when_the_chain_belongs_to_another_tenant()
    {
        var (_, chain) = await SeedChainAsync("cyc-e", "سليمان", "فارس");
        var (otherTenantId, _) = await SeedChainAsync("cyc-f", "داوود");
        await using var context = ContextFor(otherTenantId);

        // The walk is tenant-scoped: another tenant's ancestry is invisible, so the query
        // finds nothing to walk rather than climbing into it.
        var result = await CycleCheckQuery.WouldCreateCycleAsync(
            context, otherTenantId, chain[0], chain[1], default);

        result.Should().BeFalse();
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~CycleCheckQueryTests`
Expected: FAIL to compile — `CycleCheckQuery` does not exist. (Docker must be running.)

- [x] **Step 3: Write the query**

Create `src/FamilyTree.Infrastructure/FamilyMembers/CycleCheckQuery.cs`:

```csharp
using System.Data;
using System.Data.Common;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace FamilyTree.Infrastructure.FamilyMembers;

/// <summary>
/// Answers one question: would attaching <c>member_id</c> under <c>parent_id</c> close a loop?
///
/// Raw SQL because EF Core cannot express WITH RECURSIVE, and the same caveat as
/// <see cref="FamilyMemberSearchQuery"/> applies — the EF global query filter (layer 1 of the
/// three-layer tenant isolation in design spec §3.2) does not reach raw SQL, so every table
/// reference carries an explicit tenant_id predicate, the recursive term included. Without it
/// a walk starting on a permitted row could climb into another tenant's ancestry.
/// </summary>
internal static class CycleCheckQuery
{
    /// <summary>
    /// Far past any real genealogy. The walk already terminates on acyclic data — which is the
    /// invariant this check exists to preserve — so the bound is not part of the correctness
    /// argument. It exists so that data already corrupted fails as an error rather than as a
    /// hung connection.
    /// </summary>
    private const int MaxDepth = 100;

    /// <summary>
    /// The walk starts at the PROPOSED PARENT and climbs. If it reaches the member being
    /// moved, that member is an ancestor of its own proposed parent, so the move would close a
    /// loop. Starting at the parent rather than the member is also what makes the self-move
    /// case fall out for free: the first row is then the member itself.
    /// </summary>
    private const string Sql = """
        WITH RECURSIVE chain AS (
            SELECT id, parent_id, 1 AS depth
            FROM family_members
            WHERE id = @parent_id AND tenant_id = @tenant_id
            UNION ALL
            SELECT m.id, m.parent_id, c.depth + 1
            FROM chain c
            JOIN family_members m ON m.id = c.parent_id AND m.tenant_id = @tenant_id
            WHERE c.depth < @max_depth
        )
        SELECT EXISTS (SELECT 1 FROM chain WHERE id = @member_id);
        """;

    public static async Task<bool> WouldCreateCycleAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid memberId,
        Guid proposedParentId,
        CancellationToken ct)
    {
        // An empty tenant id is an unauthenticated caller. Fail closed — report a cycle — so a
        // caller who may see nothing cannot move anything either.
        if (tenantId == Guid.Empty) return true;

        var connection = context.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;

            // The service runs this inside its move transaction. A DbCommand built from the
            // connection is not enlisted automatically, and without this it would read outside
            // the transaction — a different snapshot from the one the update writes.
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();

            AddParameter(command, "tenant_id", NpgsqlDbType.Uuid, tenantId);
            AddParameter(command, "member_id", NpgsqlDbType.Uuid, memberId);
            AddParameter(command, "parent_id", NpgsqlDbType.Uuid, proposedParentId);
            AddParameter(command, "max_depth", NpgsqlDbType.Integer, MaxDepth);

            return Convert.ToBoolean(await command.ExecuteScalarAsync(ct));
        }
        finally
        {
            // Close only what this method opened: inside the move transaction the connection
            // belongs to the caller, and closing it there would abort the transaction.
            if (opened) await context.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, NpgsqlDbType type, object value)
    {
        var parameter = new NpgsqlParameter(name, type) { Value = value };
        command.Parameters.Add(parameter);
    }
}
```

- [x] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~CycleCheckQueryTests`
Expected: PASS — five tests.

- [x] **Step 5: Commit**

```bash
git add src/FamilyTree.Infrastructure/FamilyMembers/CycleCheckQuery.cs tests/FamilyTree.Api.IntegrationTests/FamilyMembers/CycleCheckQueryTests.cs
git commit -m "feat: add the move cycle-detection query

A recursive CTE walking upward from the proposed parent, per design 3.5:
one query regardless of depth, reading the snapshot the update writes.
Tenant-scoped in the recursive term too, since raw SQL is outside the EF
query filter."
```

---

### Task 3: The move command on the service

**Files:**
- Create: `src/FamilyTree.Contracts/FamilyMembers/MoveFamilyMemberRequest.cs`
- Modify: `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberServiceTests.cs`

**Interfaces:**
- Consumes: `FamilyMember.MoveTo` (Task 1), `CycleCheckQuery.WouldCreateCycleAsync` (Task 2).
- Produces: `record MoveFamilyMemberRequest(Guid? ParentId, int Version)` and `Task<FamilyMemberResponse> IFamilyMemberService.MoveAsync(Guid id, MoveFamilyMemberRequest request, CancellationToken ct = default)`.

- [x] **Step 1: Write the failing tests**

Append to `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberServiceTests.cs`. The helpers `SeedTenantWithTreeAsync`, `ServiceFor`, and `ContextFor` already exist in that file.

```csharp
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
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberServiceTests`
Expected: FAIL to compile — `MoveFamilyMemberRequest` and `MoveAsync` do not exist.

- [x] **Step 3: Add the contract**

Create `src/FamilyTree.Contracts/FamilyMembers/MoveFamilyMemberRequest.cs`:

```csharp
namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// Re-parents a member. A null <paramref name="ParentId"/> promotes them to first generation,
/// attached to the family tree itself rather than to a member (BR-003).
///
/// <paramref name="Version"/> is the value from the last read and is required — omitting it is
/// a stale write by definition. Move is a dedicated command rather than a field on
/// <see cref="UpdateFamilyMemberRequest"/> because it carries a rule no other edit does: the
/// target must not be the member or one of their descendants (design spec §4.6).
/// </summary>
public sealed record MoveFamilyMemberRequest(Guid? ParentId, int Version);
```

- [x] **Step 4: Declare the command on the interface**

Add to `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`, after `UpdateAsync`:

```csharp
    /// <summary>
    /// Re-parents a member, or promotes them to first generation with a null parent id.
    /// Throws <c>MOVE_CREATES_CYCLE</c> when the target is the member or one of their
    /// descendants, and <c>MEMBER_NOT_FOUND</c> when either id names nothing visible to the
    /// caller's tenant.
    /// </summary>
    Task<FamilyMemberResponse> MoveAsync(
        Guid id, MoveFamilyMemberRequest request, CancellationToken ct = default);
```

- [x] **Step 5: Implement the command**

Add to `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`, after `UpdateAsync`:

```csharp
    public async Task<FamilyMemberResponse> MoveAsync(
        Guid id, MoveFamilyMemberRequest request, CancellationToken ct = default)
    {
        // One transaction for the check and the write, so the CTE reads the snapshot the write
        // lands on. Design spec §4.6 also puts an audit insert in here; there is no audit_logs
        // table yet, and the transaction exists so adding it later is one statement rather
        // than a restructuring.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // Design §3.2: two moves can each be acyclic against their own snapshot and jointly
        // form a cycle. The lock is transaction-scoped and per-tenant, exactly as
        // AdministratorGuard.SerializeOnTenantAsync does it for the last-administrator rule.
        // The GUID is folded to a bigint because the advisory-lock namespace is one bigint; a
        // collision between two tenants costs contention, never a wrong answer.
        var lockKey = BitConverter.ToInt64(tenant.TenantId.ToByteArray(), 0);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", ct);

        var member = await context.FamilyMembers.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        // Design spec §3.2, layer 2: see the identical assertion in UpdateAsync for rationale.
        if (member.TenantId != tenant.TenantId)
            throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        if (request.ParentId is { } targetId && targetId != Guid.Empty)
        {
            // Same tree as well as same tenant: cross-tree moves are out of scope, and this is
            // the check that keeps them out. Reported as MEMBER_NOT_FOUND rather than a
            // distinct code — from the client's side both mean "that id names nothing here".
            var targetExists = await context.FamilyMembers
                .AnyAsync(m => m.Id == targetId && m.FamilyTreeId == member.FamilyTreeId, ct);

            if (!targetExists)
                throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

            if (await CycleCheckQuery.WouldCreateCycleAsync(context, tenant.TenantId, id, targetId, ct))
                throw new ConflictException(
                    "MOVE_CREATES_CYCLE",
                    "This member cannot be moved under their own descendant.");
        }

        member.MoveTo(request.ParentId, timeProvider.GetUtcNow());

        // Load-bearing for the same reason as in UpdateAsync: without it EF compares the
        // version it just read, and the concurrency token is inert.
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

        await transaction.CommitAsync(ct);

        return Map(member);
    }
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~FamilyMemberServiceTests`
Expected: PASS — the eight new tests plus the existing ones.

- [x] **Step 7: Commit**

```bash
git add src/FamilyTree.Contracts/FamilyMembers/MoveFamilyMemberRequest.cs src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberServiceTests.cs
git commit -m "feat: add the move command behind IFamilyMemberService

Transaction, per-tenant advisory lock, tenant and tree ownership checks,
the cycle CTE, then the write with the client's version as the
concurrency token. A target in another tenant is reported as not found,
never as forbidden."
```

---

### Task 4: The endpoint

**Files:**
- Modify: `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/AuthorizationTests.cs`

**Interfaces:**
- Consumes: `IFamilyMemberService.MoveAsync` (Task 3).
- Produces: `POST /api/v1/family-members/{id:guid}/move` → 200 with `FamilyMemberResponse`; 404 `MEMBER_NOT_FOUND`; 409 `MOVE_CREATES_CYCLE` / `CONCURRENCY_CONFLICT`; 401 unauthenticated; 403 without `Member.Move`.

- [x] **Step 1: Write the failing endpoint tests**

Append to `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs`. The helpers `AuthenticateAsync`, `CreateAsync`, and `CodeOf` already exist there.

```csharp
    [Fact]
    public async Task Post_move_reattaches_a_member_to_a_new_parent()
    {
        await AuthenticateAsync();
        var first = await CreateAsync("سليمان");
        var second = await CreateAsync("داوود");
        var child = await CreateAsync("فارس", first.Id);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/family-members/{child.Id}/move",
            new MoveFamilyMemberRequest(second.Id, child.Version));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        moved.ParentId.Should().Be(second.Id);
    }

    [Fact]
    public async Task Post_move_promotes_a_member_to_first_generation()
    {
        await AuthenticateAsync();
        var parent = await CreateAsync("سليمان");
        var child = await CreateAsync("فارس", parent.Id);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/family-members/{child.Id}/move",
            new MoveFamilyMemberRequest(null, child.Version));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        moved.ParentId.Should().BeNull();
    }

    [Fact]
    public async Task Post_move_returns_409_for_a_move_under_a_descendant()
    {
        await AuthenticateAsync();
        var grandparent = await CreateAsync("سليمان");
        var parent = await CreateAsync("فارس", grandparent.Id);
        var child = await CreateAsync("عمر", parent.Id);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/family-members/{grandparent.Id}/move",
            new MoveFamilyMemberRequest(child.Id, grandparent.Version));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("MOVE_CREATES_CYCLE");
    }

    [Fact]
    public async Task Post_move_returns_409_for_a_move_under_the_member_themselves()
    {
        await AuthenticateAsync();
        var member = await CreateAsync("سليمان");

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/family-members/{member.Id}/move",
            new MoveFamilyMemberRequest(member.Id, member.Version));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("MOVE_CREATES_CYCLE");
    }

    [Fact]
    public async Task Post_move_returns_404_for_a_target_that_does_not_exist()
    {
        await AuthenticateAsync();
        var member = await CreateAsync("سليمان");

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/family-members/{member.Id}/move",
            new MoveFamilyMemberRequest(Guid.CreateVersion7(), member.Version));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(response)).Should().Be("MEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task Post_move_returns_409_for_a_stale_version()
    {
        await AuthenticateAsync();
        var parent = await CreateAsync("سليمان");
        var member = await CreateAsync("فارس");

        var rename = await _client.PutAsJsonAsync(
            $"/api/v1/family-members/{member.Id}",
            new UpdateFamilyMemberRequest("فارس أحمد", member.Version));
        rename.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/family-members/{member.Id}/move",
            new MoveFamilyMemberRequest(parent.Id, member.Version));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("CONCURRENCY_CONFLICT");
    }
```

Extend the existing `Endpoints_require_authentication` theory with the new route:

```csharp
    [InlineData("POST", "/api/v1/family-members/0199a0b1-0000-7000-8000-000000000001/move")]
```

and make that test's POST body depend on the path, since move takes a different body from create:

```csharp
        if (method == "POST")
            request.Content = path.EndsWith("/move")
                ? JsonContent.Create(new MoveFamilyMemberRequest(null, 1))
                : JsonContent.Create(new CreateFamilyMemberRequest("فارس", null));
```

- [x] **Step 2: Write the permission test**

Append to `tests/FamilyTree.Api.IntegrationTests/Endpoints/AuthorizationTests.cs`. Read `Delete_member_returns_403_for_a_caller_lacking_the_delete_permission` in that file first and mirror its setup exactly — including how it obtains a client and calls `TokenWith(...)`.

```csharp
    [Fact]
    public async Task Move_member_returns_403_for_a_caller_lacking_the_move_permission()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenWith(Permissions.Member.View, Permissions.Member.Edit));

        var response = await client.PostAsJsonAsync(
            "/api/v1/family-members/0199a0b1-0000-7000-8000-000000000001/move",
            new MoveFamilyMemberRequest(null, 1));

        // Member.Edit is deliberately present: move is its own permission, and holding the
        // edit permission must not confer the right to restructure the tree.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~FamilyMemberEndpointsTests|FullyQualifiedName~AuthorizationTests"`
Expected: FAIL — 404 from an unmapped route.

- [x] **Step 4: Map the endpoint**

Add to `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs`, between the `MapPut` and `MapDelete` registrations:

```csharp
        // A dedicated command rather than a field on PUT (design spec §4.6): it carries a rule
        // no other edit does, and PUT goes on rejecting parentId outright.
        group.MapPost("/{id:guid}/move", async (
            Guid id, MoveFamilyMemberRequest request, IFamilyMemberService members, CancellationToken ct) =>
            Results.Ok(await members.MoveAsync(id, request, ct)))
            .RequirePermission(Permissions.Member.Move);
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~FamilyMemberEndpointsTests|FullyQualifiedName~AuthorizationTests"`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs tests/FamilyTree.Api.IntegrationTests/Endpoints/AuthorizationTests.cs
git commit -m "feat: expose POST /api/v1/family-members/{id}/move

Guarded by Member.Move, seeded in Phase 1 and unused until now. The
permission test holds Member.Edit on purpose: editing a member must not
confer the right to restructure the tree."
```

---

### Task 5: The concurrent-move guard

**Files:**
- Create: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/ConcurrentMoveTests.cs`

**Interfaces:**
- Consumes: `IFamilyMemberService.MoveAsync` (Task 3). Adds no production code — this task exists so the advisory lock cannot be deleted as ceremony.

- [x] **Step 1: Write the test**

Create `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/ConcurrentMoveTests.cs`:

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
/// Two moves that are each acyclic against their own snapshot can jointly close a loop: A under
/// B while B goes under A. Each context is a separate connection, so this is the real race
/// rather than a simulation of it — the per-tenant advisory lock in MoveAsync is the only thing
/// that makes it come out right.
/// </summary>
public sealed class ConcurrentMoveTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private static IFamilyMemberService ServiceFor(ApplicationDbContext context, Guid tenantId) =>
        new FamilyMemberService(context, new StubTenantContext(tenantId, Guid.CreateVersion7()), Clock);

    [Fact]
    public async Task Two_moves_that_would_close_a_loop_cannot_both_succeed()
    {
        Guid tenantId;
        await using (var seed = ContextFor(Guid.Empty))
        {
            var tenant = Tenant.Create("Tenant race", "mv-race", Now);
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            seed.FamilyTrees.Add(FamilyTreeAggregate.Create(tenant.Id, "Tree race", Now));
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        FamilyMemberResponse first, second;
        await using (var context = ContextFor(tenantId))
        {
            var service = ServiceFor(context, tenantId);
            first = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
            second = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        }

        // Separate contexts, therefore separate connections and separate transactions.
        await using var contextA = ContextFor(tenantId);
        await using var contextB = ContextFor(tenantId);

        var moveA = Task.Run(async () =>
        {
            try
            {
                await ServiceFor(contextA, tenantId).MoveAsync(
                    first.Id, new MoveFamilyMemberRequest(second.Id, first.Version), default);
                return true;
            }
            catch (Exception) { return false; }
        });

        var moveB = Task.Run(async () =>
        {
            try
            {
                await ServiceFor(contextB, tenantId).MoveAsync(
                    second.Id, new MoveFamilyMemberRequest(first.Id, second.Version), default);
                return true;
            }
            catch (Exception) { return false; }
        });

        var outcomes = await Task.WhenAll(moveA, moveB);

        // At most one may commit. Which one is not the point — the point is that the pair
        // cannot both land, because the loser reads the winner's committed row.
        outcomes.Count(succeeded => succeeded).Should().BeLessThanOrEqualTo(1);

        await using var verify = ContextFor(tenantId);
        var members = verify.FamilyMembers.ToList();
        var firstRow = members.Single(m => m.Id == first.Id);
        var secondRow = members.Single(m => m.Id == second.Id);

        // The loop, stated directly: neither may point at the other while the other points back.
        (firstRow.ParentId == second.Id && secondRow.ParentId == first.Id).Should().BeFalse();
    }
}
```

- [x] **Step 2: Run the test**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~ConcurrentMoveTests`
Expected: PASS — the lock added in Task 3 is what makes it pass.

> If it fails, the fault is in Task 3's implementation, not in this test. Check that the lock is taken *before* the member is loaded, and that `CycleCheckQuery` sets `command.Transaction`. A cycle check reading outside the transaction sees a stale snapshot, and both moves pass.

- [x] **Step 3: Commit**

```bash
git add tests/FamilyTree.Api.IntegrationTests/FamilyMembers/ConcurrentMoveTests.cs
git commit -m "test: pin the concurrent-move guard

Two real connections racing to close a loop. Without the per-tenant
advisory lock both moves pass their own cycle check and commit, so this
is what stops the lock being deleted as ceremony."
```

---

### Task 6: The frontend API call and hook

**Files:**
- Modify: `frontend/src/features/members/membersApi.ts`
- Modify: `frontend/src/features/members/useMembers.ts`
- Test: `frontend/src/features/members/membersApi.test.ts`

**Interfaces:**
- Consumes: the endpoint from Task 4.
- Produces: `membersApi.move(id: string, parentId: string | null, version: number): Promise<FamilyMember>` and `useMoveMember()`, a mutation taking `{ id, parentId, version }`.

- [x] **Step 1: Write the failing test**

Append to `frontend/src/features/members/membersApi.test.ts`, matching the mocking style already used there for `update` and `remove` — read the top of that file and reuse its fetch stub rather than introducing a second one. Adjust the mock's variable name below to whatever that file already calls it.

```ts
  it('posts a move to the dedicated command endpoint', async () => {
    await membersApi.move('m1', 'p1', 3)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/family-members/m1/move',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ parentId: 'p1', version: 3 }),
      }),
    )
  })

  it('sends a null parent when promoting to the first generation', async () => {
    await membersApi.move('m1', null, 3)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/family-members/m1/move',
      expect.objectContaining({ body: JSON.stringify({ parentId: null, version: 3 }) }),
    )
  })
```

- [x] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/features/members/membersApi.test.ts`
Expected: FAIL — `membersApi.move is not a function`.

- [x] **Step 3: Add the call**

Add to `frontend/src/features/members/membersApi.ts`, after `update`:

```ts
  /**
   * Re-parents a member; a null parentId promotes them to the first generation. A dedicated
   * command, not a field on update: the server rejects parentId on PUT outright (design spec
   * §4.6), because a move carries a rule no other edit does.
   */
  move: (id: string, parentId: string | null, version: number): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(`${MEMBERS}/${id}/move`, {
      method: 'POST',
      body: JSON.stringify({ parentId, version }),
    }),
```

- [x] **Step 4: Add the hook**

Add to `frontend/src/features/members/useMembers.ts`, after `useUpdateMember`:

```ts
export const useMoveMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: ({
      id,
      parentId,
      version,
    }: {
      id: string
      parentId: string | null
      version: number
    }) => membersApi.move(id, parentId, version),
    // The whole members namespace, as every other mutation does: a move changes the tree's
    // shape, the moved member's version, and every ancestor path the search results carry.
    onSuccess: invalidate,
  })
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/members`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add frontend/src/features/members/membersApi.ts frontend/src/features/members/useMembers.ts frontend/src/features/members/membersApi.test.ts
git commit -m "feat: call the move endpoint from the SPA

A dedicated call rather than a parentId on update, mirroring the server.
Invalidates the whole members namespace: a move changes tree shape, the
member's version, and every ancestor path search returns."
```

---

### Task 7: Descendant ids for the dialog

**Files:**
- Modify: `frontend/src/features/tree/flattenTree.ts`
- Test: `frontend/src/features/tree/flattenTree.test.ts`

**Interfaces:**
- Consumes: `allNodes`, `findNode`, and `FamilyTreeNode`, all already in `flattenTree.ts`.
- Produces: `descendantIds(rootMembers: readonly FamilyTreeNode[], id: string): Set<string>` — every descendant of `id`, excluding `id` itself. Empty for an unknown id.

- [x] **Step 1: Write the failing test**

Append to `frontend/src/features/tree/flattenTree.test.ts`, reusing the `node` fixture helper already defined at the top of that file and adding `descendantIds` to its import from `./flattenTree`.

```ts
describe('descendantIds', () => {
  it('collects every descendant, however deep', () => {
    const roots = [
      node('s1', 'سليمان', 1, [
        node('f1', 'فارس', 2, [node('o1', 'عمر', 3, [], 'f1')], 's1'),
        node('k1', 'خالد', 2, [], 's1'),
      ]),
    ]

    expect(descendantIds(roots, 's1')).toEqual(new Set(['f1', 'o1', 'k1']))
  })

  it('excludes the member themselves, who is disabled for a different reason', () => {
    const roots = [node('s1', 'سليمان', 1, [node('f1', 'فارس', 2, [], 's1')])]

    expect(descendantIds(roots, 's1').has('s1')).toBe(false)
  })

  it('returns nothing for a leaf', () => {
    const roots = [node('s1', 'سليمان', 1, [node('f1', 'فارس', 2, [], 's1')])]

    expect(descendantIds(roots, 'f1').size).toBe(0)
  })

  it('returns nothing for an id that is not in the tree', () => {
    const roots = [node('s1', 'سليمان', 1)]

    expect(descendantIds(roots, 'nope').size).toBe(0)
  })
})
```

- [x] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/features/tree/flattenTree.test.ts`
Expected: FAIL — `descendantIds` is not exported.

- [x] **Step 3: Implement it**

Add to `frontend/src/features/tree/flattenTree.ts`, after `descendantCount`:

```ts
/**
 * Every descendant of `id`, so the move dialog can grey out the targets the server would
 * refuse. A courtesy, not the rule: the server's cycle CTE remains the only authority, and a
 * client working from a stale tree still gets a translated 409.
 *
 * The member themselves is excluded — they are disabled too, but for a reason the dialog
 * states differently.
 */
export const descendantIds = (
  rootMembers: readonly FamilyTreeNode[],
  id: string,
): Set<string> => {
  const subject = findNode(rootMembers, id)
  if (subject === undefined) return new Set()
  return new Set(allNodes(subject.children).map((node) => node.id))
}
```

- [x] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx vitest run src/features/tree/flattenTree.test.ts`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add frontend/src/features/tree/flattenTree.ts frontend/src/features/tree/flattenTree.test.ts
git commit -m "feat: derive a member's descendant ids from the tree

What the move dialog greys out. Client-side, from the tree already in
memory, so the common mistake costs no round trip; the server's CTE
stays the authority."
```

---

### Task 8: The move dialog

**Files:**
- Create: `frontend/src/features/tree/MoveDialog.tsx`
- Create: `frontend/src/features/tree/MoveDialog.test.tsx`
- Modify: `frontend/src/i18n/locales/en.json`
- Modify: `frontend/src/i18n/locales/ar.json`

**Interfaces:**
- Consumes: `useSearch` and `MIN_QUERY_LENGTH` from `./useSearch`, `descendantIds` (Task 7, via the caller), and the search-hit type from `../members/types`.
- Produces:

```ts
export interface MoveDialogProps {
  member: FamilyTreeNode          // the member being moved
  familyName: string              // shown as the first-generation target
  blockedIds: ReadonlySet<string> // the member and everyone beneath them
  errorCode: string | null
  isSaving: boolean
  onCancel: () => void
  onConfirm: (parentId: string | null) => void  // null = promote to first generation
}
```

- [x] **Step 1: Add the translation keys**

In `frontend/src/i18n/locales/en.json`, add a `move` block as a sibling of the existing `modal` block:

```json
  "move": {
    "title": "Move member",
    "body": "Choose the member {{name}} should be attached to.",
    "searchPlaceholder": "Search for the new parent…",
    "rootOption": "{{family}} — first generation",
    "self": "A member cannot be their own parent.",
    "descendant": "This member is already beneath the one being moved.",
    "confirm": "Move",
    "noResults": "No matching members."
  },
```

and in the `errors` block of the same file:

```json
    "MOVE_CREATES_CYCLE": "This member cannot be moved under their own descendant.",
```

and next to the existing `toast.added` / `toast.deleted`:

```json
    "moved": "{{name}} was moved."
```

In `frontend/src/i18n/locales/ar.json`, the same keys:

```json
  "move": {
    "title": "نقل فرد",
    "body": "اختر الفرد الذي سيُنسب إليه {{name}}.",
    "searchPlaceholder": "ابحث عن الأب الجديد…",
    "rootOption": "{{family}} — الجيل الأول",
    "self": "لا يمكن أن يكون الفرد أبًا لنفسه.",
    "descendant": "هذا الفرد يقع أصلًا ضمن ذرية الفرد المنقول.",
    "confirm": "نقل",
    "noResults": "لا يوجد أفراد مطابقون."
  },
```

```json
    "MOVE_CREATES_CYCLE": "لا يمكن نقل هذا الفرد ليصبح ضمن ذريته.",
```

```json
    "moved": "تم نقل {{name}}."
```

- [x] **Step 2: Run the locale parity test**

Run: `cd frontend && npx vitest run src/i18n/locales.test.ts`
Expected: PASS. A key in one file but not the other fails here, before any component reads it.

- [x] **Step 3: Write the failing component test**

Create `frontend/src/features/tree/MoveDialog.test.tsx`:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { membersApi } from '../members/membersApi'
import type { FamilyTreeNode } from '../members/types'
import { MoveDialog } from './MoveDialog'

vi.mock('../members/membersApi')

const node = (
  id: string,
  name: string,
  generation: number,
  children: FamilyTreeNode[] = [],
  parentId: string | null = null,
): FamilyTreeNode => ({ id, name, parentId, generation, hasMoreChildren: false, children })

const SUBJECT = node('s1', 'سليمان', 1, [node('f1', 'فارس', 2, [], 's1')])

const renderDialog = (overrides: Partial<Parameters<typeof MoveDialog>[0]> = {}) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const props = {
    member: SUBJECT,
    familyName: 'عائلة السقا',
    blockedIds: new Set(['s1', 'f1']),
    errorCode: null,
    isSaving: false,
    onCancel: vi.fn(),
    onConfirm: vi.fn(),
    ...overrides,
  }
  render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MoveDialog {...props} />
      </QueryClientProvider>
    </I18nextProvider>,
  )
  return props
}

describe('MoveDialog', () => {
  beforeEach(() => {
    vi.mocked(membersApi.search).mockResolvedValue({ total: 0, items: [] })
  })

  afterEach(() => vi.restoreAllMocks())

  it('offers the family tree itself as the first-generation target', async () => {
    const user = userEvent.setup()
    const props = renderDialog()

    await user.click(
      screen.getByRole('button', {
        name: i18n.t('move.rootOption', { family: 'عائلة السقا' }),
      }),
    )
    await user.click(screen.getByRole('button', { name: i18n.t('move.confirm') }))

    // null, not the tree's id: a first-generation member hangs off no member at all.
    expect(props.onConfirm).toHaveBeenCalledWith(null)
  })

  it('offers a searched member as a target', async () => {
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 'd1', name: 'داوود', generation: 1, ancestors: [] }],
    })
    const user = userEvent.setup()
    const props = renderDialog()

    await user.type(screen.getByLabelText(i18n.t('move.searchPlaceholder')), 'داوود')
    await user.click(await screen.findByRole('button', { name: /داوود/ }))
    await user.click(screen.getByRole('button', { name: i18n.t('move.confirm') }))

    expect(props.onConfirm).toHaveBeenCalledWith('d1')
  })

  it('disables the member themselves and their descendants, with the reason', async () => {
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 2,
      items: [
        { id: 's1', name: 'سليمان', generation: 1, ancestors: [] },
        { id: 'f1', name: 'فارس', generation: 2, ancestors: [{ id: 's1', name: 'سليمان' }] },
      ],
    })
    const user = userEvent.setup()
    renderDialog()

    await user.type(screen.getByLabelText(i18n.t('move.searchPlaceholder')), 'ال')

    expect(await screen.findByRole('button', { name: /سليمان/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: /فارس/ })).toBeDisabled()
    expect(screen.getByText(i18n.t('move.self'))).toBeInTheDocument()
    expect(screen.getByText(i18n.t('move.descendant'))).toBeInTheDocument()
  })

  it('cannot be confirmed before a target is chosen', () => {
    renderDialog()

    expect(screen.getByRole('button', { name: i18n.t('move.confirm') })).toBeDisabled()
  })

  it('shows the translated server error rather than the raw code', () => {
    renderDialog({ errorCode: 'MOVE_CREATES_CYCLE' })

    expect(screen.getByText(i18n.t('errors.MOVE_CREATES_CYCLE'))).toBeInTheDocument()
    expect(screen.queryByText('MOVE_CREATES_CYCLE')).not.toBeInTheDocument()
  })
})
```

- [x] **Step 4: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/features/tree/MoveDialog.test.tsx`
Expected: FAIL — cannot resolve `./MoveDialog`.

- [x] **Step 5: Write the component**

Create `frontend/src/features/tree/MoveDialog.tsx`. Read `MemberActions.tsx` first and reuse its overlay, card, input, and button styles verbatim — this dialog must be indistinguishable from the add/edit/delete modals, not a second visual language. The structure below is what the tests pin; the styling is what you copy across.

```tsx
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { FamilyTreeNode } from '../members/types'
import { MIN_QUERY_LENGTH, useSearch } from './useSearch'

export interface MoveDialogProps {
  member: FamilyTreeNode
  familyName: string
  /** The member and everyone beneath them: the targets the server would refuse. */
  blockedIds: ReadonlySet<string>
  errorCode: string | null
  isSaving: boolean
  onCancel: () => void
  /** null means "promote to first generation". */
  onConfirm: (parentId: string | null) => void
}

/**
 * The chosen target. `null` is a real choice — the family tree — so "nothing chosen yet" needs
 * its own value rather than being spelled null.
 */
type Choice = { kind: 'none' } | { kind: 'root' } | { kind: 'member'; id: string }

export const MoveDialog = ({
  member,
  familyName,
  blockedIds,
  errorCode,
  isSaving,
  onCancel,
  onConfirm,
}: MoveDialogProps) => {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [choice, setChoice] = useState<Choice>({ kind: 'none' })
  const { page } = useSearch(query)

  const reasonFor = (id: string): string | null => {
    if (id === member.id) return t('move.self')
    if (blockedIds.has(id)) return t('move.descendant')
    return null
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (choice.kind === 'none') return
    onConfirm(choice.kind === 'root' ? null : choice.id)
  }

  const searched = query.trim().length >= MIN_QUERY_LENGTH

  return (
    <div role="dialog" aria-modal="true" aria-label={t('move.title')}>
      <form onSubmit={submit}>
        <h2>{t('move.title')}</h2>
        <p>{t('move.body', { name: member.name })}</p>

        <label htmlFor="move-search">{t('move.searchPlaceholder')}</label>
        <input
          id="move-search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder={t('move.searchPlaceholder')}
        />

        {/* Always offered, and always legal: promoting to first generation cannot close a
            loop. Listed first because it is the one target search can never return — the
            family tree is not a member. */}
        <button
          type="button"
          aria-pressed={choice.kind === 'root'}
          onClick={() => setChoice({ kind: 'root' })}
        >
          {t('move.rootOption', { family: familyName })}
        </button>

        {page.items.map((hit) => {
          const reason = reasonFor(hit.id)
          return (
            <div key={hit.id}>
              <button
                type="button"
                disabled={reason !== null}
                aria-pressed={choice.kind === 'member' && choice.id === hit.id}
                onClick={() => setChoice({ kind: 'member', id: hit.id })}
              >
                {hit.name}
                {/* The ancestor path is what tells the many repeated names apart — the reason
                    design spec §5.4 asked the search endpoint for it. */}
                {hit.ancestors.length > 0 && (
                  <span> {hit.ancestors.map((ancestor) => ancestor.name).join(' ‹ ')}</span>
                )}
              </button>
              {reason !== null && <span>{reason}</span>}
            </div>
          )
        })}

        {searched && page.items.length === 0 && <p>{t('move.noResults')}</p>}

        {/* Codes are the contract, the text is not. A raw code must never reach a reader: it
            would be English-only in an Arabic UI. */}
        {errorCode !== null && <p role="alert">{t(`errors.${errorCode}`)}</p>}

        <button type="button" onClick={onCancel}>
          {t('modal.cancel')}
        </button>
        <button type="submit" disabled={choice.kind === 'none' || isSaving}>
          {t('move.confirm')}
        </button>
      </form>
    </div>
  )
}
```

> Verify while writing: the exact exported name and shape of the search-hit type in `frontend/src/features/members/types.ts` (used above as objects with `id`, `name`, `generation`, `ancestors`). If the field names differ, follow that file and fix the test fixtures to match.

- [x] **Step 6: Run the test to verify it passes**

Run: `cd frontend && npx vitest run src/features/tree/MoveDialog.test.tsx`
Expected: PASS — five tests.

- [x] **Step 7: Commit**

```bash
git add frontend/src/features/tree/MoveDialog.tsx frontend/src/features/tree/MoveDialog.test.tsx frontend/src/i18n/locales/en.json frontend/src/i18n/locales/ar.json
git commit -m "feat: add the move target dialog

Search-and-pick, reusing the endpoint whose ancestor paths tell the many
repeated names apart. The family tree is offered first as the
first-generation target; the member and their descendants are offered
disabled with the reason, which the server still enforces."
```

---

### Task 9: Wire Move into the tree screen

**Files:**
- Modify: `frontend/src/features/tree/TreePage.tsx`
- Modify: `frontend/src/features/tree/MemberPanel.tsx`
- Modify: `frontend/src/features/tree/MemberActions.tsx`
- Test: `frontend/src/features/tree/TreePage.test.tsx`

**Interfaces:**
- Consumes: `useMoveMember` (Task 6), `descendantIds` (Task 7), `MoveDialog` (Task 8).
- Produces: no new exports. `MemberPermissions` in `MemberPanel.tsx` gains `canMove: boolean`, and both `MemberPanel` and `ContextMenu` gain an `onMove: () => void` prop.

- [x] **Step 1: Write the failing tests**

In `frontend/src/features/tree/TreePage.test.tsx`, add `'Member.Move'` to the default `permissions` array assigned in `beforeEach`, and stub the new call beside the existing `update`/`remove` stubs:

```tsx
    vi.mocked(membersApi.move).mockResolvedValue({ ...FLAT[1], parentId: 's2', version: 4 })
```

Then append these tests:

```tsx
  it('moves a member to the parent chosen in the dialog', async () => {
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 's2', name: 'عمر', generation: 1, ancestors: [] }],
    })
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(await screen.findByText('فارس'))

    const panel = await screen.findByRole('complementary', { name: 'فارس سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.move') }))

    await user.type(await screen.findByLabelText(i18n.t('move.searchPlaceholder')), 'عمر')
    await user.click(await screen.findByRole('button', { name: /عمر/ }))
    await user.click(screen.getByRole('button', { name: i18n.t('move.confirm') }))

    // Version 3 comes from the flat list, the same join the editor reads: a move is a write
    // like any other and must carry the version the client actually held.
    await waitFor(() => expect(membersApi.move).toHaveBeenCalledWith('f1', 's2', 3))
  })

  it('promotes a member to the first generation from the dialog', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(await screen.findByText('فارس'))

    const panel = await screen.findByRole('complementary', { name: 'فارس سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.move') }))
    await user.click(
      await screen.findByRole('button', {
        name: i18n.t('move.rootOption', { family: 'عائلة السقا' }),
      }),
    )
    await user.click(screen.getByRole('button', { name: i18n.t('move.confirm') }))

    await waitFor(() => expect(membersApi.move).toHaveBeenCalledWith('f1', null, 3))
  })

  it('translates a rejected move rather than showing the raw code', async () => {
    vi.mocked(membersApi.move).mockRejectedValue(new ApiError('MOVE_CREATES_CYCLE', 409))
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 's2', name: 'عمر', generation: 1, ancestors: [] }],
    })
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.move') }))
    await user.type(await screen.findByLabelText(i18n.t('move.searchPlaceholder')), 'عمر')
    await user.click(await screen.findByRole('button', { name: /عمر/ }))
    await user.click(screen.getByRole('button', { name: i18n.t('move.confirm') }))

    expect(await screen.findByText(i18n.t('errors.MOVE_CREATES_CYCLE'))).toBeInTheDocument()
    // The dialog stays open, so the next act is choosing another target rather than finding
    // the member again.
    expect(screen.getByLabelText(i18n.t('move.searchPlaceholder'))).toBeInTheDocument()
  })

  it('disables Move for a caller without the permission', async () => {
    permissions = ['Member.View']
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    expect(within(panel).getByRole('button', { name: i18n.t('tree.move') })).toBeDisabled()
  })
```

Replace the existing test named `renders Move disabled, because its backend command is a later phase` — it asserts the opposite of this feature — with its counterpart:

```tsx
  it('enables Move now that the backend command exists', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    expect(within(panel).getByRole('button', { name: i18n.t('tree.move') })).toBeEnabled()
  })
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/tree/TreePage.test.tsx`
Expected: FAIL — Move is still disabled, so the click does nothing and no dialog appears.

- [x] **Step 3: Wire the page**

In `frontend/src/features/tree/TreePage.tsx`, add the imports:

```tsx
import { useMoveMember } from '../members/useMembers'
import { descendantIds } from './flattenTree'
import { MoveDialog } from './MoveDialog'
```

State and mutation, beside the existing ones:

```tsx
  const moveMember = useMoveMember()
  const [moveOpen, setMoveOpen] = useState(false)
```

Extend the `permissions` object the page builds for the panel and context menu with `canMove: hasPermission('Member.Move')` — read how `canCreate`/`canEdit`/`canDelete` are derived there and follow it exactly.

The handler, beside `confirm` and `openDelete`:

```tsx
  const confirmMove = (parentId: string | null) => {
    if (selected === undefined) return
    // The version comes from the flat list, the same join the editor reads — a move is a
    // write like any other and must carry the version the client actually held.
    const version = detailById.get(selected.id)?.version
    if (version === undefined) return

    setErrorCode(null)
    moveMember.mutate(
      { id: selected.id, parentId, version },
      {
        onSuccess: () => {
          setMoveOpen(false)
          showToast(t('toast.moved', { name: fullName(selected, byId) }))
        },
        // The dialog stays open on failure: the user's next act is to choose a different
        // target, and closing would make them find the member again first.
        onError: (error) => setErrorCode(codeOf(error)),
      },
    )
  }
```

Render it beside the other dialogs:

```tsx
      {moveOpen && selected !== undefined && (
        <MoveDialog
          member={selected}
          familyName={familyName}
          // The member and everyone beneath them. Computed here because the page holds the
          // tree; the dialog only knows the member it was handed.
          blockedIds={new Set([selected.id, ...descendantIds(roots, selected.id)])}
          errorCode={errorCode}
          isSaving={moveMember.isPending}
          onCancel={() => {
            setMoveOpen(false)
            setErrorCode(null)
          }}
          onConfirm={confirmMove}
        />
      )}
```

Wire both triggers — `MemberPanel`'s `onMove` and `ContextMenu`'s `onMove` — to `() => setMoveOpen(true)`.

> Verify while writing: `detailById`'s value shape. It is built from the flat members list and is read elsewhere in this file as `detailById.get(id)?.life`; confirm the version is reachable as `?.version` and adjust if the map stores something narrower.

- [x] **Step 4: Enable the buttons**

In `frontend/src/features/tree/MemberPanel.tsx`: add `canMove: boolean` to `MemberPermissions`, add an `onMove: () => void` prop, and replace the hard-disabled Move button — deleting the comment that calls it a Phase 5 command — with one gated on `permissions.canMove`, written exactly like the Add/Edit/Delete buttons above it.

In `frontend/src/features/tree/MemberActions.tsx`: the same for the context menu's Move item, and update the blocked-delete comment and copy so Move reads as the available way out rather than a later phase.

- [x] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/tree`
Expected: PASS — the whole tree suite.

- [x] **Step 6: Commit**

```bash
git add frontend/src/features/tree
git commit -m "feat: enable Move on the tree screen

The button three surfaces have rendered disabled since Phase 2 now opens
the target dialog. A rejected move keeps the dialog open with the
translated reason, so the next act is picking another target rather than
finding the member again."
```

---

### Task 10: Full verification and documentation

**Files:**
- Modify: `README.md`
- Modify: `frontend/src/features/members/membersApi.ts`
- Modify: `frontend/src/features/members/MemberForm.tsx`
- Modify: `src/FamilyTree.Contracts/FamilyMembers/UpdateFamilyMemberRequest.cs`

- [x] **Step 1: Run every test**

Run: `dotnet test`
Expected: PASS — all four test projects. Docker must be running.

Run: `cd frontend && npm test`
Expected: PASS — the whole component suite.

- [x] **Step 2: Run the linter and the type check**

Run: `cd frontend && npm run lint && npx tsc --noEmit`
Expected: no errors, and no warnings beyond the two pre-existing `only-export-components` ones in `providers.tsx` and `AuthContext.tsx`.

- [x] **Step 3: Correct the stale Phase 5 references**

Three comments say re-parenting is a future phase. They are now false — update each to name `membersApi.move` / the move endpoint instead:

- `frontend/src/features/members/membersApi.ts`, in the `update` doc comment
- `frontend/src/features/members/MemberForm.tsx`, the comment beside the parent field
- `src/FamilyTree.Contracts/FamilyMembers/UpdateFamilyMemberRequest.cs`, the closing line of its summary

Leave `src/FamilyTree.Application/FamilyTrees/FamilyTreeAssembler.cs` and `src/FamilyTree.Api/Authorization/PasswordChangeGateMiddleware.cs` alone: the first describes the validation this command now performs and can be updated only if its sentence is actually wrong, and the second is about public share links, which are Phase 6.

- [x] **Step 4: Document the command**

Add to `README.md`, after the paragraph describing the members screen and the tree outline:

```markdown
A member can be re-parented with `POST /api/v1/family-members/{id}/move`, which takes
`{ parentId, version }` and is guarded by `Member.Move`. A null `parentId` promotes the member
to the first generation, attached to the tree itself rather than to a member. The command
refuses any target that is the member or one of their descendants with `MOVE_CREATES_CYCLE`,
detected by a recursive query inside the move transaction rather than in memory, and serialized
per tenant so that two concurrent moves cannot each pass their own check and jointly close a
loop.

Move is deliberately not a field on `PUT`, which still rejects `parentId` outright. Generations
are not stored, so a moved subtree renumbers itself on the next read — there is no backfill and
no migration.

Unlike design spec §4.6, the move transaction does not yet write an audit row: there is no
`audit_logs` table. The transaction exists so that insert can be added without restructuring.
```

- [x] **Step 5: Commit**

```bash
git add README.md frontend/src/features/members/membersApi.ts frontend/src/features/members/MemberForm.tsx src/FamilyTree.Contracts/FamilyMembers/UpdateFamilyMemberRequest.cs
git commit -m "docs: describe the move command

Records what a client can get wrong: that a null parentId is a promotion
rather than an error, that PUT still refuses parentId, and that the audit
row design spec 4.6 asks for is not written yet."
```

---

## Plan Self-Review

**Spec coverage.** Every section of the design maps to a task: §3 architecture → the file layout across Tasks 1–9; §3.1 cycle detection → Task 2; §3.2 concurrent moves → the lock in Task 3, proven in Task 5; §3.3 generation → nothing to build, exercised by Task 4's move tests reading the member back and recorded in Task 10's README text; §4 endpoint, permission, and the error table → Task 4; §5 contracts → Task 3; §6 rules 1 and 5 → Task 1, rules 2 and 6 → Tasks 2 and 3, rule 3 → Tasks 1, 3, 4, 8 and 9, rule 4 → Task 3's no-op test; §7 frontend → Tasks 6–9; §8 testing → each task's own test step, with the named integration cases distributed to the task that owns each rule; §9 the audit gap → stated in Task 3's implementation comment and in Task 10's README.

**Type consistency.** `MoveFamilyMemberRequest(Guid? ParentId, int Version)` is created in Task 3 and used unchanged in Tasks 4 and 6. `MoveTo(Guid?, DateTimeOffset)` from Task 1 is called only from Task 3. `CycleCheckQuery.WouldCreateCycleAsync(context, tenantId, memberId, proposedParentId, ct)` from Task 2 is called with that argument order in Task 3. `descendantIds(roots, id): Set<string>` from Task 7 feeds `blockedIds: ReadonlySet<string>` in Tasks 8 and 9. `membersApi.move(id, parentId, version)` from Task 6 is what Task 9's tests assert against. The permission flag is `canMove` in both Task 9's page wiring and its component change.

**Verification points**, flagged inline in the task that depends on each rather than assumed: whether the integration test project can see `internal` Infrastructure types (Task 2), the exact name and shape of the search-hit type in `frontend/src/features/members/types.ts` (Task 8), the existing overlay and button styling in `MemberActions.tsx` that `MoveDialog` must reuse (Task 8), the fetch-mock variable name in `membersApi.test.ts` (Task 6), how `AuthorizationTests` mints a scoped token (Task 4), how `TreePage` derives its `permissions` object, and whether `detailById` exposes `version` (Task 9).

**Known behavioural change to an existing test.** `TreePage.test.tsx`'s `renders Move disabled, because its backend command is a later phase` asserts the opposite of this feature. Task 9 replaces it deliberately rather than deleting it quietly.
