# Audit Logs — Design

**Date:** 2026-08-23
**Status:** Approved, ready for implementation planning
**Depends on:** Phase 4 (authorization), Move Member (2026-08-23)

## 1. Purpose

Record who changed which family member, when, and what the values were before and after.

This is the second piece of Phase 5, and the one three other things are waiting on: design
spec §4.6's audit insert, deferred when the move command shipped because no table existed;
the `Audit.View` permission, seeded in Phase 1 and unused ever since; and the audit report the
reports README records as blocked.

It also closes the gap a hard delete leaves. Members are deleted for real, not flagged, so
once a member is gone the audit row is the only record they were ever there (platform design
spec §3.7).

## 2. Scope

**In scope.** The `AuditLog` entity and its table, an `IAuditWriter` staged into the existing
commands, audit writes for the four family-member commands (create, update, move, delete), a
read endpoint behind `Audit.View`, and an `/audit` screen in the SPA with Arabic/English and
RTL support.

**Out of scope.** Auditing user, role, and authentication events — a later slice. Retention,
archival, and export of audit rows. Relationship history, the third Phase 5 item.

**Deliberately not folded in.** The reports screen's "Recent activity" section stays
timestamp-derived for now, so the app briefly holds two answers to "what changed lately" — one
that can show deletions and name a user, one that cannot. Rebuilding that section on audit
data is a follow-up slice, kept separate because it changes a shipped feature rather than
adding one.

## 3. Architecture

```
src/FamilyTree.Domain/Audit/
    AuditLog.cs                  the entity; insert-only by shape
    AuditActions.cs              CREATE, UPDATE, MOVE, DELETE

src/FamilyTree.Application/Audit/
    IAuditWriter.cs              Record(...) — stages, never saves
    IAuditService.cs             the read side
    AuditLimits.cs               page cap

src/FamilyTree.Contracts/Audit/
    AuditEntryResponse.cs, AuditPageResponse.cs

src/FamilyTree.Infrastructure/Audit/
    AuditWriter.cs               serializes values, adds to the tracked context
    AuditService.cs              the paged, tenant-scoped read
    (+ persistence configuration and one migration)

src/FamilyTree.Api/Endpoints/Audit/
    AuditEndpoints.cs            GET /api/v1/audit

frontend/src/features/audit/
    AuditPage.tsx, auditApi.ts, useAudit.ts, types.ts
```

### 3.1 Insert-only by shape, not by discipline

`AuditLog` has private setters, one static `Create` factory, and no mutating method. There is
no repository, no update path, and no delete path anywhere in the codebase — so making an
audit row lie means adding code that does not exist, rather than merely calling something that
does.

### 3.2 Atomicity comes free

`IAuditWriter.Record` does not save. It stages the entity on the tracked `DbContext`, and the
caller's existing `SaveChangesAsync` persists the mutation and the audit row together.

This is what makes SRS §33 — "if the audit insertion fails, the member move should also fail" —
true without restructuring a single command. `MoveAsync` already owns an explicit transaction.
Create, update, and delete own none and need none: EF wraps each `SaveChangesAsync` in its own
transaction, so one save covering both rows is already atomic.

A command that fails before its save writes no audit row at all, which is correct — nothing
happened, so there is nothing to record.

### 3.3 Action as a string

`Action` is a string constant from `AuditActions`, not an enum. These rows are read directly by
operators querying the table, and an enum would store integers needing a lookup — in the one
table whose entire purpose is being readable after the fact.

## 4. What each command records

| Command | Action | `old_values` | `new_values` |
|---|---|---|---|
| Create | `CREATE` | null | the full member |
| Update | `UPDATE` | name, dates, deceased flag — before | the same fields, after |
| Move | `MOVE` | `{ "parentId": "…" }` | `{ "parentId": "…" }` |
| Delete | `DELETE` | the full member snapshot | null |

Update records only the fields that command can change, because those are the only ones that
can differ; recording the whole member would bury one changed name in eight unchanged values.
Move records `parentId` alone, which is SRS §32's own worked example.

Delete is the exception that carries everything. After a hard delete the row is gone, so its
`old_values` is the last remaining evidence the member existed — including the name, which the
viewer needs precisely because the member can no longer be looked up.

Values are serialized with the same `System.Text.Json` camelCase policy the API uses, so what
an operator reads in the table matches what the API returns.

