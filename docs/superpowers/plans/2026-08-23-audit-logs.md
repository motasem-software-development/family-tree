# Audit Logs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record who changed which family member, when, and what the values were before and after — and let a permitted user read that trail.

**Architecture:** An insert-only `AuditLog` entity, written through an `IAuditWriter` that *stages* the row on the tracked `DbContext` rather than saving it. The caller's existing `SaveChangesAsync` then persists the mutation and its audit row in one transaction, which is what makes "if the audit insert fails, the operation fails" true without restructuring a single command. The read side is a paged, tenant-scoped endpoint behind `Audit.View`, and an SPA screen that names each row's subject from the row's own stored values rather than by looking the member up — because a deleted member cannot be looked up.

**Tech Stack:** .NET 10, EF Core with Npgsql, PostgreSQL `jsonb`, ASP.NET Core Minimal APIs, `System.Text.Json`, xUnit + FluentAssertions + Testcontainers; React 19, TanStack Query, react-i18next, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-08-23-audit-logs-design.md`

## Global Constraints

- **Audit rows are insert-only.** No update path and no delete path may exist anywhere in the codebase — not on the entity, not in a service, not in a test helper.
- **The writer never calls `SaveChangesAsync`.** It stages the entity; the caller's save persists it. A writer that saved would commit the audit row separately from the change it describes, defeating the whole design (spec §3.2, §8 rule 5).
- **Every row is tenant-scoped and user-attributed** from `ITenantContext`, which already exposes `TenantId` and `UserId`. Neither is ever read from a header, query string, or route value.
- **Time comes from the injected `TimeProvider`** — never `DateTimeOffset.UtcNow`.
- **Ids are `Guid.CreateVersion7()`** (the `Entity` base class already does this).
- **Error codes are contract; message text is not.** This work introduces no new API error codes.
- **Cross-tenant is 404, never 403.** Another tenant's audit rows are invisible via the EF global query filter, not merely forbidden.
- **`Action` is a string constant** from `AuditActions` — never an enum, never a bare literal at a call site.
- **Every row's values carry the member's `name`**, including `MOVE`, so the viewer can name a row's subject without a lookup (spec §4, §7).
- **Frontend i18n:** every user-visible string is a key present in BOTH `frontend/src/i18n/locales/ar.json` and `en.json`. `locales.test.ts` fails the build otherwise.
- **`Audit.View` already exists** in `Permissions.cs` and is already seeded to the system roles. Do not add seed rows.

---

### Task 1: The AuditLog entity

**Files:**
- Create: `src/FamilyTree.Domain/Audit/AuditLog.cs`
- Create: `src/FamilyTree.Domain/Audit/AuditActions.cs`
- Create: `src/FamilyTree.Domain/Audit/AuditEntityTypes.cs`
- Test: `tests/FamilyTree.Domain.Tests/Audit/AuditLogTests.cs`

**Interfaces:**
- Consumes: `Entity`, `DomainException`, and `ITenantOwned` from `FamilyTree.Domain.Common`.
- Produces:
  - `AuditLog.Create(Guid tenantId, Guid userId, string action, string entityType, Guid entityId, string? oldValues, string? newValues, DateTimeOffset now)` returning `AuditLog`, with read-only properties `TenantId`, `UserId`, `Action`, `EntityType`, `EntityId`, `OldValues`, `NewValues` (plus `Id`/`CreatedAt`/`UpdatedAt` from `Entity`).
  - `AuditActions.Create/Update/Move/Delete` — the strings `"CREATE"`, `"UPDATE"`, `"MOVE"`, `"DELETE"`.
  - `AuditEntityTypes.FamilyMember` — the string `"FamilyMember"`.

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Domain.Tests/Audit/AuditLogTests.cs`:

```csharp
using FamilyTree.Domain.Audit;
using FamilyTree.Domain.Common;
using FluentAssertions;

namespace FamilyTree.Domain.Tests.Audit;

public class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid EntityId = Guid.CreateVersion7();

    private static AuditLog Sut(string? oldValues = null, string? newValues = "{\"name\":\"سليمان\"}") =>
        AuditLog.Create(
            TenantId, UserId, AuditActions.Create, AuditEntityTypes.FamilyMember, EntityId,
            oldValues, newValues, Now);

    [Fact]
    public void Create_records_what_it_was_given()
    {
        var entry = Sut();

        entry.Id.Should().NotBeEmpty();
        entry.TenantId.Should().Be(TenantId);
        entry.UserId.Should().Be(UserId);
        entry.Action.Should().Be("CREATE");
        entry.EntityType.Should().Be("FamilyMember");
        entry.EntityId.Should().Be(EntityId);
        entry.NewValues.Should().Be("{\"name\":\"سليمان\"}");
        entry.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_allows_either_side_of_the_values_to_be_absent()
    {
        // A CREATE has no before and a DELETE has no after. Both are normal, not errors.
        AuditLog.Create(TenantId, UserId, AuditActions.Delete, AuditEntityTypes.FamilyMember,
            EntityId, "{\"name\":\"سليمان\"}", null, Now).NewValues.Should().BeNull();

        Sut(oldValues: null).OldValues.Should().BeNull();
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_id()
    {
        var act = () => AuditLog.Create(
            Guid.Empty, UserId, AuditActions.Create, AuditEntityTypes.FamilyMember, EntityId,
            null, "{}", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("AUDIT_TENANT_REQUIRED");
    }

    [Fact]
    public void Create_rejects_an_empty_user_id()
    {
        // An unattributed row is worse than no row: it looks like evidence and names nobody.
        var act = () => AuditLog.Create(
            TenantId, Guid.Empty, AuditActions.Create, AuditEntityTypes.FamilyMember, EntityId,
            null, "{}", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("AUDIT_USER_REQUIRED");
    }

    [Fact]
    public void Create_rejects_a_blank_action_or_entity_type()
    {
        var blankAction = () => AuditLog.Create(
            TenantId, UserId, "  ", AuditEntityTypes.FamilyMember, EntityId, null, "{}", Now);
        var blankType = () => AuditLog.Create(
            TenantId, UserId, AuditActions.Create, "", EntityId, null, "{}", Now);

        blankAction.Should().Throw<DomainException>().Which.Code.Should().Be("AUDIT_ACTION_REQUIRED");
        blankType.Should().Throw<DomainException>().Which.Code.Should().Be("AUDIT_ENTITY_TYPE_REQUIRED");
    }

    [Fact]
    public void AuditLog_exposes_no_way_to_change_a_recorded_row()
    {
        // The insert-only guarantee, asserted rather than assumed: a public setter or a
        // mutating method on this type would let a later commit quietly rewrite history.
        var mutators = typeof(AuditLog)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(AuditLog))
            .Select(m => m.Name);

        typeof(AuditLog).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Should().BeEmpty();
        mutators.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Domain.Tests --filter FullyQualifiedName~AuditLogTests`
Expected: FAIL to compile — the `FamilyTree.Domain.Audit` namespace does not exist.

- [ ] **Step 3: Write the constants**

Create `src/FamilyTree.Domain/Audit/AuditActions.cs`:

```csharp
namespace FamilyTree.Domain.Audit;

/// <summary>
/// The verbs an audit row can carry. Strings rather than an enum: these rows are read directly
/// by operators querying the table, and an enum would store integers that need a lookup — in
/// the one table whose entire purpose is being readable after the fact (design §3.3).
/// </summary>
public static class AuditActions
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Move = "MOVE";
    public const string Delete = "DELETE";
}
```

Create `src/FamilyTree.Domain/Audit/AuditEntityTypes.cs`:

```csharp
namespace FamilyTree.Domain.Audit;

/// <summary>
/// What kind of thing a row is about. Only members are audited today; users and roles are a
/// later slice, and this constant is the seam that keeps them from needing a schema change.
/// </summary>
public static class AuditEntityTypes
{
    public const string FamilyMember = "FamilyMember";
}
```

- [ ] **Step 4: Write the entity**

Create `src/FamilyTree.Domain/Audit/AuditLog.cs`:

