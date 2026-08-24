# Derivation and the Shared Filtered Query — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Derive each member's branch and generation from the existing parent chain, and put one
filter set — name, status, branch, generation, country — behind both the members list and the
family-tree view, plus the two reference endpoints the filter UI will need.

**Architecture:** One recursive walk downward from the selected root produces branch and
generation together (spec §3). It exists twice, deliberately: as raw SQL in `FamilyMemberQuery`
for the flat list, and as a pure in-memory function `MemberDerivation` for the tree, which
already loads every member and shapes it in process (spec §4.2). An integration test asserts the
two agree on the seeded family, so the duplication cannot drift silently. The filter shape is
one record, `MemberFilterRequest`, bound by every endpoint that filters, so a filter added later
cannot reach one caller and miss another.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, PostgreSQL, xunit + FluentAssertions, React 19 +
TanStack Query, react-i18next, vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-08-24-member-data-filters-export-design.md`

This is **Plan 2 of 4** from the spec's §9 decomposition, and the one the spec singles out as
carrying the tenant-isolation risk. Plan 1 (member contact data) is complete on this branch —
see `docs/superpowers/plans/2026-08-24-member-contact-data.md` and `git log main..HEAD`; do not
re-run it. Plan 3 (filter UI) and Plan 4 (Excel export) both consume what this plan produces.

**No UI ships here.** The frontend changes in Task 9 are types, an API surface, and query hooks
only — no component renders a filter control until Plan 3.

Section references of the form §N point at the design spec above, or — where the text says
"specification §N" — at the source requirement document it implements.

---

## STATUS: COMPLETE (2026-08-24)

All nine tasks are done and verified. Do **not** re-run them — check `git log main..HEAD`
before assuming otherwise; the commits are the authority, this heading is a summary.

**Verified end to end:** backend 701 tests pass (Domain 96, Import 43, Application 267,
Integration 295), frontend 302 pass, lint and build clean, en/ar key parity clean. Every item
under "Verification" below was exercised against the running API on the seeded 351-member
family: the root reads branch `null` / generation 0, `?status=dead` returns 400
`FILTER_INVALID_STATUS`, `/branches` returns داوود's four children, `/generations` returns
`[0..9]`, and searching the tree for a deep name kept 57 nodes of 351 — 18 matches plus their
ancestor chains, every ancestor marked `matches: false`.

### Two decisions taken while implementing

- **`FamilyTreeNodeResponse.Matches` defaults to `true`.** That is what "no filter applied"
  means, and the safe failure for a construction site that forgets it is a visible member rather
  than an invisible one. It also spared twenty-odd export test fixtures that build nodes
  positionally a mechanical edit carrying no signal. The assembler always passes it explicitly.
- **The cross-tenant test had to be strengthened.** The first version grafted a stowaway from
  another tenant onto the host's tree and asserted it was absent — and it passed with the
  `tenant_id` predicate removed from the recursive term, because the outer join's own predicate
  caught it. It now hangs a **host** member off that stowaway: reachable only by walking through
  another tenant's row, and caught by nothing else. Both cross-tenant tests were confirmed to
  fail with the predicate removed and pass with it restored.

### What is NOT done

Plans 3 and 4 from spec §9. No UI ships here: no filter bar, no sheet, no active-count badge,
no Country or Branch column, no dimming in the tree, and no Excel export. `filterParams.ts`,
`useBranchesQuery`, `useGenerationsQuery` and `FamilyMemberListItem` exist for Plan 3 to build
against; nothing renders them yet.

---

## Global Constraints

- Target framework `net10.0`; `Nullable` enable; `TreatWarningsAsErrors` true (Directory.Build.props) — a warning fails the build.
- Branch: `member-data-filters-export`, already cut from `main`. Do not create another branch.
- **No migration in this plan.** Every column and index the query needs already exists —
  `country_id` and `is_deceased` were indexed in Plan 1 (spec §2.3), branch and generation are
  derived and deliberately not stored (spec §2.5). If a step seems to need a migration, stop:
  something has gone wrong.
- Test frameworks are fixed: xunit 2.9.3 + FluentAssertions 7.2.0 (backend), vitest 4 + Testing Library (frontend). Do not add test packages.
- Every new user-facing string must be added to **both** `frontend/src/i18n/locales/en.json` and `ar.json`. `locales.test.ts` enforces key parity and will fail the suite otherwise.
- Every new domain error code must get an `errors.<CODE>` entry in both locale files.
- Arabic test fixtures use real Arabic names (`سليمان`, `داوود`), matching existing tests.
- Raw SQL carries an explicit `tenant_id` predicate on **every** table reference, the recursive
  term included. This is the house rule stated in `FamilyMemberSearchQuery`'s class comment and
  restated in spec §3.1; it is not optional and not defensive.
- Filter endpoints keep the permission the endpoint already carries. **No new permission is
  introduced** (spec §5.2): `family-members` keeps `Member.View`, the three `family-tree/*`
  endpoints keep `FamilyTree.View`.

### The worked example

Spec §8 requires §21's worked example to be reproduced as a literal test table rather than
paraphrased. This is that table, and Tasks 2 and 4 both assert against it:

```
داوود                     branch = (none → "Root")   generation 0
├── سليمان                branch = سليمان             generation 1
│   ├── فارس              branch = سليمان             generation 2
│   │   └── محمود         branch = سليمان             generation 3
│   └── خالد              branch = سليمان             generation 2
└── عمر                   branch = عمر                generation 1
    └── يوسف              branch = عمر                generation 2
```

محمود is the point of the example: generation 3, branch سليمان. Branch is *which* subtree,
generation is *how deep* — spec §30 calls the distinction fundamental, and the two answers
differ for every member below generation 1.

### Refinement of spec §1.3

Spec §1.3 says "If several parentless members ever exist, each parentless member is itself a
branch." The CTE in spec §3 — which §3 calls "the entire branch rule" — does not do that: with
no `rootId`, every parentless member is an anchor row with `branch_id IS NULL` and
`generation 0`, so each reads as **Root**, not as its own branch.

**Resolution: §3's CTE wins.** It is the normative statement, it is the one with code in it, and
the data has exactly one parentless member (داوود), so the two readings differ only on a shape
that does not exist. Selecting one of those roots as `rootId` gives the §1.3 answer anyway. This
is recorded so a later reader does not "fix" the CTE into disagreeing with the spec.

### Generation numbering, and the one place it is not root-relative

Spec §1.2 is precise and easy to get half-right:

- `MemberDerivation` and `FamilyMemberQuery` produce **root-relative** generations, root = 0.
  The generation *filter* and the generations endpoint use these.
- `FamilyTreeNodeResponse.Generation` keeps its existing **absolute 1-based** value. Plan 3
  moves the two *display* sites (`MemberPanel.tsx`, `TreePage.tsx`) to root-relative; the node's
  field is consumed by the PDF export and the reports page too, and is not this plan's to
  change.

So on the tree path the assembler compares the filter against a root-relative number it derives
separately, while still emitting the absolute one. Task 6 states this in a code comment, because
it is exactly the kind of thing that reads like a bug.

---

## Task 1: The shared filter shape

`MemberFilterRequest` is the wire shape — bound straight off the query string by every endpoint
that filters. `MemberFilter` is its validated form, with `Status` parsed and blank strings folded
to null, so nothing downstream re-parses. Two records rather than one because spec §5.1 wants an
unrecognised `status` to be a 400 `FILTER_INVALID_STATUS` rather than a silent default, and a
bound record cannot refuse to bind.

**Files:**
- Create: `src/FamilyTree.Contracts/FamilyMembers/MemberFilterRequest.cs`
- Create: `src/FamilyTree.Application/FamilyMembers/MemberFilter.cs`
- Test: `tests/FamilyTree.Application.Tests/FamilyMembers/MemberFilterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `MemberFilterRequest` (record, `[AsParameters]`-bindable); `MemberStatusFilter` enum
  (`All`, `Alive`, `Deceased`); `MemberFilter` with `MemberFilter.None`,
  `MemberFilter.TryCreate(MemberFilterRequest, out MemberFilter)` and `bool IsEmpty`. Used by
  Tasks 3, 4, 5, 6, 7, 8 and by Plan 4's export endpoint.

- [x] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/FamilyMembers/MemberFilterTests.cs` asserts:

- `TryCreate` with every field null yields `MemberFilter.None` and `IsEmpty` is true.
- `status` parses case-insensitively: `"all"`, `"ALIVE"`, `"Deceased"` all succeed.
- `status = "dead"` returns false — the caller turns that into `FILTER_INVALID_STATUS`.
- `status = null` and `status = ""` both mean `All` and leave `IsEmpty` true. An absent parameter
  and an empty one arrive identically over the wire and must not diverge.
- `search = "   "` folds to null and leaves `IsEmpty` true; `search = " فارس "` is trimmed.
- `IsEmpty` is false when any one of branch, generation, country, or search is set.
- `RootId` does **not** affect `IsEmpty`: it selects the root, it does not filter. Getting this
  wrong makes an unfiltered subtree view take the expensive path and report itself as filtered.

A negative or unknown `generation` is deliberately **not** an error: it is a filter nothing
matches, and it returns an empty list. Do not invent a code the spec does not have.

- [x] **Step 2: Write `MemberFilterRequest`**

```csharp
namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// The filter set, bound from the query string by every endpoint that filters — the members
/// list, the tree view, and (Plan 4) the Excel export. One shared record is what makes spec
/// §15's combinability structural: a filter added here reaches every caller at once, so none can
/// quietly support a subset (design §5.1).
///
/// An absent parameter means "no filter". <paramref name="Status"/> is a string rather than an
/// enum because an unrecognised value is a 400 with a code (design §5.1), and model binding
/// cannot refuse — <c>MemberFilter</c> is where it becomes typed.
///
/// <paramref name="RootId"/> is not a filter. It selects the root that branch and generation are
/// measured from (design §1.3), and it rides in this record rather than as a separate parameter
/// so the export can be handed the page's exact query string unchanged.
/// </summary>
public sealed record MemberFilterRequest(
    string? Search,
    string? Status,
    Guid? BranchId,
    int? Generation,
    int? CountryId,
    Guid? RootId);
```

- [x] **Step 3: Write `MemberFilter`**

In `src/FamilyTree.Application/FamilyMembers/MemberFilter.cs`: the `MemberStatusFilter` enum and
the validated record.

```csharp
public enum MemberStatusFilter { All, Alive, Deceased }

public sealed record MemberFilter(
    string? Search,
    MemberStatusFilter Status,
    Guid? BranchId,
    int? Generation,
    int? CountryId,
    Guid? RootId)
{
    public static MemberFilter None { get; } = new(null, MemberStatusFilter.All, null, null, null, null);

    /// <summary>
    /// True when nothing is being filtered out. RootId is excluded on purpose: it changes what
    /// the numbers are measured from, not which members come back.
    /// </summary>
    public bool IsEmpty =>
        Search is null && Status is MemberStatusFilter.All &&
        BranchId is null && Generation is null && CountryId is null;

    /// <summary>Returns false only for an unrecognised status (design §5.1).</summary>
    public static bool TryCreate(MemberFilterRequest request, out MemberFilter filter) { /* ... */ }
}
```

`TryCreate` trims `Search` and folds blank to null; maps `null` and `""` to `All` and otherwise
matches `alive` / `deceased` / `all` with `StringComparison.OrdinalIgnoreCase`.

- [x] **Step 4: Run the tests** — `dotnet test tests/FamilyTree.Application.Tests` passes.
- [x] **Step 5: Commit** — `feat: add the shared member filter shape`

---

## Task 2: `MemberDerivation` — branch and generation, in memory

The pure twin of the CTE, and the one the worked-example table is asserted against. The tree page
already holds every member in process (spec §4.2), so it derives rather than re-queries.

**Files:**
- Create: `src/FamilyTree.Application/FamilyMembers/MemberDerivation.cs`
- Test: `tests/FamilyTree.Application.Tests/FamilyMembers/MemberDerivationTests.cs`

**Interfaces:**
- Consumes: `FamilyMember` (Domain).
- Produces: `readonly record struct MemberPlacement(Guid? BranchId, int Generation)` and
  `MemberDerivation.Derive(IReadOnlyList<FamilyMember> members, Guid? rootId) -> IReadOnlyDictionary<Guid, MemberPlacement>`.
  Used by Tasks 3 and 6.

- [x] **Step 1: Write the failing test**

Reproduce the worked-example tree above as a fixture, then assert it as a literal table —
`[InlineData("محمود", "سليمان", 3)]` and so on for all seven members — rather than as prose. Also
assert:

- With no `rootId`, داوود is the root: `BranchId` null, generation 0.
- With `rootId` = سليمان, سليمان is the root (branch null, generation 0), فارس and خالد are their
  own branches at generation 1, and محمود is branch فارس at generation 2. The same member answers
  differently under a different root, which is the whole reason the root is a parameter.
- A member outside the selected subtree is **absent from the dictionary**, not present with a
  null placement. Absence is what the tree filter uses to prune.
- An unknown `rootId` yields an empty dictionary.
- A parent chain that loops terminates instead of hanging: the walk visits each id at most once.
  Cycles are impossible through the move command, and this is what keeps a corrupt import from
  becoming a hung request rather than an error.

- [x] **Step 2: Implement `Derive`**

Iterative breadth-first from the anchor set, mirroring the CTE exactly:

```csharp
// COALESCE(t.branch_id, c.id) is the entire branch rule (design §3): a direct child of the root
// has no parent branch, so it becomes its own branch, and every descendant inherits it
// unchanged. Iterative rather than recursive so a long chain cannot exhaust the stack.
var branchId = parent.BranchId ?? child.Id;
```

Anchors are the member with `rootId` when supplied, otherwise every member with a null
`ParentId`; each anchor gets `(null, 0)`. Children come from one `GroupBy(ParentId)` pass, so the
whole derivation is linear in the input. A `visited` set guards the cycle case.

- [x] **Step 3: Run the tests** — `dotnet test tests/FamilyTree.Application.Tests` passes.
- [x] **Step 4: Commit** — `feat: derive a member's branch and generation from the parent chain`