## 5. Contracts

```csharp
/// <summary>
/// One recorded change. <paramref name="OldValues"/> and <paramref name="NewValues"/> are raw
/// JSON as stored — the shape varies by action, so the client renders them generically rather
/// than binding them to a member DTO that a DELETE row would not satisfy.
/// </summary>
public sealed record AuditEntryResponse(
    Guid Id,
    Guid UserId,
    /// Resolved at read time by joining the user store. Null when the account has since been
    /// deleted — the id is kept regardless, because it is what the stored row actually holds.
    string? UserEmail,
    string Action,
    string EntityType,
    Guid EntityId,
    string? OldValues,
    string? NewValues,
    DateTimeOffset CreatedAt);

/// <summary>A page of entries, newest first, with the untruncated total beside it.</summary>
public sealed record AuditPageResponse(int Total, IReadOnlyList<AuditEntryResponse> Items);
```

`Total` is the untruncated count, following the rule the reports endpoint established: a client
must be able to say "50 of 1,284", never `items.length`.

The email is resolved when the page is read rather than copied into the row at write time. An
audit row must record what was true when the change happened, but an email is an attribute of
the account rather than of the change — copying it would leave the trail showing an address the
person no longer uses. The id is what the row stores, and the id is what identifies them; the
email is a convenience for the reader, which is why it is allowed to be null.

## 6. API and authorization

`GET /api/v1/audit?limit=&offset=`, guarded by `.RequirePermission(Permissions.Audit.View)`.
The constant and its membership in the system roles were seeded in Phase 1; no seed change is
needed.

Newest first, ordered by `(created_at DESC, id DESC)` — `id` breaks ties, because two changes
inside one transaction share a timestamp and an unstable order would make paging skip or repeat
rows. `limit` is clamped 1–50 and `offset` floors at 0, matching the search endpoint.

Tenant scoping is the EF global query filter, as everywhere else, so another tenant's rows are
not merely forbidden but invisible.

## 7. Frontend

An `/audit` screen listing what happened: the action, which member, who did it, when, and the
values that changed. The nav entry is gated on `Audit.View`, so a user without the permission
never sees a link to a screen that would 403 them.

**The member's name comes from the audit row's own values, never from a lookup.** A deleted
member cannot be looked up — that is the entire reason the snapshot is stored — and a viewer
resolving names against the members list would show every deleted member as blank, which is
exactly the case an audit trail exists to answer.

The old and new values render as a readable field-by-field diff rather than raw JSON, but
generically: the client does not know which fields any given action carries, and must not break
when a later slice audits users or roles with a different shape.

Timestamps use the same locale-aware formatting as the rest of the app, so Arabic gets
Arabic-Indic numerals per the design system's numeral rule.

## 8. Rules

1. **Rows are never updated or deleted.** No code path exists, and a test asserts the absence.
2. **Every row is tenant-scoped and user-attributed** from `ITenantContext`, which already
   carries both. Neither is ever taken from a header, query string, or route value.
3. **`CreatedAt` comes from the injected `TimeProvider`**, never `DateTimeOffset.UtcNow`.
4. **A failed command records nothing.** The audit row shares the command's save, so a rollback
   takes both.
5. **The writer never saves.** A writer calling `SaveChangesAsync` itself would commit the audit
   row separately from the change it describes, which is the one thing §3.2 exists to prevent.

## 9. Testing

**Domain unit tests:** the factory rejects an empty tenant or user id; a created row carries the
action, entity type, entity id, and timestamp it was given.

**Integration tests**, where the rules that matter live:

- each of the four commands writes exactly one row, with the right action and the right values
- a rejected command — a cycle-refused move, a delete blocked by children, a stale-version
  update — writes NO row, proving the rollback takes both
- a deleted member's row still carries their name, after the member row is gone
- the endpoint orders newest first and pages stably across a tie in `created_at`
- another tenant's rows are invisible, not merely forbidden
- a caller without `Audit.View` is refused

**Frontend tests:** the screen renders an entry per action type, shows a deleted member's name
from the snapshot, hides the nav entry without the permission, and renders an empty state.

## 10. What this does not solve

User, role, and authentication events are still unaudited, so "who deactivated this account"
has no answer yet. That is the next audit slice, and it needs no schema change — `EntityType`
already carries the distinction.