```csharp
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Audit;

/// <summary>
/// One recorded change. Rows are insert-only: this type has no public setter and no mutating
/// method, and no update or delete path exists anywhere in the codebase (design §3.1). Making
/// an audit row lie therefore means adding code that does not exist, rather than calling
/// something that does.
///
/// Because member deletion is a hard delete, a DELETE row's <see cref="OldValues"/> is the only
/// remaining record that the member existed at all (platform design spec §3.7).
/// </summary>
public sealed class AuditLog : Entity, ITenantOwned
{
    public const int MaxActionLength = 32;
    public const int MaxEntityTypeLength = 64;

    private AuditLog() { }

    public Guid TenantId { get; private set; }

    /// <summary>Who made the change, from ITenantContext. Never null, never Guid.Empty.</summary>
    public Guid UserId { get; private set; }

    public string Action { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }

    /// <summary>
    /// The values before and after, as JSON, or null where the action has no such side — a
    /// CREATE has no before and a DELETE has no after. Stored as jsonb; kept as a string here
    /// so the domain owns no serialization policy.
    /// </summary>
    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }

    public static AuditLog Create(
        Guid tenantId,
        Guid userId,
        string action,
        string entityType,
        Guid entityId,
        string? oldValues,
        string? newValues,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("AUDIT_TENANT_REQUIRED", "An audit entry must belong to a tenant.");

        // An unattributed row is worse than no row: it has the shape of evidence and names
        // nobody. ITenantContext always carries a user on an authenticated request, so an empty
        // id here means the writer was called from somewhere it should not have been.
        if (userId == Guid.Empty)
            throw new DomainException("AUDIT_USER_REQUIRED", "An audit entry must name the user who acted.");

        if (string.IsNullOrWhiteSpace(action))
            throw new DomainException("AUDIT_ACTION_REQUIRED", "An audit entry must carry an action.");

        if (string.IsNullOrWhiteSpace(entityType))
            throw new DomainException("AUDIT_ENTITY_TYPE_REQUIRED", "An audit entry must carry an entity type.");

        var entry = new AuditLog
        {
            TenantId = tenantId,
            UserId = userId,
            Action = action.Trim(),
            EntityType = entityType.Trim(),
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
        };
        entry.InitializeTimestamps(now);
        return entry;
    }
}
```

> `Entity` supplies `Id`, `CreatedAt`, and `UpdatedAt`. `UpdatedAt` is meaningless for a row that is never updated; it is set equal to `CreatedAt` by `InitializeTimestamps` and simply never moves. Do not add a separate timestamp field.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Domain.Tests --filter FullyQualifiedName~AuditLogTests`
Expected: PASS — six tests.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyTree.Domain/Audit tests/FamilyTree.Domain.Tests/Audit
git commit -m "feat: add the AuditLog entity

Insert-only by shape: no public setter, no mutating method, one factory.
A test asserts that absence, because the guarantee is what makes the
table worth reading."
```

---

### Task 2: Persistence — table, filter, migration

**Files:**
- Create: `src/FamilyTree.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`
- Modify: `src/FamilyTree.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: a migration under `src/FamilyTree.Infrastructure/Persistence/Migrations/` (generated)
- Test: `tests/FamilyTree.Api.IntegrationTests/Persistence/AuditLogPersistenceTests.cs`

**Interfaces:**
- Consumes: `AuditLog` (Task 1).
- Produces: `ApplicationDbContext.AuditLogs` — a `DbSet<AuditLog>` carrying the tenant query filter.

- [ ] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Persistence/AuditLogPersistenceTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Audit;
using FamilyTree.Domain.Tenants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Persistence;

public sealed class AuditLogPersistenceTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private async Task<Guid> SeedTenantAsync(string slug)
    {
        await using var context = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static AuditLog Entry(Guid tenantId, string action = AuditActions.Create) =>
        AuditLog.Create(
            tenantId, Guid.CreateVersion7(), action, AuditEntityTypes.FamilyMember,
            Guid.CreateVersion7(), null, "{\"name\":\"سليمان\"}", Now);

    [Fact]
    public async Task An_entry_round_trips_through_the_database()
    {
        var tenantId = await SeedTenantAsync("aud-alpha");
        await using var context = ContextFor(tenantId);

        context.AuditLogs.Add(Entry(tenantId));
        await context.SaveChangesAsync();

        var stored = await context.AuditLogs.SingleAsync();
        stored.Action.Should().Be("CREATE");
        // Arabic must survive the jsonb column unmangled — the values are the point of the row.
        stored.NewValues.Should().Contain("سليمان");
    }

    [Fact]
    public async Task Another_tenants_entries_are_invisible_rather_than_forbidden()
    {
        var tenantId = await SeedTenantAsync("aud-beta");
        var otherTenantId = await SeedTenantAsync("aud-gamma");

        await using (var seed = ContextFor(otherTenantId))
        {
            seed.AuditLogs.Add(Entry(otherTenantId));
            await seed.SaveChangesAsync();
        }

        await using var context = ContextFor(tenantId);

        // Not "denied" — absent. The global query filter is what makes design spec §4.4's
        // uniform 404 true by construction rather than by discipline.
        (await context.AuditLogs.CountAsync()).Should().Be(0);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~AuditLogPersistenceTests`
Expected: FAIL to compile — `ApplicationDbContext` has no `AuditLogs`. (Docker must be running.)

- [ ] **Step 3: Write the configuration**

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`:

```csharp
using FamilyTree.Domain.Audit;
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action).IsRequired().HasMaxLength(AuditLog.MaxActionLength);
        builder.Property(x => x.EntityType).IsRequired().HasMaxLength(AuditLog.MaxEntityTypeLength);

        // jsonb, not text (SRS §32): the values are queryable this way, and PostgreSQL
        // validates the document on write, so a malformed payload fails at the insert rather
        // than years later when someone tries to read the trail.
        builder.Property(x => x.OldValues).HasColumnType("jsonb");
        builder.Property(x => x.NewValues).HasColumnType("jsonb");

        builder.HasOne<Tenant>()
               .WithMany()
               .HasForeignKey(x => x.TenantId)
               .OnDelete(DeleteBehavior.Restrict);

        // The read path's exact ordering (design §6): newest first, id breaking ties. Two
        // changes inside one transaction share a timestamp, so without the id the page
        // boundary would be unstable and paging could skip or repeat rows.
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt, x.Id })
               .IsDescending(false, true, true);

        // "Everything that ever happened to this member", which is the second question anyone
        // asks after "what happened lately".
        builder.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });

        // Deliberately NO foreign key to the user or to family_members. A member row is hard
        // deleted and its audit row must outlive it; a user may be deleted too. An FK would
        // either block those deletes or cascade away the evidence.
    }
}
```

> Verify while writing: `.IsDescending(bool[])` on `HasIndex` requires EF Core 9 or later. This project is on .NET 10 / EF Core 10, so it is available. If the build disagrees, drop the `.IsDescending(...)` call and keep the plain composite index — the ordering still works, only slightly less efficiently — and say so in your report.

- [ ] **Step 4: Register the set and the filter**

In `src/FamilyTree.Infrastructure/Persistence/ApplicationDbContext.cs`:

Add the using at the top: `using FamilyTree.Domain.Audit;`

Add the set beside the others (after `RefreshTokens`):

```csharp
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
```

Add the filter beside the others (the block around `builder.Entity<FamilyMember>().HasQueryFilter(...)`):

```csharp
        builder.Entity<AuditLog>().HasQueryFilter(x => x.TenantId == _tenantId);
```

- [ ] **Step 5: Generate the migration**

Run:

```bash
dotnet ef migrations add AddAuditLogs \
  --project src/FamilyTree.Infrastructure \
  --startup-project src/FamilyTree.Api
```

Then READ the generated migration before continuing. It must create `audit_logs` with `jsonb`
columns for `old_values`/`new_values`, the two indexes, and the tenant foreign key — and it must
contain nothing else. If it also drops or alters an unrelated table, the model snapshot was
stale: stop and report it rather than applying it.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~AuditLogPersistenceTests`
Expected: PASS — two tests. The fixture migrates the container, so the new migration applies automatically.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Infrastructure/Persistence tests/FamilyTree.Api.IntegrationTests/Persistence
git commit -m "feat: add the audit_logs table