---

## Task 3: `MemberFilterPredicate` — the four-way AND

Spec §15's combinability, isolated and unit-tested. The tree assembly (Task 6) uses it, and
Task 4 cross-checks the SQL against it, so "what does `status=alive` mean" has one answer on this
side of the boundary.

**Files:**
- Create: `src/FamilyTree.Application/FamilyMembers/MemberFilterPredicate.cs`
- Test: `tests/FamilyTree.Application.Tests/FamilyMembers/MemberFilterPredicateTests.cs`

**Interfaces:**
- Consumes: `MemberFilter`, `MemberPlacement`, `FamilyMember`.
- Produces: `MemberFilterPredicate.Matches(FamilyMember member, MemberPlacement placement, MemberFilter filter) -> bool`.
  Used by Task 6.

- [x] **Step 1: Write the failing test**

- Each predicate alone: search (case-insensitive substring, Arabic), status alive, status
  deceased, branch, generation, country.
- All four of spec §15's axes at once, asserted both ways: a member satisfying all four matches,
  and the same member fails when any single axis is changed. A four-way AND that is accidentally
  an OR passes every single-axis test.
- `MemberFilter.None` matches everything, including a member with no country and no dates.
- `BranchId` naming the root's own id matches nobody: the root's branch is null, and `Root` is a
  rendering of "no branch", not a branch you can filter by.
