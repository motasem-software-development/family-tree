# Move Member — Design

**Date:** 2026-08-23
**Status:** Approved, ready for implementation planning
**Depends on:** Phase 2 (family tree), Phase 4 (authorization)

## 1. Purpose

Re-parent a member without deleting and re-creating them. This is the first piece of Phase 5
("Advanced Tree Operations": move member, cycle detection, relationship history, audit logs)
and the one the existing UI is already waiting on.

Three surfaces render a disabled Move button today — `MemberPanel`, the canvas context menu,
and the blocked-delete dialog — and `UpdateFamilyMemberRequest` carries `ParentId` for the sole
purpose of rejecting it. A member attached to the wrong ancestor, which is the normal outcome
of importing a hand-kept tree, currently has no repair path at all: the delete that would undo
it is refused while they have children, and Move is what that refusal points at.

## 2. Scope

**In scope.** A `MoveTo` command on the domain entity, cycle detection in Infrastructure, a
dedicated endpoint, the contracts and error codes, unit and integration tests, and a
search-and-pick move dialog in the SPA with Arabic/English and RTL support.

**Out of scope.** Relationship history and audit logs, the other two items of Phase 5. Moving
more than one member at a time. Moving a member between family trees or tenants, which is not
a repair operation but a data migration and has no UI to ask for it.

**Deviation from the approved design spec.** §4.6 requires that move run in a single
transaction covering the state change *and* an audit insert. No `audit_logs` table or entity
exists yet, so this ships without the audit half, recorded here the way Phase 2 recorded its
deferred trigram index. The transaction is built anyway, so adding the insert later is one
statement inside a block that already exists rather than a restructuring.

## 3. Architecture

The rule splits across two layers because the two halves know different things. The entity can
see itself, so it owns self-parenting and the version bump; only the database can see the
ancestor chain, so it owns the cycle check.

```
src/FamilyTree.Domain/FamilyMembers/
    FamilyMember.cs              + MoveTo(newParentId, now)

src/FamilyTree.Contracts/FamilyMembers/
    MoveFamilyMemberRequest.cs   parentId, version

src/FamilyTree.Application/FamilyMembers/
    IFamilyMemberService.cs      + MoveAsync(id, request, ct)

src/FamilyTree.Infrastructure/FamilyMembers/
    FamilyMemberService.cs       + MoveAsync — transaction, lock, checks, save
    CycleCheckQuery.cs           the recursive CTE, alone in its own file

src/FamilyTree.Api/Endpoints/FamilyMembers/
    FamilyMemberEndpoints.cs     + POST /{id}/move

frontend/src/features/tree/
    MoveDialog.tsx               search-and-pick target chooser
    TreePage.tsx                 wiring, invalidation, toast
    MemberPanel.tsx              Move enabled
    MemberActions.tsx            context menu and blocked-delete copy
```

### 3.1 Cycle detection

A recursive CTE walking upward from the *proposed parent*, executed inside the move
transaction (design spec §3.5). Not an in-memory loop: it is one query regardless of depth,
and it reads the same snapshot the update writes. If the walk reaches the member being moved,
the member is an ancestor of its own proposed parent and the move is refused.

The walk terminates because the tree is acyclic, which is the invariant this check exists to
preserve — so a cycle already in the data would make it non-terminating. A depth bound well
past any real genealogy (100) is not a substitute for correctness, but it turns a
corrupted-data case from a hung connection into an error, and costs nothing on healthy data.

### 3.2 Concurrent moves

Two moves can each be acyclic against the snapshot they read and jointly form a cycle: move A
under B and B under A, committed at the same instant. The check-then-save race is the same one
the last-administrator rule faces, and it gets the same answer — `pg_advisory_xact_lock` on a
bigint folded from the tenant GUID, taken at the top of the transaction, exactly as
`AdministratorGuard.SerializeOnTenantAsync` does it. Moves within one tenant serialize; moves
across tenants do not. This is a rare administrative operation, so the contention cost is
theoretical and the correctness gain is not.

### 3.3 Generation

Unchanged, because generation is never stored (design spec §3.6). It is computed during tree
assembly, so a moved subtree renumbers itself on the next read, and the reports that count
generations follow without knowing a move happened.

## 4. API and authorization

`POST /api/v1/family-members/{id}/move`, body `{ parentId: Guid?, version: int }`, returning
the updated `FamilyMemberResponse`. A dedicated command rather than a `PUT` field, per design
spec §4.6 — and `PUT` goes on rejecting `parentId` outright, unchanged by this work.