jsonb values, the composite index the read path's ordering needs, and the
tenant query filter that makes another tenant's rows invisible rather
than forbidden. No FK to members or users: the evidence has to outlive
both."
```

---

### Task 3: The writer

**Files:**
- Create: `src/FamilyTree.Application/Audit/IAuditWriter.cs`
- Create: `src/FamilyTree.Infrastructure/Audit/AuditWriter.cs`
- Modify: `src/FamilyTree.Infrastructure/DependencyInjection.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Audit/AuditWriterTests.cs`

**Interfaces:**
- Consumes: `AuditLog`, `AuditActions`, `AuditEntityTypes` (Task 1); `ApplicationDbContext.AuditLogs` (Task 2); `ITenantContext`; `TimeProvider`.
- Produces: `void IAuditWriter.Record(string action, string entityType, Guid entityId, object? oldValues, object? newValues)` — serializes and STAGES; never saves.

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Api.IntegrationTests/Audit/AuditWriterTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.Audit;
using FamilyTree.Domain.Audit;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Audit;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Audit;

public sealed class AuditWriterTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;
    private static readonly Guid UserId = Guid.CreateVersion7();

    private async Task<Guid> SeedTenantAsync(string slug)
    {
        await using var context = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static IAuditWriter WriterFor(ApplicationDbContext context, Guid tenantId) =>
        new AuditWriter(context, new StubTenantContext(tenantId, UserId), Clock);

    [Fact]
    public async Task Record_stages_the_entry_without_saving_it()
    {
        var tenantId = await SeedTenantAsync("wr-alpha");
        await using var context = ContextFor(tenantId);

        WriterFor(context, tenantId).Record(
            AuditActions.Create, AuditEntityTypes.FamilyMember, Guid.CreateVersion7(),
            null, new { name = "سليمان" });

        // The whole design rests on this: the writer stages, the caller's save commits. A
        // writer that saved would land the audit row separately from the change it describes.
        (await context.AuditLogs.CountAsync()).Should().Be(0);

        await context.SaveChangesAsync();

        (await context.AuditLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Record_serializes_values_in_the_same_camelCase_the_api_uses()
    {
        var tenantId = await SeedTenantAsync("wr-beta");
        await using var context = ContextFor(tenantId);

        WriterFor(context, tenantId).Record(
            AuditActions.Move, AuditEntityTypes.FamilyMember, Guid.CreateVersion7(),
            new { name = "فارس", parentId = "old" },
            new { name = "فارس", parentId = "new" });
        await context.SaveChangesAsync();

        var stored = await context.AuditLogs.SingleAsync();
        // camelCase, so what an operator reads in the table matches what the API returns.
        stored.OldValues.Should().Contain("\"parentId\"").And.Contain("old");
        stored.NewValues.Should().Contain("new");
        stored.Action.Should().Be("MOVE");
    }

    [Fact]
    public async Task Record_attributes_the_entry_to_the_acting_user_and_tenant()
    {
        var tenantId = await SeedTenantAsync("wr-gamma");
        await using var context = ContextFor(tenantId);

        WriterFor(context, tenantId).Record(
            AuditActions.Delete, AuditEntityTypes.FamilyMember, Guid.CreateVersion7(),
            new { name = "عمر" }, null);
        await context.SaveChangesAsync();

        var stored = await context.AuditLogs.SingleAsync();
        stored.TenantId.Should().Be(tenantId);
        stored.UserId.Should().Be(UserId);
        stored.NewValues.Should().BeNull();
    }

    [Fact]
    public void IAuditWriter_offers_no_way_to_save()
    {
        // Guards design §8 rule 5 at the shape level: if the interface ever grows a save,
        // the atomicity argument in §3.2 stops holding and nothing else would notice.
        typeof(IAuditWriter).GetMethods().Should().OnlyContain(m => m.Name == "Record");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~AuditWriterTests`
Expected: FAIL to compile — `IAuditWriter` and `AuditWriter` do not exist.

- [ ] **Step 3: Write the interface**

Create `src/FamilyTree.Application/Audit/IAuditWriter.cs`:

```csharp
namespace FamilyTree.Application.Audit;

/// <summary>
/// Stages one audit row on the current unit of work. It deliberately does NOT save.
///
/// The caller's own <c>SaveChangesAsync</c> persists the change and its audit row together, so
/// SRS §33 — "if the audit insertion fails, the member move should also fail" — holds without
/// any command being restructured (design §3.2). A writer that saved for itself would commit
/// the row describing a change independently of the change, which is exactly the failure this
/// shape prevents.
/// </summary>
public interface IAuditWriter
{
    /// <param name="oldValues">The before state, or null where the action has none (a CREATE).</param>
    /// <param name="newValues">The after state, or null where the action has none (a DELETE).</param>
    void Record(string action, string entityType, Guid entityId, object? oldValues, object? newValues);
}
```

- [ ] **Step 4: Write the implementation**

Create `src/FamilyTree.Infrastructure/Audit/AuditWriter.cs`:

```csharp
using System.Text.Json;
using FamilyTree.Application.Audit;
using FamilyTree.Application.Common;
using FamilyTree.Domain.Audit;
using FamilyTree.Infrastructure.Persistence;

namespace FamilyTree.Infrastructure.Audit;

public sealed class AuditWriter(
    ApplicationDbContext context,
    ITenantContext tenant,
    TimeProvider timeProvider) : IAuditWriter
{
    /// <summary>
    /// Web defaults are camelCase — the same policy the API serializes responses with, so the
    /// JSON an operator reads in the table matches the JSON the endpoint returned.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, Guid entityId, object? oldValues, object? newValues) =>
        context.AuditLogs.Add(AuditLog.Create(
            tenant.TenantId,
            tenant.UserId,
            action,
            entityType,
            entityId,
            Serialize(oldValues),
            Serialize(newValues),
            timeProvider.GetUtcNow()));

    private static string? Serialize(object? values) =>
        values is null ? null : JsonSerializer.Serialize(values, Options);
}
```

- [ ] **Step 5: Register it**

In `src/FamilyTree.Infrastructure/DependencyInjection.cs`, beside the other scoped services (near `services.AddScoped<IFamilyMemberService, FamilyMemberService>();`):

```csharp
        // Scoped, like the DbContext it stages onto: a singleton would outlive the unit of
        // work whose SaveChanges is supposed to commit its rows.
        services.AddScoped<IAuditWriter, AuditWriter>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~AuditWriterTests`
Expected: PASS — four tests.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Application/Audit src/FamilyTree.Infrastructure/Audit src/FamilyTree.Infrastructure/DependencyInjection.cs tests/FamilyTree.Api.IntegrationTests/Audit
git commit -m "feat: add the audit writer

Stages rather than saves, so the row lands in the same transaction as the
change it describes. A test asserts the interface offers no save at all —
the atomicity argument depends on that absence."
```

---

### Task 4: Audit the four member commands

**Files:**
- Modify: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Audit/MemberAuditTests.cs`

**Interfaces:**
- Consumes: `IAuditWriter.Record` (Task 3), `AuditActions`, `AuditEntityTypes` (Task 1).
- Produces: no new public API. `FamilyMemberService`'s primary constructor gains a fourth parameter, `IAuditWriter auditor` — every existing construction site must be updated, including tests.

> Verify first: `FamilyMemberService` is constructed directly in several integration tests
> (`FamilyMemberServiceTests`, `ConcurrentMoveTests`, and possibly others). Find them all with
> `grep -rn "new FamilyMemberService" tests src` before changing the constructor, and update each.
> A test that no longer compiles is the cheap failure here; one you miss is not.

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Api.IntegrationTests/Audit/MemberAuditTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Audit;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Audit;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Audit;