- Search matches on **name only**. It must not match a national ID or a phone number — those are
  contact details, and a name box that silently searches them discloses more than the user asked
  for.

- [x] **Step 2: Implement `Matches`**

Plain `&&` across the supplied predicates; a null filter field is skipped. Search uses
`CultureInfo.InvariantCulture` with `CompareOptions.IgnoreCase` `IndexOf`, matching `ILIKE`'s
behaviour on the Arabic corpus closely enough that Task 4's cross-check test holds.

- [x] **Step 3: Run the tests** — passes.
- [x] **Step 4: Commit** — `feat: combine the member filters with a single AND`

---

## Task 4: `FamilyMemberQuery` — the recursive CTE

The second raw-SQL surface in the codebase. It follows `FamilyMemberSearchQuery` file-for-file:
the same class shape, the same parameter helper, the same tenant argument in a class comment.
**This is the task spec §9 says warrants the most scrutiny.**

**Files:**
- Create: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberQuery.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberQueryTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `MemberFilter`, `FamilyMemberListItem` (Task 5).
- Produces: `internal static` `FamilyMemberQuery.ListAsync(...)`, `ListBranchesAsync(...)`,
  `ListGenerationsAsync(...)`. Used by Tasks 5 and 7, and by Plan 4's exporter.

- [x] **Step 1: Write the failing tests**