Guarded by `.RequirePermission(Permissions.Member.Move)`. Both the constant and its membership
in the four system roles were seeded in Phase 1; no seed change is needed.

| Situation | Status | Code |
|---|---|---|
| No such member, or another tenant's | 404 | `MEMBER_NOT_FOUND` |
| No such target parent, or another tenant's, or another tree's | 404 | `MEMBER_NOT_FOUND` |
| Target is the member itself | 409 | `MOVE_CREATES_CYCLE` |
| Target is a descendant of the member | 409 | `MOVE_CREATES_CYCLE` |
| `version` is not the one on the row | 409 | `CONCURRENCY_CONFLICT` |

Cross-tenant is 404, never 403, per design spec §4.4: a 403 would confirm the id exists.

A missing target is reported as `MEMBER_NOT_FOUND` rather than a distinct `PARENT_NOT_FOUND`,
because from the client's side both mean the same thing — one of the two ids in this request
does not name a member here — and the dialog only ever offers targets it has just read.

## 5. Contracts

```csharp
/// <summary>
/// Re-parents a member. A null <paramref name="ParentId"/> makes them first-generation,
/// attached to the family tree itself rather than to a member (BR-003).
/// <paramref name="Version"/> is the value from the last read and is required — omitting it
/// is a stale write by definition.
/// </summary>
public sealed record MoveFamilyMemberRequest(Guid? ParentId, int Version);
```

Nothing else is added. The response is the existing `FamilyMemberResponse`, so a client that
already knows how to read a member after an edit knows how to read one after a move.

## 6. Rules

1. **Self-parenting is refused** by the entity, as `MOVE_CREATES_CYCLE`. A self-loop is the
   degenerate cycle; giving it its own code would ask the client to translate two messages for
   one mistake.
2. **Descendant targets are refused** by the CTE, as the same code.
3. **A move to `null` is a promotion to first generation**, always legal. BR-003 already models
   first-generation members that way, and without it a mistake at the top of the tree is
   unrepairable through the UI.
4. **Moving to the parent a member already has succeeds** and costs a version bump like any
   other write. It changes nothing, harms nothing, and rejecting it would be a third error code
   for a case no user can distinguish from success.
5. **`Guid.Empty` is normalized to null**, as `FamilyMember.Create` already does, so it can
   never reach the database and fail a foreign key at write time.
6. **The target must belong to the same tenant and the same family tree.** Cross-tree moves are
   out of scope, and the check is what keeps them out.

## 7. Frontend

`MoveDialog` opens from any of the three surfaces that offer Move. It searches members through
the existing `membersApi.search`, whose hits already carry an ancestor path — the field that
tells the many repeated names apart, and the reason §5.4 of the design spec asked for it. The
family tree itself is offered as the first target in the list and sends `parentId: null`.

The member itself and its descendants are shown disabled with the reason stated, computed
client-side from the tree already in memory. This is a courtesy, not the rule: the server's CTE
remains the only authority, and a stale client that asks anyway gets a translated 409.

On success the dialog closes, a toast names the moved member by their composed name, and both
the tree and members queries are invalidated — the tree because its shape changed, the flat
list because `parentId` and `version` did.

The Move button loses its `disabled` in `MemberPanel` and the context menu, gated on
`hasPermission('Member.Move')` the way the other three actions are gated. The blocked-delete
dialog keeps its shape but stops describing Move as a later phase: it is now the way out it was
always meant to be. `MOVE_CREATES_CYCLE` joins the error map in `ar.json` and `en.json`, whose
key parity `locales.test.ts` already enforces.

## 8. Testing

**Domain unit tests:** self-move refused with the coded exception; a legal move bumps the
version and touches the timestamp; `Guid.Empty` normalizes to null.

**Integration tests**, which is where the rules that matter actually live:

- a valid re-parent, read back through the tree endpoint with the subtree renumbered
- a three-level cycle refused: moving a grandparent under its own grandchild
- a self-move refused through the endpoint, not only the entity
- a promotion to first generation
- a target in another tenant, and one in another tree, both 404
- a stale `version`, 409
- the permission guard: a caller without `Member.Move` is refused
- two concurrent moves that jointly form a cycle — the case §3.2 exists for, run against real
  transactions so the advisory lock is what makes it pass

**Frontend tests:** descendants offered disabled, the first-generation target sending null, the
Move button enabled only with the permission, and the translated cycle error on a 409.

## 9. What this does not solve

The tree still has no history: after a move, nothing records where the member used to hang.
That is relationship history and audit logs, the rest of Phase 5, and both are blocked on the
same missing `audit_logs` table (§2).