public sealed class MemberAuditTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;
    private static readonly Guid UserId = Guid.CreateVersion7();

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

    private static IFamilyMemberService ServiceFor(ApplicationDbContext context, Guid tenantId)
    {
        var tenant = new StubTenantContext(tenantId, UserId);
        return new FamilyMemberService(context, tenant, Clock, new AuditWriter(context, tenant, Clock));
    }

    [Fact]
    public async Task Creating_a_member_records_the_new_values_and_no_old_ones()
    {
        var tenantId = await SeedTenantWithTreeAsync("aud-create");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var created = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);

        var entry = await context.AuditLogs.SingleAsync();
        entry.Action.Should().Be(AuditActions.Create);
        entry.EntityType.Should().Be(AuditEntityTypes.FamilyMember);
        entry.EntityId.Should().Be(created.Id);
        entry.OldValues.Should().BeNull();
        entry.NewValues.Should().Contain("سليمان");
    }

    [Fact]
    public async Task Renaming_a_member_records_both_sides_of_the_change()
    {
        var tenantId = await SeedTenantWithTreeAsync("aud-update");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var created = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", null), default);
        await service.UpdateAsync(
            created.Id, new UpdateFamilyMemberRequest("فارس أحمد", created.Version), default);

        var entry = await context.AuditLogs.SingleAsync(a => a.Action == AuditActions.Update);
        entry.OldValues.Should().Contain("فارس").And.NotContain("أحمد");
        entry.NewValues.Should().Contain("أحمد");
    }

    [Fact]
    public async Task Moving_a_member_records_both_parent_ids_and_the_name()
    {
        var tenantId = await SeedTenantWithTreeAsync("aud-move");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var first = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        var second = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        var child = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", first.Id), default);

        await service.MoveAsync(
            child.Id, new MoveFamilyMemberRequest(second.Id, child.Version), default);

        var entry = await context.AuditLogs.SingleAsync(a => a.Action == AuditActions.Move);
        entry.OldValues.Should().Contain(first.Id.ToString());
        entry.NewValues.Should().Contain(second.Id.ToString());
        // The name rides along on both halves so the viewer can say WHOSE move this was
        // without looking the member up (design §4, §7).
        entry.OldValues.Should().Contain("فارس");
        entry.NewValues.Should().Contain("فارس");
    }

    [Fact]
    public async Task Deleting_a_member_records_the_snapshot_that_outlives_them()
    {
        var tenantId = await SeedTenantWithTreeAsync("aud-delete");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var created = await service.CreateAsync(new CreateFamilyMemberRequest("عمر", null), default);
        await service.DeleteAsync(created.Id, default);

        (await context.FamilyMembers.CountAsync()).Should().Be(0);

        var entry = await context.AuditLogs.SingleAsync(a => a.Action == AuditActions.Delete);
        // The member row is gone. This is now the only record they ever existed, so it has to
        // carry the name (platform design spec §3.7).
        entry.OldValues.Should().Contain("عمر");
        entry.NewValues.Should().BeNull();
    }

    [Fact]
    public async Task A_refused_move_records_nothing()
    {
        var tenantId = await SeedTenantWithTreeAsync("aud-refused");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var parent = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        var child = await service.CreateAsync(new CreateFamilyMemberRequest("فارس", parent.Id), default);

        var act = async () => await service.MoveAsync(
            parent.Id, new MoveFamilyMemberRequest(child.Id, parent.Version), default);
        await act.Should().ThrowAsync<ConflictException>();

        // Two CREATEs and nothing else: the refused move never happened, so there is nothing
        // to record. The audit row shares the command's transaction, so a rollback takes both.
        (await context.AuditLogs.CountAsync(a => a.Action == AuditActions.Move)).Should().Be(0);
    }

    [Fact]
    public async Task A_delete_blocked_by_children_records_nothing()
    {
        var tenantId = await SeedTenantWithTreeAsync("aud-blocked");
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context, tenantId);

        var parent = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
        await service.CreateAsync(new CreateFamilyMemberRequest("فارس", parent.Id), default);

        var act = async () => await service.DeleteAsync(parent.Id, default);
        await act.Should().ThrowAsync<ConflictException>();

        (await context.AuditLogs.CountAsync(a => a.Action == AuditActions.Delete)).Should().Be(0);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~MemberAuditTests`
Expected: FAIL to compile — `FamilyMemberService` takes three constructor arguments, not four.

- [ ] **Step 3: Take the dependency and add the snapshot helpers**

In `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`, add the usings:

```csharp
using FamilyTree.Application.Audit;
using FamilyTree.Domain.Audit;
```

Extend the primary constructor:

```csharp
public sealed class FamilyMemberService(
    ApplicationDbContext context,
    ITenantContext tenant,
    TimeProvider timeProvider,
    IAuditWriter auditor) : IFamilyMemberService
```

Add these two private helpers at the bottom of the class, beside `Map`:

```csharp
    /// <summary>
    /// The whole member, for the two actions that need it: a CREATE has no before to compare
    /// against, and a DELETE leaves nothing behind to compare with.
    /// </summary>
    private static object Snapshot(FamilyMember member) => new
    {
        member.Name,
        member.ParentId,
        member.DateOfBirth,
        member.DateOfDeath,
        member.IsDeceased,
    };

    /// <summary>
    /// Just the fields the update command can change. Recording the whole member would bury one
    /// changed name among unchanged values (design §4).
    /// </summary>
    private static object EditableSnapshot(FamilyMember member) => new
    {
        member.Name,
        member.DateOfBirth,
        member.DateOfDeath,
        member.IsDeceased,
    };
```

- [ ] **Step 4: Record from each command**

Four call sites, each placed so it captures the right state:

**`CreateAsync`** — after `context.FamilyMembers.Add(member);` and BEFORE the `try`/`SaveChangesAsync`:

```csharp
        // The id exists before the save (Entity assigns it on construction), so the row can be
        // staged now and land in the same save as the member.
        auditor.Record(
            AuditActions.Create, AuditEntityTypes.FamilyMember, member.Id, null, Snapshot(member));
```

**`UpdateAsync`** — capture BEFORE the mutation, record after it. Immediately before `member.Update(...)`:

```csharp
        var before = EditableSnapshot(member);
```

and immediately after `member.Update(...)` (before the `OriginalValue` line):

```csharp
        auditor.Record(
            AuditActions.Update, AuditEntityTypes.FamilyMember, member.Id, before, EditableSnapshot(member));
```

**`MoveAsync`** — capture the old parent before the move. Immediately before `member.MoveTo(...)`:

```csharp
        var previousParentId = member.ParentId;
```

and immediately after `member.MoveTo(...)`:

```csharp
        // The name rides along on both halves, unchanged: it is what lets the viewer name the
        // subject of a move without a lookup (design §4).
        auditor.Record(
            AuditActions.Move, AuditEntityTypes.FamilyMember, member.Id,
            new { member.Name, ParentId = previousParentId },
            new { member.Name, member.ParentId });
```

**`DeleteAsync`** — record BEFORE the removal, while the member is still readable:

```csharp
        // Before Remove, while there is still something to snapshot. After the save this row is
        // the only record the member existed (platform design spec §3.7).
        auditor.Record(
            AuditActions.Delete, AuditEntityTypes.FamilyMember, member.Id, Snapshot(member), null);

        context.FamilyMembers.Remove(member);
```

- [ ] **Step 5: Update every other construction site**

The constructor gained a parameter, so every direct `new FamilyMemberService(...)` must pass a writer. Use the grep from the Interfaces note above. In the integration tests the shape is:

```csharp
    private static IFamilyMemberService ServiceFor(ApplicationDbContext context, Guid tenantId)
    {
        var tenant = new StubTenantContext(tenantId, Guid.CreateVersion7());
        return new FamilyMemberService(context, tenant, Clock, new AuditWriter(context, tenant, Clock));
    }
```

> Note the shared `tenant` instance: the writer and the service must agree on the tenant, and
> constructing two `StubTenantContext`s with different ids would make the audit row belong to a
> tenant the member does not.

DI needs no change — `IAuditWriter` was registered in Task 3 and is resolved automatically.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~MemberAuditTests`
Expected: PASS — six tests.

Then run the neighbours you touched, which must still pass unchanged:

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~FamilyMemberServiceTests|FullyQualifiedName~ConcurrentMoveTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs tests/FamilyTree.Api.IntegrationTests
git commit -m "feat: record an audit row for every member command