In `FamilyMemberQueryTests`, against real PostgreSQL via the existing `DatabaseTestBase`:

- The worked-example tree, seeded, produces the worked-example table — the same `[InlineData]`
  rows as Task 2. This is the test that pins the SQL to the pure function.
- **The cross-tenant branch walk.** Seed two tenants. Give tenant B a member whose `parent_id`
  points at a tenant A member, written directly rather than through the aggregate. Walk from
  tenant A's root and assert tenant B's member is absent. Without the `tenant_id` predicate in
  the **recursive** term this test fails and every other test in the file still passes — which is
  exactly why it is written out separately rather than folded into another case.
- An empty tenant id (`Guid.Empty`, an unauthenticated caller) returns nothing and runs no walk.
  Fail closed, before any SQL, as `FamilyMemberSearchQuery` does.
- Each filter alone narrows the result; all four together narrow it further.
- A search term containing `%` matches literally, not everything — the same `EscapeLikePattern`
  hazard `FamilyMemberSearchQuery` documents.
- `ListBranchesAsync` returns the root's direct children, ordered by name, and excludes the root.
- `ListGenerationsAsync` returns `[0, 1, 2, 3]` for the worked example — distinct and ascending.
- Branches and generations ignore the *other* filter fields: they describe what is available to
  filter by, so narrowing them by the current filter would make a dropdown that erases its own
  options as soon as you use one.