Each row is staged into the command's own save, so a refused command
records nothing — asserted for both a cycle-refused move and a delete
blocked by children. Delete snapshots the whole member, because after a
hard delete the row is the only record they existed."
```

---

### Task 5: The read side

**Files:**
- Create: `src/FamilyTree.Contracts/Audit/AuditEntryResponse.cs`
- Create: `src/FamilyTree.Contracts/Audit/AuditPageResponse.cs`
- Create: `src/FamilyTree.Application/Audit/IAuditService.cs`
- Create: `src/FamilyTree.Application/Audit/AuditLimits.cs`
- Create: `src/FamilyTree.Infrastructure/Audit/AuditService.cs`
- Modify: `src/FamilyTree.Infrastructure/DependencyInjection.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Audit/AuditServiceTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext.AuditLogs` (Task 2).
- Produces:
  - `record AuditEntryResponse(Guid Id, Guid UserId, string? UserEmail, string Action, string EntityType, Guid EntityId, string? OldValues, string? NewValues, DateTimeOffset CreatedAt)`
  - `record AuditPageResponse(int Total, IReadOnlyList<AuditEntryResponse> Items)`
  - `Task<AuditPageResponse> IAuditService.GetAsync(int limit, int offset, CancellationToken ct = default)`
  - `AuditLimits.DefaultPageSize = 25`, `AuditLimits.MaxPageSize = 50`

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Api.IntegrationTests/Audit/AuditServiceTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.Audit;
using FamilyTree.Domain.Audit;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Audit;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Audit;

public sealed class AuditServiceTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private async Task<Guid> SeedTenantAsync(string slug)
    {
        await using var context = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>Seeds `count` entries sharing ONE timestamp, so ordering must fall to the id.</summary>
    private async Task SeedEntriesAsync(Guid tenantId, int count)
    {
        await using var context = ContextFor(tenantId);
        for (var i = 0; i < count; i++)
        {
            context.AuditLogs.Add(AuditLog.Create(
                tenantId, Guid.CreateVersion7(), AuditActions.Create, AuditEntityTypes.FamilyMember,
                Guid.CreateVersion7(), null, $"{{\"name\":\"عضو {i}\"}}", Now));
        }
        await context.SaveChangesAsync();
    }

    private static IAuditService ServiceFor(ApplicationDbContext context) => new AuditService(context);

    [Fact]
    public async Task Returns_the_newest_entries_first()
    {
        var tenantId = await SeedTenantAsync("rd-alpha");
        await using (var context = ContextFor(tenantId))
        {
            context.AuditLogs.Add(AuditLog.Create(
                tenantId, Guid.CreateVersion7(), AuditActions.Create, AuditEntityTypes.FamilyMember,
                Guid.CreateVersion7(), null, "{\"name\":\"الأقدم\"}", Now));
            context.AuditLogs.Add(AuditLog.Create(
                tenantId, Guid.CreateVersion7(), AuditActions.Delete, AuditEntityTypes.FamilyMember,
                Guid.CreateVersion7(), "{\"name\":\"الأحدث\"}", null, Now.AddHours(1)));
            await context.SaveChangesAsync();
        }

        await using var read = ContextFor(tenantId);
        var page = await ServiceFor(read).GetAsync(AuditLimits.DefaultPageSize, 0, default);

        page.Total.Should().Be(2);
        page.Items[0].Action.Should().Be(AuditActions.Delete);
    }

    [Fact]
    public async Task Pages_stably_when_entries_share_a_timestamp()
    {
        var tenantId = await SeedTenantAsync("rd-beta");
        await SeedEntriesAsync(tenantId, 10);
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context);

        var first = await service.GetAsync(4, 0, default);
        var second = await service.GetAsync(4, 4, default);

        // Ten entries share one timestamp. Without the id breaking the tie the two pages could
        // overlap or skip — which is exactly what makes an audit trail untrustworthy.
        first.Items.Should().HaveCount(4);
        second.Items.Should().HaveCount(4);
        first.Items.Select(i => i.Id).Should().NotIntersectWith(second.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Reports_the_untruncated_total_beside_a_capped_page()
    {
        var tenantId = await SeedTenantAsync("rd-gamma");
        await SeedEntriesAsync(tenantId, 60);
        await using var context = ContextFor(tenantId);

        var page = await ServiceFor(context).GetAsync(AuditLimits.MaxPageSize, 0, default);

        // "50 of 60", never items.length — the rule the reports endpoint established.
        page.Items.Should().HaveCount(AuditLimits.MaxPageSize);
        page.Total.Should().Be(60);
    }

    [Fact]
    public async Task Clamps_an_absurd_limit_and_a_negative_offset()
    {
        var tenantId = await SeedTenantAsync("rd-delta");
        await SeedEntriesAsync(tenantId, 60);
        await using var context = ContextFor(tenantId);
        var service = ServiceFor(context);

        (await service.GetAsync(5000, -3, default)).Items.Should().HaveCount(AuditLimits.MaxPageSize);
        (await service.GetAsync(0, 0, default)).Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Cannot_see_another_tenants_entries()
    {
        var tenantId = await SeedTenantAsync("rd-epsilon");
        var otherTenantId = await SeedTenantAsync("rd-zeta");
        await SeedEntriesAsync(otherTenantId, 3);
        await using var context = ContextFor(tenantId);

        var page = await ServiceFor(context).GetAsync(AuditLimits.DefaultPageSize, 0, default);

        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~AuditServiceTests`
Expected: FAIL to compile — `IAuditService`, `AuditService`, and `AuditLimits` do not exist.

- [ ] **Step 3: Write the contracts**

Create `src/FamilyTree.Contracts/Audit/AuditEntryResponse.cs`:

```csharp
namespace FamilyTree.Contracts.Audit;

/// <summary>
/// One recorded change. <paramref name="OldValues"/> and <paramref name="NewValues"/> are raw
/// JSON exactly as stored: the shape varies by action, so the client renders them generically
/// rather than binding to a member DTO that a DELETE row would not satisfy.
///
/// <paramref name="UserEmail"/> is resolved at read time by joining the user store, and is null
/// when that account has since been deleted. The id is what the row stores and what identifies
/// the actor; the email is a convenience for the reader.
/// </summary>
public sealed record AuditEntryResponse(
    Guid Id,
    Guid UserId,
    string? UserEmail,
    string Action,
    string EntityType,
    Guid EntityId,
    string? OldValues,
    string? NewValues,
    DateTimeOffset CreatedAt);
```

Create `src/FamilyTree.Contracts/Audit/AuditPageResponse.cs`:

```csharp
namespace FamilyTree.Contracts.Audit;

/// <summary>
/// A page of entries, newest first. <paramref name="Total"/> is the untruncated count, so a
/// client can say "50 of 1,284" and must never present items.Count as the total.
/// </summary>
public sealed record AuditPageResponse(int Total, IReadOnlyList<AuditEntryResponse> Items);
```

- [ ] **Step 4: Write the limits and the interface**

Create `src/FamilyTree.Application/Audit/AuditLimits.cs`:

```csharp
namespace FamilyTree.Application.Audit;

/// <summary>
/// Server-side constants, not client parameters — the same stance the reports endpoint takes.
/// A client cannot ask for the whole table.
/// </summary>
public static class AuditLimits
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 50;
}
```

Create `src/FamilyTree.Application/Audit/IAuditService.cs`:

```csharp
using FamilyTree.Contracts.Audit;

namespace FamilyTree.Application.Audit;

public interface IAuditService
{
    /// <summary>
    /// One page of audit entries, newest first, scoped to the caller's tenant.
    /// </summary>
    /// <param name="limit">Clamped to 1..<see cref="AuditLimits.MaxPageSize"/>.</param>
    /// <param name="offset">Negative values are treated as 0.</param>
    Task<AuditPageResponse> GetAsync(int limit, int offset, CancellationToken ct = default);
}
```

- [ ] **Step 5: Write the service**

Create `src/FamilyTree.Infrastructure/Audit/AuditService.cs`:

```csharp
using FamilyTree.Application.Audit;
using FamilyTree.Contracts.Audit;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Audit;

/// <summary>
/// The read side. Every query runs through the tenant query filter, so another tenant's rows
/// are absent rather than forbidden — which is what makes the uniform 404 of design spec §4.4
/// true by construction here too.
/// </summary>
public sealed class AuditService(ApplicationDbContext context) : IAuditService
{
    public async Task<AuditPageResponse> GetAsync(int limit, int offset, CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, AuditLimits.MaxPageSize);
        var safeOffset = Math.Max(offset, 0);

        var total = await context.AuditLogs.CountAsync(ct);
        if (total == 0) return new AuditPageResponse(0, []);

        // Id breaks the timestamp tie. Two changes inside one transaction share a CreatedAt, and
        // an unstable order would let paging skip or repeat rows — an audit trail that cannot be
        // paged consistently is not evidence of anything.
        var items = await context.AuditLogs
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(safeOffset)
            .Take(safeLimit)
            .Select(entry => new AuditEntryResponse(
                entry.Id,
                entry.UserId,
                // Left join by projection: a deleted account yields null rather than dropping
                // the row. Losing an entry because its author is gone would be the opposite of
                // what an audit trail is for.
                context.Users.Where(user => user.Id == entry.UserId).Select(user => user.Email).FirstOrDefault(),
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.OldValues,
                entry.NewValues,
                entry.CreatedAt))
            .ToListAsync(ct);

        return new AuditPageResponse(total, items);
    }
}
```

- [ ] **Step 6: Register it**

In `src/FamilyTree.Infrastructure/DependencyInjection.cs`, beside `IAuditWriter`:

```csharp
        services.AddScoped<IAuditService, AuditService>();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~AuditServiceTests`
Expected: PASS — five tests.

- [ ] **Step 8: Commit**

```bash
git add src/FamilyTree.Contracts/Audit src/FamilyTree.Application/Audit src/FamilyTree.Infrastructure/Audit src/FamilyTree.Infrastructure/DependencyInjection.cs tests/FamilyTree.Api.IntegrationTests/Audit
git commit -m "feat: add the audit read service

Newest first with the id breaking timestamp ties, so paging stays stable
across rows written in one transaction. The user's email is resolved at
read time and may be null: a deleted account must not delete its trail."
```

---

### Task 6: The endpoint