- [x] **Step 2: Write the SQL**

The CTE is spec §3's, verbatim, with the projection and predicates around it:

```sql
WITH RECURSIVE tree AS (
    SELECT m.id, NULL::uuid AS branch_id, 0 AS generation
    FROM family_members m
    WHERE m.tenant_id = @tenant_id
      AND (CASE WHEN @root_id IS NULL THEN m.parent_id IS NULL ELSE m.id = @root_id END)
  UNION ALL
    SELECT c.id, COALESCE(t.branch_id, c.id), t.generation + 1
    FROM tree t
    JOIN family_members c ON c.parent_id = t.id AND c.tenant_id = @tenant_id
)
SELECT m.id, m.name, m.parent_id, m.version, m.created_at, m.updated_at,
       m.date_of_birth, m.date_of_death, m.is_deceased,
       m.national_id, m.mobile_number, m.whats_app_number, m.country_id,
       co.code AS country_code,
       t.branch_id, b.name AS branch_name, t.generation
FROM tree t
JOIN family_members m ON m.id = t.id AND m.tenant_id = @tenant_id
LEFT JOIN countries co ON co.id = m.country_id
LEFT JOIN family_members b ON b.id = t.branch_id AND b.tenant_id = @tenant_id
WHERE (@search      IS NULL OR m.name ILIKE @search ESCAPE '\')
  AND (@is_deceased IS NULL OR m.is_deceased = @is_deceased)
  AND (@branch_id   IS NULL OR t.branch_id   = @branch_id)
  AND (@generation  IS NULL OR t.generation  = @generation)
  AND (@country_id  IS NULL OR m.country_id  = @country_id)
ORDER BY m.name, m.id
LIMIT @limit OFFSET @offset;
```

Notes that belong in the file as comments, not only here:

- `countries` is the one join with **no** `tenant_id` predicate, and that is correct: it is
  system-level reference data with no global query filter (spec §2.1). Every other table
  reference has one, the recursive term included.
- The `b` self-join resolves the branch *name* in the same pass. `LEFT`, because the root's
  `branch_id` is null and the root must still come back — an inner join would silently drop it.
- `@is_deceased` is a nullable bool, not the status string: `All` binds `DBNull`, which makes the
  predicate a no-op. Translating at the parameter is what keeps the SQL free of a three-valued
  enum.
- `LIMIT` / `OFFSET` exist because spec §5.3 says the contract must not have to change when
  pagination arrives. Callers pass `int.MaxValue` / `0` today; the members list stays unpaginated.

Branches and generations reuse the same CTE text with a different tail:

```sql
-- branches: the direct children of the root, which is what branch_id can be
SELECT b.id, b.name FROM tree t
JOIN family_members b ON b.id = t.id AND b.tenant_id = @tenant_id
WHERE t.generation = 1 ORDER BY b.name, b.id;

-- generations
SELECT DISTINCT generation FROM tree ORDER BY generation;
```

Keep the CTE in one `private const string` and concatenate the tails, so there is literally one
copy of the walk to review.

- [x] **Step 3: Write the reader**

`AddParameter` copied from `FamilyMemberSearchQuery`, plus a nullable overload that binds
`DBNull.Value` for an absent filter value. Open and close the connection around the whole call,
as that class does. Map rows into `FamilyMemberListItem`.

- [x] **Step 4: Run the tests** — `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FamilyMemberQueryTests` passes. Docker must be running.
- [x] **Step 5: Commit** — `feat: derive branch and generation in one tenant-safe walk`

---

## Task 5: The filtered members list

`ListAsync` grows a filter and starts returning branch and generation. A new response record
rather than three nullable fields on `FamilyMemberResponse`: the single-member endpoints have no
root to measure from, and a nullable `Generation` that means "not applicable here" rather than
"unknown" is the kind of field that gets misread once and then relied on.

**Files:**
- Create: `src/FamilyTree.Contracts/FamilyMembers/FamilyMemberListItem.cs`
- Modify: `src/FamilyTree.Application/FamilyMembers/IFamilyMemberService.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyMembers/FamilyMemberService.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/FamilyMembers/FamilyMemberServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `FamilyMemberQuery`, `MemberFilter`.
- Produces: `FamilyMemberListItem`; `IFamilyMemberService.ListAsync(MemberFilter, CancellationToken)`.
  Used by Tasks 8, 9 and Plan 4.

- [x] **Step 1: Write the failing test**

Extend `FamilyMemberServiceTests`: an unfiltered list still returns every member ordered by name
(the existing guarantee must not regress); a status filter narrows it; every row carries a branch
name, or null for the root; the root's generation is 0.

- [x] **Step 2: Write `FamilyMemberListItem`**

Every field of `FamilyMemberResponse` plus `Guid? BranchId`, `string? BranchName`,
`int Generation`. Flat rather than nested — the client renders one table row per item, and a
nested `Member` object would buy nothing but a level of indirection.

- [x] **Step 3: Change `ListAsync`**

Signature becomes
`Task<IReadOnlyList<FamilyMemberListItem>> ListAsync(MemberFilter filter, CancellationToken ct = default)`.
The body delegates to `FamilyMemberQuery.ListAsync`, exactly as `SearchAsync` delegates to
`FamilyMemberSearchQuery` — including the comment recording that this read does **not** go
through the tenant query filter and why that is still safe.

No back-compat overload. The one-argument `ListAsync` is deleted and every caller updated; Plan
1's Task 6 already settled that question the same way ("remove Update back-compat overload per
coordinator ruling").

- [x] **Step 4: Run the tests** — the backend suite builds and passes.
- [x] **Step 5: Commit** — `feat: filter the members list and return branch and generation`

---

## Task 6: Filtering the tree view

The tree keeps its whole-tree load and in-memory assembly (spec §4.2). It gains the **ancestor
rule**: a member who fails the filter but has a matching descendant stays in the response,
flagged as a non-match, because dropping them would detach the subtree.

**Files:**
- Modify: `src/FamilyTree.Contracts/FamilyTrees/FamilyTreeViewResponse.cs`
- Modify: `src/FamilyTree.Application/FamilyTrees/FamilyTreeAssembler.cs`
- Modify: `src/FamilyTree.Application/FamilyTrees/IFamilyTreeService.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyTrees/FamilyTreeService.cs`
- Test: `tests/FamilyTree.Application.Tests/FamilyTrees/FamilyTreeAssemblerTests.cs` (extend)

**Interfaces:**
- Consumes: `MemberDerivation`, `MemberFilterPredicate`, `MemberFilter`.
- Produces: `FamilyTreeNodeResponse.Matches`;
  `FamilyTreeAssembler.Assemble(members, filter, maxDepth)`;
  `IFamilyTreeService.GetViewAsync(MemberFilter, int? maxDepth, CancellationToken)`.
  Used by Tasks 8 and 9.

- [x] **Step 1: Write the failing test**

Extend `FamilyTreeAssemblerTests`:

- With `MemberFilter.None`, the output is identical to today's and every node has
  `Matches = true`. The unfiltered path must not change shape.
- Filtering to a member deep in the tree keeps their whole ancestor chain, each ancestor with
  `Matches = false` and the target with `Matches = true`.
- A subtree containing no match is dropped entirely — the ancestor rule keeps ancestors *of a
  match*, not every member.
- A matching member's non-matching **children** are dropped. The rule is about ancestors; a
  descendant carries no structural obligation.
- The generation filter is root-relative while `Generation` stays absolute: with `rootId` set to
  a generation-2 member, `generation=1` matches that member's children, and those children still
  report `Generation = 3`. Assert both numbers in the same test — they are the two halves of spec
  §1.2, and asserting one alone passes with the other wrong.
- `maxDepth` and a filter together: the depth limit still applies, and `HasMoreChildren` reflects
  children that exist and are kept, not children that exist.

- [x] **Step 2: Add `Matches` to the node**

```csharp
/// <summary>
/// False when this member is present only to hold up a matching descendant (design §4.2). The
/// client renders them dimmed and non-selectable; dropping them server-side would detach the
/// subtree and render the outline as garbage. Always true when no filter is applied.
/// </summary>
bool Matches
```

Placed last in the record so existing positional construction in tests stays readable.

- [x] **Step 3: Change the assembler**

`Assemble` takes the `MemberFilter` in place of the bare `rootId` — the root now arrives as
`filter.RootId`. When `filter.IsEmpty`, take the existing path unchanged and stamp
`Matches = true`: no derivation, no predicate, no behaviour change for the overwhelmingly common
case.

Otherwise derive placements with `MemberDerivation.Derive(members, filter.RootId)`, evaluate
`MemberFilterPredicate.Matches` per member, then build bottom-up, keeping a node when it matches
or when any kept descendant does.

Note in the code that `Generation` in the output stays absolute while the predicate reads the
root-relative placement — with a pointer to spec §1.2, because it reads like an inconsistency and
is not one.

- [x] **Step 4: Change the service and the interface**

`GetViewAsync(Guid? rootId, int? maxDepth, ...)` becomes
`GetViewAsync(MemberFilter filter, int? maxDepth, ...)`. `maxDepth` stays a separate parameter:
it is a transport concern (how much of the tree to ship), not a filter, and spec §5.1 lists it
outside the shared shape.

- [x] **Step 5: Run the tests** — passes.
- [x] **Step 6: Commit** — `feat: filter the tree view, keeping ancestors of a match visible`

---

## Task 7: The branches and generations endpoints

Reference data for Plan 3's dropdowns. Both take only `rootId` (spec §5.1) — they answer "what
can be filtered by", which must not itself be filtered.

**Files:**
- Create: `src/FamilyTree.Contracts/FamilyTrees/BranchResponse.cs`
- Modify: `src/FamilyTree.Application/FamilyTrees/IFamilyTreeService.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyTrees/FamilyTreeService.cs`
- Modify: `src/FamilyTree.Api/Endpoints/FamilyTrees/FamilyTreeEndpoints.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyTreeEndpointsTests.cs` (extend)

**Interfaces:**
- Consumes: `FamilyMemberQuery`.
- Produces: `BranchResponse(Guid Id, string Name)`;
  `IFamilyTreeService.ListBranchesAsync(Guid? rootId, ...)` and `ListGenerationsAsync(Guid? rootId, ...)`;
  `GET /api/v1/family-tree/branches` and `GET /api/v1/family-tree/generations`. Used by Task 9
  and Plan 3.

- [x] **Step 1: Write the failing test**

Both endpoints return 200 for a `FamilyTree.View` holder and 403 without it —
`AuthorizationTests` is the house pattern for the second half. Branches come back name-ordered;
generations come back ascending, starting at 0. A `rootId` naming nothing returns an empty list,
not a 404: "this subtree has no branches" and "no such subtree" are the same answer to a
dropdown, and the uniform-404 argument in design spec §4.4 is about reads of members, not
reference lists.

- [x] **Step 2: Write the contract, the service methods, and the endpoints**

Guarded by `Permissions.FamilyTree.View`, alongside `/view` and `/export.pdf`.

Generations return `IReadOnlyList<int>` — a bare ascending array. No wrapper record: there is one
field, and a `GenerationResponse` holding an `int` called `Generation` is ceremony.

- [x] **Step 3: Run the tests** — passes.
- [x] **Step 4: Commit** — `feat: list the branches and generations available to filter by`

---

## Task 8: Binding the filter on the two existing endpoints

**Files:**
- Modify: `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs`
- Modify: `src/FamilyTree.Api/Endpoints/FamilyTrees/FamilyTreeEndpoints.cs`
- Modify: `frontend/src/i18n/locales/en.json`, `ar.json` (the `FILTER_INVALID_STATUS` entry)
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyMemberEndpointsTests.cs` (extend)