**Files:**
- Create: `src/FamilyTree.Api/Endpoints/Audit/AuditEndpoints.cs`
- Modify: `src/FamilyTree.Api/Program.cs` (register the group beside the others)
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/AuditEndpointsTests.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/AuthorizationTests.cs`

**Interfaces:**
- Consumes: `IAuditService.GetAsync` (Task 5).
- Produces: `GET /api/v1/audit?limit=&offset=` → 200 `AuditPageResponse`; 401 unauthenticated; 403 without `Audit.View`.

> Verify first: how endpoint groups are registered in `Program.cs` (look for `MapReportEndpoints`)
> and follow that line exactly.

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/AuditEndpointsTests.cs`. Read
`ReportEndpointsTests.cs` in the same folder first and mirror its fixture setup and authentication
helper — that file's pattern wins over this sketch wherever they differ.

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Audit;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class AuditEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
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

    [Fact]
    public async Task Get_requires_authentication()
    {
        var response = await _client.GetAsync("/api/v1/audit");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_returns_an_empty_page_before_anything_has_happened()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/audit");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<AuditPageResponse>())!;
        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_returns_the_row_a_member_command_just_wrote()
    {
        await AuthenticateAsync();
        var created = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest("سليمان", null));
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.GetAsync("/api/v1/audit");

        var page = (await response.Content.ReadFromJsonAsync<AuditPageResponse>())!;
        page.Total.Should().Be(1);
        page.Items[0].Action.Should().Be("CREATE");
        page.Items[0].NewValues.Should().Contain("سليمان");
        // End to end: the endpoint resolves the acting user's email, not just their id.
        page.Items[0].UserEmail.Should().Be(ApiFactory.AdminEmail);
    }
}
```

Also append a permission test to `tests/FamilyTree.Api.IntegrationTests/Endpoints/AuthorizationTests.cs`, mirroring the `Move_member_returns_403_...` test already in that file:

```csharp
    [Fact]
    public async Task Audit_returns_403_for_a_caller_lacking_the_audit_permission()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenWith(Permissions.Member.View, Permissions.Member.Edit));

        var response = await client.GetAsync("/api/v1/audit");

        // Reading the trail is its own permission: being able to change members must not
        // confer the right to see who changed them.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~AuditEndpointsTests|FullyQualifiedName~AuthorizationTests"`
Expected: FAIL — 404 from an unmapped route.

- [ ] **Step 3: Write the endpoint**

Create `src/FamilyTree.Api/Endpoints/Audit/AuditEndpoints.cs`:

```csharp
using FamilyTree.Api.Authorization;
using FamilyTree.Application.Audit;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.Audit;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit").WithTags("Audit");

        // Audit.View, not FamilyTree.View: reading who changed what is a different privilege
        // from reading the tree itself, and the permission has existed unused since Phase 1
        // waiting for this endpoint.
        //
        // limit and offset are the client's to choose, unlike the reports endpoint's fixed
        // windows — a trail grows without bound, so paging is the point rather than a detail.
        // The service clamps both; the endpoint passes them through untouched.
        group.MapGet("/", async (
            IAuditService audit, CancellationToken ct, int? limit = null, int? offset = null) =>
            Results.Ok(await audit.GetAsync(limit ?? AuditLimits.DefaultPageSize, offset ?? 0, ct)))
            .RequirePermission(Permissions.Audit.View);

        return app;
    }
}
```

- [ ] **Step 4: Register the group**

In `src/FamilyTree.Api/Program.cs`, beside the other `Map*Endpoints()` calls:

```csharp
app.MapAuditEndpoints();
```

Add the matching using if the file uses explicit usings for the other endpoint namespaces.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~AuditEndpointsTests|FullyQualifiedName~AuthorizationTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyTree.Api/Endpoints/Audit src/FamilyTree.Api/Program.cs tests/FamilyTree.Api.IntegrationTests/Endpoints
git commit -m "feat: expose GET /api/v1/audit

Behind Audit.View, which has been seeded and unused since Phase 1. The
permission test holds Member.Edit deliberately: changing members must not
confer the right to see who changed them."
```

---

### Task 7: The frontend data layer

**Files:**
- Create: `frontend/src/features/audit/types.ts`
- Create: `frontend/src/features/audit/auditApi.ts`
- Create: `frontend/src/features/audit/useAudit.ts`
- Test: `frontend/src/features/audit/auditApi.test.ts`

**Interfaces:**
- Consumes: the endpoint from Task 6.
- Produces:
  - `interface AuditEntry { id, userId, userEmail: string | null, action, entityType, entityId, oldValues: string | null, newValues: string | null, createdAt }`
  - `interface AuditPage { total: number; items: AuditEntry[] }`
  - `auditApi.list(limit: number, offset: number): Promise<AuditPage>`
  - `useAuditQuery(limit: number, offset: number)`, `auditKeys`, `AUDIT_PAGE_SIZE = 25`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/features/audit/auditApi.test.ts`. Read `frontend/src/features/members/membersApi.test.ts` first and reuse its fetch-stub setup exactly — including whatever it names the mock. Adjust the sketch below to match that file.

```ts
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { auditApi } from './auditApi'

const fetchMock = vi.fn()
vi.stubGlobal('fetch', fetchMock)

describe('auditApi', () => {
  beforeEach(() => {
    fetchMock.mockReset()
    fetchMock.mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ total: 0, items: [] }),
    })
  })

  it('asks for a page with an explicit limit and offset', async () => {
    await auditApi.list(25, 50)

    // Parameter building goes through URLSearchParams, as everywhere else in this client.
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/audit?limit=25&offset=50')
  })

  it('returns the page as the server framed it', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        total: 61,
        items: [
          {
            id: 'a1',
            userId: 'u1',
            userEmail: 'admin@example.com',
            action: 'DELETE',
            entityType: 'FamilyMember',
            entityId: 'm1',
            oldValues: '{"name":"عمر"}',
            newValues: null,
            createdAt: '2026-08-23T12:00:00Z',
          },
        ],
      }),
    })

    const page = await auditApi.list(25, 0)

    // The untruncated total travels with the page: the screen must be able to say "1 of 61".
    expect(page.total).toBe(61)
    expect(page.items[0].userEmail).toBe('admin@example.com')
    expect(page.items[0].newValues).toBeNull()
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/features/audit/auditApi.test.ts`
Expected: FAIL — cannot resolve `./auditApi`.

- [ ] **Step 3: Write the types**

Create `frontend/src/features/audit/types.ts`:

```ts
/**
 * One recorded change, mirroring AuditEntryResponse field for field.
 *
 * `oldValues` and `newValues` are raw JSON strings, not parsed objects: their shape varies by
 * action, and a later slice will audit users and roles with a different shape again. The screen
 * parses them defensively rather than binding them to a member type.
 */
export interface AuditEntry {
  id: string
  userId: string
  /** Null when the acting account has since been deleted. */
  userEmail: string | null
  action: string
  entityType: string
  entityId: string
  oldValues: string | null
  newValues: string | null
  createdAt: string
}

export interface AuditPage {
  /** Untruncated. Never render items.length as the total. */
  total: number
  items: AuditEntry[]
}
```

- [ ] **Step 4: Write the client and the hook**

Create `frontend/src/features/audit/auditApi.ts`:

```ts
import { apiFetch } from '../../services/apiClient'
import type { AuditPage } from './types'

const AUDIT = '/api/v1/audit'

export const auditApi = {
  list: (limit: number, offset: number): Promise<AuditPage> => {
    const params = new URLSearchParams({ limit: String(limit), offset: String(offset) })
    return apiFetch<AuditPage>(`${AUDIT}?${params}`)
  },
}
```

Create `frontend/src/features/audit/useAudit.ts`:

```ts
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { auditApi } from './auditApi'
import type { AuditPage } from './types'

/** Matches the server's AuditLimits.DefaultPageSize; the server clamps regardless. */
export const AUDIT_PAGE_SIZE = 25

export const auditKeys = {
  all: ['audit'] as const,
  page: (limit: number, offset: number) => ['audit', limit, offset] as const,
}

export const useAuditQuery = (limit: number, offset: number) =>
  useQuery<AuditPage>({
    queryKey: auditKeys.page(limit, offset),
    queryFn: () => auditApi.list(limit, offset),
    // Holding the previous page while the next loads stops the table blanking between clicks.
    placeholderData: keepPreviousData,
  })
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/audit`
Expected: PASS — two tests.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/features/audit
git commit -m "feat: read the audit trail from the SPA

Values stay raw JSON strings across the boundary: their shape varies by
action, and a later slice audits users and roles with a shape different
again."
```

---

### Task 8: The audit screen

**Files:**
- Create: `frontend/src/features/audit/AuditPage.tsx`
- Create: `frontend/src/features/audit/auditValues.ts`
- Create: `frontend/src/features/audit/auditValues.test.ts`
- Create: `frontend/src/features/audit/AuditPage.test.tsx`
- Modify: `frontend/src/routes/AppRoutes.tsx`
- Modify: `frontend/src/app/AppShell.tsx`
- Modify: `frontend/src/i18n/locales/en.json`, `frontend/src/i18n/locales/ar.json`

**Interfaces:**
- Consumes: `useAuditQuery`, `AUDIT_PAGE_SIZE` (Task 7), `AppShell`, `useAuth`.
- Produces:
  - `parseValues(raw: string | null): Record<string, unknown> | null`
  - `subjectName(entry: AuditEntry): string | null`
  - `changedFields(oldValues, newValues): ChangedField[]` where `ChangedField = { field: string; before: unknown; after: unknown }`

- [ ] **Step 1: Add the translation keys**

In `frontend/src/i18n/locales/en.json`, add an `audit` block beside the others:

```json
  "audit": {
    "title": "Audit trail",
    "subtitle": "Every change to a family member, and who made it.",
    "empty": "Nothing has been changed yet.",
    "colWhen": "When",
    "colWho": "Who",
    "colAction": "Action",
    "colSubject": "Member",
    "colChange": "Change",
    "unknownUser": "Deleted account",
    "unknownSubject": "—",
    "showing": "Showing {{count}} of {{total}}",
    "previous": "Previous",
    "next": "Next",
    "actionCREATE": "Added",
    "actionUPDATE": "Edited",
    "actionMOVE": "Moved",
    "actionDELETE": "Deleted"
  },