**Interfaces:**
- Consumes: `MemberFilterRequest`, `MemberFilter`.
- Produces: filtered `GET /api/v1/family-members` and `GET /api/v1/family-tree/view`.

- [x] **Step 1: Write the failing test**

- `GET /api/v1/family-members` with no parameters returns every member — the unfiltered contract
  is unchanged.
- `?status=alive` narrows it; `?status=dead` is a **400** whose body carries
  `FILTER_INVALID_STATUS`, following `EXPORT_INVALID_STYLE`'s precedent exactly.
- `?status=` (empty) is *not* an error — it is an absent filter (Task 1, Step 1).
- Four filters at once compose (spec §15).
- The same `?status=dead` on `/view` is the same 400 with the same code. One code, both
  endpoints — a client cannot learn two spellings of the same mistake.
- An unknown `branchId` or `countryId` returns an empty list, not an error. They are filters, and
  a filter matching nothing is a legitimate answer.

- [x] **Step 2: Bind and validate**

Both endpoints take `[AsParameters] MemberFilterRequest filter`, call `MemberFilter.TryCreate`,
and return `ProblemResults.Coded(400, "FILTER_INVALID_STATUS", ...)` on failure. Extract the
shared few lines into one small helper rather than writing them twice — Plan 4's export endpoint
is the third caller.

- [x] **Step 3: Add the locale entries** — `errors.FILTER_INVALID_STATUS` in both files.
- [x] **Step 4: Run the tests** — backend suite and `npm test` (for `locales.test.ts`) pass.
- [x] **Step 5: Commit** — `feat: accept the filter set on the members list and the tree view`

---

## Task 9: Frontend types and data access — no UI

Everything Plan 3 needs to build against, and nothing that renders. The members table gains no
column and the tree gains no dimming here.

**Files:**
- Modify: `frontend/src/features/members/types.ts`
- Modify: `frontend/src/features/members/membersApi.ts`
- Modify: `frontend/src/features/members/useMembers.ts`
- Create: `frontend/src/features/filters/filterParams.ts`
- Test: `frontend/src/features/filters/filterParams.test.ts`
- Test: `frontend/src/features/members/membersApi.test.ts` (extend)

**Interfaces:**
- Produces: `MemberFilters` type, `FamilyMemberListItem`, `Branch`;
  `toFilterParams(MemberFilters) -> URLSearchParams` and `fromSearchParams(URLSearchParams) -> MemberFilters`;
  `membersApi.list(filters)`, `membersApi.tree(params)`, `membersApi.branches(rootId)`,
  `membersApi.generations(rootId)`; `useBranchesQuery`, `useGenerationsQuery`. Used by Plans 3
  and 4.

- [x] **Step 1: Write the failing test**

`filterParams.test.ts` is the round-trip test spec §6.1 calls for — this module is the seam where
client and server could disagree about what `status=alive` means:

- Empty filters serialise to an empty query string. Not `?status=all`: an explicit default is a
  parameter the server has to special-case, and it makes an unfiltered URL look filtered.
- Every field round-trips: `toFilterParams` then `fromSearchParams` is the identity.
- `fromSearchParams` ignores unknown keys, so a URL carrying an unrelated parameter (a tab, a
  selected member) survives a filter change.
- A malformed `generation=abc` reads back as undefined rather than `NaN`. `NaN` reaches the
  server as the string `NaN` and comes back a 400 the user cannot act on.

- [x] **Step 2: Write `filterParams.ts`** — pure, no React, no fetch.
- [x] **Step 3: Extend the types**

`FamilyMemberListItem extends FamilyMember` with `branchId: string | null`,
`branchName: string | null`, `generation: number`. `FamilyTreeNode` gains `matches: boolean`.
`Branch { id: string; name: string }`.

- [x] **Step 4: Extend the API and the hooks**

`membersApi.list` takes optional filters and appends `toFilterParams`. `treePath` merges the
filter params with `maxDepth`. Add `branches` and `generations`. Add `useBranchesQuery` and
`useGenerationsQuery` under `memberKeys.branches` / `memberKeys.generations`, nested under
`'members'` so the existing blanket invalidation refreshes them when a move changes the tree's
shape.

- [x] **Step 5: Run the checks** — `npm test && npm run lint && npm run build` in `frontend/`.
- [x] **Step 6: Commit** — `feat: carry the member filters through the client API`

---

## Verification

Run before declaring the plan done:

- `dotnet build` — clean, no warnings (warnings are errors).
- `dotnet test` — all five projects. The integration suite needs Docker and takes ~3-4 minutes.
- `cd frontend && npm test && npm run lint && npm run build`.
- Manually, against the running stack — **rebuild the containers first**, or the api serves a
  stale image and a new endpoint 404s:
  - `GET /api/v1/family-members` returns all 349 members, each with a branch name and a
    generation, the root reading branch `null` / generation 0.
  - `GET /api/v1/family-members?status=deceased&generation=2` returns a strict subset.
  - `GET /api/v1/family-members?status=dead` returns 400 `FILTER_INVALID_STATUS`.
  - `GET /api/v1/family-tree/branches` returns داوود's children.
  - `GET /api/v1/family-tree/generations` returns `[0,1,…]`.
  - `GET /api/v1/family-tree/view?search=<a deep name>` returns that member with `matches: true`
    and their ancestors with `matches: false`.

## What this plan does not do

- No UI. No filter bar, no sheet, no active-count badge, no Country or Branch column, no dimming
  in the tree — all Plan 3.
- No Excel export, no ClosedXML, no server-side full-name composition — all Plan 4.
- No change to the reports page or the PDF export's generation numbering (spec §1.2 leaves both
  tree-wide and absolute).
- No pagination. `FamilyMemberQuery` takes limit and offset so adding it later is one file, but
  the list endpoint stays unpaginated (spec §5.3).