```

In `frontend/src/i18n/locales/ar.json`, the same keys:

```json
  "audit": {
    "title": "سجل التدقيق",
    "subtitle": "كل تغيير على أفراد العائلة، ومن قام به.",
    "empty": "لم يُجرَ أي تغيير بعد.",
    "colWhen": "التاريخ",
    "colWho": "المستخدم",
    "colAction": "الإجراء",
    "colSubject": "الفرد",
    "colChange": "التغيير",
    "unknownUser": "حساب محذوف",
    "unknownSubject": "—",
    "showing": "عرض {{count}} من {{total}}",
    "previous": "السابق",
    "next": "التالي",
    "actionCREATE": "إضافة",
    "actionUPDATE": "تعديل",
    "actionMOVE": "نقل",
    "actionDELETE": "حذف"
  },
```

`nav.audit` already exists in both files and is used by the current placeholder nav item. Do NOT add a second key for it.

- [ ] **Step 2: Run the locale parity test**

Run: `cd frontend && npx vitest run src/i18n/locales.test.ts`
Expected: PASS. A key in one file and missing from the other fails here first, and cheaply.

- [ ] **Step 3: Write the failing helper tests**

Create `frontend/src/features/audit/auditValues.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { changedFields, parseValues, subjectName } from './auditValues'
import type { AuditEntry } from './types'

const entry = (over: Partial<AuditEntry> = {}): AuditEntry => ({
  id: 'a1',
  userId: 'u1',
  userEmail: 'admin@example.com',
  action: 'UPDATE',
  entityType: 'FamilyMember',
  entityId: 'm1',
  oldValues: '{"name":"فارس"}',
  newValues: '{"name":"فارس أحمد"}',
  createdAt: '2026-08-23T12:00:00Z',
  ...over,
})

describe('parseValues', () => {
  it('returns null for an absent side', () => {
    expect(parseValues(null)).toBeNull()
  })

  it('returns null rather than throwing on malformed JSON', () => {
    // The screen must not white-screen over one bad row written by some future writer.
    expect(parseValues('{not json')).toBeNull()
  })
})

describe('subjectName', () => {
  it('prefers the new values, which describe the member as they now are', () => {
    expect(subjectName(entry())).toBe('فارس أحمد')
  })

  it('falls back to the old values for a delete, whose member no longer exists', () => {
    expect(subjectName(entry({ action: 'DELETE', oldValues: '{"name":"عمر"}', newValues: null })))
      .toBe('عمر')
  })

  it('returns null when neither side carries a name', () => {
    expect(subjectName(entry({ oldValues: '{"parentId":"p1"}', newValues: '{"parentId":"p2"}' })))
      .toBeNull()
  })
})

describe('changedFields', () => {
  it('lists only the fields that actually differ', () => {
    const fields = changedFields(
      { name: 'فارس', isDeceased: false },
      { name: 'فارس أحمد', isDeceased: false },
    )

    expect(fields).toEqual([{ field: 'name', before: 'فارس', after: 'فارس أحمد' }])
  })

  it('treats an absent side as every field appearing or disappearing', () => {
    expect(changedFields(null, { name: 'سليمان' })).toEqual([
      { field: 'name', before: undefined, after: 'سليمان' },
    ])
    expect(changedFields({ name: 'عمر' }, null)).toEqual([
      { field: 'name', before: 'عمر', after: undefined },
    ])
  })
})
```

- [ ] **Step 4: Run the helper tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/audit/auditValues.test.ts`
Expected: FAIL — cannot resolve `./auditValues`.

- [ ] **Step 5: Write the helpers**

Create `frontend/src/features/audit/auditValues.ts`:

```ts
import type { AuditEntry } from './types'

/**
 * The stored values, parsed. Returns null for an absent side — a CREATE has no before and a
 * DELETE has no after — and also for malformed JSON, so one unreadable row degrades to a row
 * with no detail rather than taking the screen down with it.
 */
export const parseValues = (raw: string | null): Record<string, unknown> | null => {
  if (raw === null) return null
  try {
    const parsed: unknown = JSON.parse(raw)
    return typeof parsed === 'object' && parsed !== null ? (parsed as Record<string, unknown>) : null
  } catch {
    return null
  }
}

/**
 * Whose change this was, taken from the entry's OWN values and never from a member lookup.
 * A deleted member cannot be looked up — that is the whole reason the snapshot is stored — and
 * resolving against the members list would blank exactly the rows an audit trail exists for.
 *
 * New values first: they describe the member as they now are. Old values are the fallback, which
 * is what a DELETE always uses.
 */
export const subjectName = (entry: AuditEntry): string | null => {
  const after = parseValues(entry.newValues)
  const before = parseValues(entry.oldValues)
  const name = after?.name ?? before?.name
  return typeof name === 'string' ? name : null
}

export interface ChangedField {
  field: string
  before: unknown
  after: unknown
}

/**
 * The fields that differ between the two sides. Generic on purpose: the client does not know
 * which fields an action carries, and must keep working when a later slice audits users or
 * roles with a different shape.
 */
export const changedFields = (
  oldValues: Record<string, unknown> | null,
  newValues: Record<string, unknown> | null,
): ChangedField[] => {
  const keys = new Set([...Object.keys(oldValues ?? {}), ...Object.keys(newValues ?? {})])

  return [...keys]
    .map((field) => ({ field, before: oldValues?.[field], after: newValues?.[field] }))
    .filter((change) => JSON.stringify(change.before) !== JSON.stringify(change.after))
}
```

- [ ] **Step 6: Run the helper tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/audit/auditValues.test.ts`
Expected: PASS — seven tests.

- [ ] **Step 7: Write the failing screen test**

Create `frontend/src/features/audit/AuditPage.test.tsx`. Read `frontend/src/features/reports/ReportsPage.test.tsx` first and mirror its provider wrapping and mocking style.

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { auditApi } from './auditApi'
import { AuditPage } from './AuditPage'

vi.mock('./auditApi')

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com', familyTreeName: 'عائلة السقا' },
    hasPermission: () => true,
    logout: vi.fn(),
  }),
}))

const entry = (over: Record<string, unknown> = {}) => ({
  id: 'a1',
  userId: 'u1',
  userEmail: 'admin@example.com',
  action: 'CREATE',
  entityType: 'FamilyMember',
  entityId: 'm1',
  oldValues: null,
  newValues: '{"name":"سليمان"}',
  createdAt: '2026-08-23T12:00:00Z',
  ...over,
})

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AuditPage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

describe('AuditPage', () => {
  beforeEach(() => {
    vi.mocked(auditApi.list).mockResolvedValue({ total: 1, items: [entry()] })
  })

  afterEach(() => vi.restoreAllMocks())

  it('names the member from the entry, and the user who acted', async () => {
    renderPage()

    expect(await screen.findByText('سليمان')).toBeInTheDocument()
    expect(screen.getByText('admin@example.com')).toBeInTheDocument()
  })

  it('names a deleted member from the snapshot that outlived them', async () => {
    vi.mocked(auditApi.list).mockResolvedValue({
      total: 1,
      items: [entry({ action: 'DELETE', oldValues: '{"name":"عمر"}', newValues: null })],
    })
    renderPage()

    // The member row is gone from the database; this name exists only inside the audit entry.
    expect(await screen.findByText('عمر')).toBeInTheDocument()
    expect(screen.getByText(i18n.t('audit.actionDELETE'))).toBeInTheDocument()
  })

  it('says who acted even when the account is gone', async () => {
    vi.mocked(auditApi.list).mockResolvedValue({
      total: 1,
      items: [entry({ userEmail: null })],
    })
    renderPage()

    expect(await screen.findByText(i18n.t('audit.unknownUser'))).toBeInTheDocument()
  })

  it('reports the untruncated total, not the number of rows on screen', async () => {
    vi.mocked(auditApi.list).mockResolvedValue({ total: 61, items: [entry()] })
    renderPage()

    expect(
      await screen.findByText(i18n.t('audit.showing', { count: 1, total: 61 })),
    ).toBeInTheDocument()
  })

  it('shows an empty state before anything has happened', async () => {
    vi.mocked(auditApi.list).mockResolvedValue({ total: 0, items: [] })
    renderPage()

    expect(await screen.findByText(i18n.t('audit.empty'))).toBeInTheDocument()
  })
})
```

- [ ] **Step 8: Run the screen test to verify it fails**

Run: `cd frontend && npx vitest run src/features/audit/AuditPage.test.tsx`
Expected: FAIL — cannot resolve `./AuditPage`.

- [ ] **Step 9: Write the screen**

Create `frontend/src/features/audit/AuditPage.tsx`. Read `frontend/src/features/reports/ReportsPage.tsx` first: it is the closest sibling — a read-only screen inside `AppShell` — and this one must match its section headers, table styling, loading state, and empty state rather than inventing a second look.

What the screen must do, and why:

- Wrap in `AppShell`, like every other screen, with `audit.title` and `audit.subtitle`.
- One row per entry, five columns: when, who, action, member, change.
- **When**: `createdAt` formatted with the active locale, so Arabic gets Arabic-Indic numerals — the treatment `MemberPanel`'s `formatDate` already applies.
- **Who**: `userEmail`, falling back to `audit.unknownUser` when null.
- **Action**: `t('audit.action' + entry.action)` — translated, never the raw `CREATE`. If a future writer emits an action this UI has no key for, fall back to the raw string rather than rendering an empty cell.
- **Member**: `subjectName(entry)`, falling back to `audit.unknownSubject`. Never a lookup against the members list.
- **Change**: `changedFields(parseValues(entry.oldValues), parseValues(entry.newValues))`, rendered field by field, before → after.
- Below the table, `audit.showing` with the count on screen and the untruncated `total`.
- Previous/Next buttons stepping `offset` by `AUDIT_PAGE_SIZE`; Previous disabled at offset 0, Next disabled once `offset + items.length >= total`.
- `audit.empty` when `total` is 0.
- A loading state matching whatever `ReportsPage` does while its query is in flight.

- [ ] **Step 10: Add the route**

In `frontend/src/routes/AppRoutes.tsx`, beside the `/reports` route:

```tsx
    <Route
      path="/audit"
      element={
        <ProtectedRoute>
          <AuditPage />
        </ProtectedRoute>
      }
    />
```

with the matching import.

- [ ] **Step 11: Turn the nav placeholder into a link**

In `frontend/src/app/AppShell.tsx`, the `hasPermission('Audit.View')` block currently renders a
`PendingNavItem` labelled `nav.audit`. Replace it with a real `Link` to `/audit`, matching how the
`/reports` and `/roles` links are written — same `navItemStyle(pathname === '/audit', true)` call —
and keep the existing clock icon exactly as it is.

Leave the `PublicLink.Create` `PendingNavItem` alone: public links are Phase 6 and genuinely have not shipped.

- [ ] **Step 12: Run the frontend suite**

Run: `cd frontend && npx vitest run`
Expected: PASS — including `AppShell.test.tsx`. If that file asserts the audit nav item is disabled, that assertion is now false: update it to assert the link instead, the way the move work replaced its own falsified test. Do not delete it.

Run: `cd frontend && npx tsc --noEmit`
Expected: silent.

- [ ] **Step 13: Commit**

```bash
git add frontend/src/features/audit frontend/src/routes/AppRoutes.tsx frontend/src/app/AppShell.tsx frontend/src/i18n
git commit -m "feat: add the audit trail screen

Names each row's subject from the entry's own values rather than by
looking the member up: a deleted member cannot be looked up, and those
are precisely the rows an audit trail exists to answer. The nav item
stops being a placeholder."
```

---

### Task 9: Full verification and documentation

**Files:**
- Modify: `README.md`
- Modify: `frontend/src/features/reports/ActivitySection.tsx` (one comment)
- Modify: `src/FamilyTree.Contracts/Reports/ActivityReport.cs` (one comment)

- [ ] **Step 1: Run every test**

Run: `dotnet test`
Expected: PASS — all four projects. Docker must be running.

Run: `cd frontend && npm test`
Expected: PASS — the whole suite.

- [ ] **Step 2: Run the linter and the type check**

Run: `cd frontend && npm run lint && npx tsc --noEmit`
Expected: no errors, and no warnings beyond the two pre-existing `react(only-export-components)` ones in `providers.tsx` and `AuthContext.tsx`.

- [ ] **Step 3: Correct the statements this work falsifies**

Both of these say an audit log does not exist. It does now — but the reports' Recent activity
section is still timestamp-derived, which is the part that stays true. Rewrite each to say that
precisely, rather than deleting the caveat:

- `src/FamilyTree.Contracts/Reports/ActivityReport.cs` — the remark about the missing `AuditLog` entity.
- `frontend/src/features/reports/ActivitySection.tsx` — the same caveat in the UI note.

Each should now say: recent activity is derived from record timestamps, so it cannot show
deletions or attribute a change to a user; the audit trail at `/audit` can do both, and rebuilding
this section on it is a follow-up.

- [ ] **Step 4: Document it**

Add to `README.md`, after the paragraph describing the move command:

```markdown
Every change to a family member is recorded in `audit_logs`: who did it, when, and the values
before and after as `jsonb`. Rows are insert-only — no update or delete path exists in the
codebase — and each one is staged into the same `SaveChanges` as the change it describes, so a
refused command records nothing and a successful one cannot fail to be recorded.

A create records the new member, an update the fields that command can change, a move both
parent ids, and a delete the entire member. That last one matters: deletion is a hard delete, so
the audit row is the only remaining record the member existed. Every row also carries the name,
which is what lets the trail name a member who is no longer there to look up.

`GET /api/v1/audit?limit=&offset=` returns a page newest-first behind the `Audit.View`
permission, with the untruncated total beside it; `limit` is capped at 50. The `/audit` screen
renders it. User, role, and authentication events are not audited yet — that is the next slice,
and it needs no schema change, since `EntityType` already carries the distinction.
```

- [ ] **Step 5: Commit**

```bash
git add README.md src/FamilyTree.Contracts/Reports/ActivityReport.cs frontend/src/features/reports/ActivitySection.tsx
git commit -m "docs: describe the audit trail

Records the two things a reader can get wrong: that the row is staged
into the command's own save rather than written separately, and that a
delete stores the whole member because nothing else survives it."
```

---

## Plan Self-Review

**Spec coverage.** Every section of the design maps to a task: §3 architecture → the file layout across Tasks 1–8; §3.1 insert-only by shape → Task 1, including the reflection test that asserts the absence; §3.2 atomicity → Task 3's staging test and Task 4's two refused-command tests; §3.3 action as a string → Task 1's `AuditActions`; §4 what each command records → Task 4, one test per action, with the name-on-every-row rule pinned by the move test; §5 contracts → Task 5; §6 API, ordering, clamping, tenant scoping → Tasks 5 and 6; §7 frontend, including "never a lookup" → Task 8's `subjectName` and its delete test; §8 rules 1–5 → Tasks 1 (rules 1–3), 3 (rule 5), 4 (rule 4); §9 testing → each task's own test step; §10 the unaudited remainder → recorded in Task 9's README text.

**Type consistency.** `AuditLog.Create(tenantId, userId, action, entityType, entityId, oldValues, newValues, now)` from Task 1 is called with that argument order in Task 3 and in Tasks 2 and 5's test seeds. `IAuditWriter.Record(action, entityType, entityId, oldValues, newValues)` from Task 3 is called with that order at all four sites in Task 4. `AuditEntryResponse`'s nine fields in Task 5 match `AuditEntry`'s nine in Task 7 field for field, camelCase across the boundary. `AuditLimits.MaxPageSize` (Task 5) and `AUDIT_PAGE_SIZE` (Task 7) are deliberately different names for different sides of the wire; the frontend constant matches `DefaultPageSize`, not `MaxPageSize`. `parseValues`, `subjectName`, and `changedFields` from Task 8 are used only within Task 8.

**Verification points**, flagged inline in the task that depends on each rather than assumed: whether `.IsDescending(bool[])` is available on this EF version (Task 2), that the generated migration touches nothing but the new table (Task 2), every existing `new FamilyMemberService(...)` site the constructor change breaks (Task 4), how endpoint groups are registered in `Program.cs` (Task 6), the fetch-stub naming in `membersApi.test.ts` (Task 7), and whether `AppShell.test.tsx` asserts the audit nav item is disabled (Task 8).

**Known behavioural change to an existing test.** `AppShell.test.tsx` may assert that the audit nav entry is a disabled placeholder. Task 8 Step 12 requires updating that assertion rather than deleting it, the same way the move work replaced the test whose premise it falsified.
