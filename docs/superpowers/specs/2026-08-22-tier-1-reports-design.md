# Tier 1 Reports — Design

**Date:** 2026-08-22
**Status:** Approved, ready for implementation planning
**Depends on:** Phase 3 (visualization), Phase 4 (authorization)

## 1. Purpose

Five read-only reports over the existing family member data, exposed as one aggregate
endpoint and one SPA screen. "Tier 1" means every statistic is derivable from the model as
it stands today: no new columns, no new entities, no migration.

The reports answer questions the tree screen cannot:

| Report | Question it answers |
|---|---|
| Structure | How wide, how deep, how balanced is this family? |
| Life status | Who is living, who has died, how long did they live? |
| Completeness | Which records still need curating? |
| Upcoming dates | Whose birthday or death anniversary falls in the next 30 days? |
| Recent activity | What changed in the tree lately? |

Completeness is the load-bearing one. `FamilyMember` documents null dates as "the norm for
imported records", so a curator needs a worklist, not a congratulatory dashboard.

## 2. Scope

**In scope.** Contracts, Application calculators, an Infrastructure service, one API endpoint,
unit and integration tests, and a `/reports` screen in the SPA with Arabic/English and RTL
support matching the rest of the app.

**Out of scope.** PDF or CSV export of reports. Scheduling or emailing. Any statistic
requiring gender, spouses, or events — those need the Relationships/Events model that
"Family Tree SaaS Platform.md" §38 defers past V1. An audit-history report, which is blocked
on the missing `AuditLog` entity (see §9).

**Deliberately not scheduled.** SRS §51 names scheduled reports as a trigger for introducing
a background job system, and the Render deployment is free-tier, single-instance, and spins
down after 15 minutes idle. Reports are computed on request.

## 3. Architecture

The arithmetic is the part most likely to be wrong, so it follows `FamilyTreeAssembler`:
pure, static, synchronous, free of EF, and unit-tested in milliseconds. Infrastructure only
loads data and delegates.

```
src/FamilyTree.Contracts/Reports/
    ReportsResponse.cs, StructureReport.cs, LifeStatusReport.cs,
    CompletenessReport.cs, UpcomingReport.cs, ActivityReport.cs, MemberRef.cs

src/FamilyTree.Application/Reports/
    IReportService.cs
    ReportLimits.cs          fixed windows and caps
    GenerationIndex.cs       memberId -> generation, shared by structure and life status
    StructureCalculator.cs
    LifeStatusCalculator.cs
    CompletenessCalculator.cs
    UpcomingCalculator.cs
    ActivityCalculator.cs

src/FamilyTree.Infrastructure/Reports/ReportService.cs
src/FamilyTree.Api/Endpoints/Reports/ReportEndpoints.cs

tests/FamilyTree.Application.Tests/Reports/     one class per calculator
tests/FamilyTree.Api.IntegrationTests/          endpoint, authorization, tenant isolation

frontend/src/features/reports/
    ReportsPage.tsx, reportsApi.ts, useReports.ts, types.ts
    StructureSection.tsx, LifeStatusSection.tsx, CompletenessSection.tsx,
    UpcomingSection.tsx, ActivitySection.tsx

modified: frontend/src/routes/AppRoutes.tsx      /reports route
          frontend/src/app/AppShell.tsx          nav entry
          frontend/src/features/tree/TreePage.tsx  ?memberId= preselection (§8)
          frontend/src/i18n/*                    ar + en report labels
```

Five small calculators rather than one class, so no file carries five unrelated rule sets.

`ReportService` loads the tenant's tree and its full member list exactly once and passes that
one list to every calculator. This matches `FamilyTreeService.GetViewAsync`, which already
loads the whole tree in memory for V1 and documents that a windowed query would change only
that method, never the contract. The same holds here.

## 4. API

```
GET /api/v1/reports
```

No query parameters. Guarded by `Permissions.FamilyTree.View`.

**Why no new permission.** Reports are aggregates over data `FamilyTree.View` and
`Member.View` already expose; no new fact is revealed, only a summary of existing ones. This
follows the precedent set by `GET /api/v1/family-tree/export.pdf`, which reuses
`FamilyTree.View` on the reasoning that a separate code adds a lockout surface for the
last-administrator guard to reason about without adding protection. No permission constant,
no seed row, no migration.

## 5. Contracts

```csharp
ReportsResponse(
    DateOnly GeneratedOn,
    StructureReport Structure,
    LifeStatusReport LifeStatus,
    CompletenessReport Completeness,
    UpcomingReport Upcoming,
    ActivityReport Activity);

MemberRef(Guid Id, string Name, Guid? ParentId);
```

`GeneratedOn` is the UTC reference day every date rule was evaluated against. It is returned
so a client never re-derives "today" in its own time zone and disagrees with the server.

`MemberRef` carries `ParentId` rather than a composed full name. See §7.

### Structure

```csharp
StructureReport(
    int TotalMembers,
    int Depth,
    IReadOnlyList<GenerationCount> Generations,
    IReadOnlyList<BranchSummary> Branches,
    int MembersWithChildren,
    int LeafMembers,
    decimal AverageChildrenPerParent);

GenerationCount(int Generation, int Count);
BranchSummary(Guid Id, string Name, int DescendantCount, int Depth);
```

Counts only, no member lists: the tree screen already browses these.
`AverageChildrenPerParent` divides by `MembersWithChildren`, not by `TotalMembers` — the
question is how many children a parent has, and including the childless makes the number
describe nothing.

`Branches` is one entry per member with `ParentId is null`, matching exactly what the tree
screen roots. Because a member's parent link is guaranteed to resolve (§6), generation 1 is
precisely the set of branch roots, so `Generations[0].Count == Branches.Count` and
`Structure.TotalMembers` always equals what the tree screen displays. Both are worth asserting
in tests: they are the invariants that catch a broken generation walk.

### Life status

```csharp
LifeStatusReport(
    int Living,
    int Deceased,
    IReadOnlyList<GenerationLifeStatus> ByGeneration,
    IReadOnlyList<AgeBracketCount> LivingAges,
    int LivingWithoutBirthDate,
    LongevityStats? Longevity);

GenerationLifeStatus(int Generation, int Living, int Deceased);
AgeBracketCount(string Bracket, int Count);
LongevityStats(int Count, int MinYears, int MaxYears, int MedianYears);
```

Age brackets: `0-17`, `18-29`, `30-44`, `45-59`, `60-74`, `75+`. Only living members holding a
birth date are bracketed; the rest are reported as `LivingWithoutBirthDate` so the histogram
never implies a population it did not measure.

`Longevity` covers deceased members having **both** dates and is null when none do — the
realistic state of a freshly imported tree, and a null says "not measurable" where zeros
would read as "measured, and zero".

### Completeness

```csharp
CompletenessReport(
    int TotalMembers,
    int CompleteRecords,
    IReadOnlyList<CompletenessIssue> Issues);

CompletenessIssue(string Code, int Count, IReadOnlyList<MemberRef> Members);
```

Issue codes, stable and translated client-side like every other code in this API:

| Code | Meaning |
|---|---|
| `MISSING_BIRTH_DATE` | `DateOfBirth is null` |
| `DECEASED_WITHOUT_DEATH_DATE` | `IsDeceased && DateOfDeath is null` |

A member can appear under more than one code; the codes are independent worklists, not a
partition. `CompleteRecords` counts members flagged by no code at all.

### Upcoming dates

```csharp
UpcomingReport(
    int WindowDays,
    int BirthdayCount,
    int AnniversaryCount,
    IReadOnlyList<UpcomingBirthday> Birthdays,
    IReadOnlyList<UpcomingAnniversary> Anniversaries);

UpcomingBirthday(MemberRef Member, DateOnly DateOfBirth, DateOnly Occurrence,
                 int DaysAway, int TurningAge);
UpcomingAnniversary(MemberRef Member, DateOnly DateOfDeath, DateOnly Occurrence,
                    int DaysAway, int Years);
```

Both ordered by `DaysAway` ascending, then by name for a stable tie-break. `Occurrence` is the
date the observance falls on this cycle, which is not always the anniversary date (see §6).

`TurningAge` is the age the member reaches **on `Occurrence`**, not their age today, and
`Years` likewise counts years elapsed at `Occurrence`. A list headed "upcoming" that showed
today's age would be off by one for every entry in it.

### Recent activity

```csharp
ActivityReport(
    int WindowDays,
    int AddedCount,
    int EditedCount,
    IReadOnlyList<ActivityEntry> Added,
    IReadOnlyList<ActivityEntry> Edited);

ActivityEntry(MemberRef Member, DateTimeOffset At);
```

Both lists ordered by `At` descending. This is a stand-in for audit history, not a substitute:
it shows the current state of a row's timestamps, so it cannot show deletions, cannot show who
made a change, and shows only the most recent edit of several. §9 covers the real fix.

### Limits

`ReportLimits` holds, as documented constants:

```csharp
UpcomingWindowDays = 30;
ActivityWindowDays = 30;
MaxMembersPerList  = 50;
```

`MaxMembersPerList` caps every member-bearing list: each completeness issue's `Members`, both
upcoming lists, and both activity lists. Completeness and activity truncate after their
documented ordering; upcoming truncates by nearest date, the only cut that keeps that list
useful.

Every capped list carries its untruncated count alongside, with no exceptions — `Count` on a
completeness issue, `AddedCount`/`EditedCount` on activity, `BirthdayCount`/`AnniversaryCount`
on upcoming — mirroring how search returns `total` independent of page size. A client must
never report `Members.Count` as the number of affected members.

A truncation that no field discloses is a lie the contract tells quietly, and a large tree can
reach 50 in a 30-day window: at 500 members with birth dates, roughly 41 birthdays fall in any
given month, so the cap is close enough to be crossed rather than hypothetical.

## 6. Computation rules

The decisions worth stating, because each has a plausible wrong answer.

**Reference day.** `TimeProvider.GetUtcNow()` reduced by
`DateOnly.FromDateTime(now.UtcDateTime)`, identical to `FamilyMember.ValidateLifeDetails`.
Members are recorded from many time zones and a calendar date has no zone of its own, so one
server-side reference day is the only stable bound. Injecting `TimeProvider` also makes every
date rule testable without waiting for a calendar.

**Generation.** The length of the resolvable parent chain, walked upward and bounded by the
member count exactly as `FamilyTreeAssembler.GenerationOf` bounds it, so a malformed chain
cannot loop. A first-generation member is generation 1, per BR-003: the root family is the
`family_trees` row, not a member.

**Unresolvable parent links cannot occur, and are still handled.** The composite self
foreign key `(parent_id, family_tree_id) → (id, family_tree_id)`, added as raw DDL in
`AddFamilyMembers`, makes a `ParentId` naming a non-existent member physically
unrepresentable. There is therefore no orphan condition to report, and no completeness code
for one — an issue that can never fire is also an issue that can never be tested.

The calculators still tolerate an unresolved link rather than throwing, treating such a member
as generation 1. This is robustness, not a finding: the calculators are pure functions over
whatever list they are handed, and if reports ever move to a windowed query, a parent outside
the window is an artifact of the query, not corruption in the data. Nothing in the response
draws attention to it.

**Added versus edited.** `Entity.InitializeTimestamps` sets `CreatedAt == UpdatedAt`, so a
newly created member matches any naive "updated recently" filter. The two lists are therefore
defined to be disjoint:

- `Added` — `CreatedAt` within the window.
- `Edited` — `UpdatedAt` within the window **and** `CreatedAt` outside it.

Testing `UpdatedAt != CreatedAt` instead is not enough: a member created on Monday and
corrected on Tuesday satisfies both clauses and would be listed twice in the same week's
report. Anchoring `Edited` on `CreatedAt` outside the window makes it mean "a change to a
member that already existed", which is the question the list actually answers. A member both
added and edited inside the window appears once, under `Added` — its arrival is the more
informative fact, and its `Added` entry already carries the newer `UpdatedAt` state.

**29 February.** A birthday or anniversary on 29 February is observed on 1 March in a
non-leap year. Chosen over "skip it" so the person never silently disappears from a 30-day
window, and over 28 February so an observance never lands before its own anniversary date.

**Birthdays are living-only.** A birthday list including the dead is a bug, not a feature.

**Anniversaries require `DateOfDeath`, not `IsDeceased`.** The domain deliberately allows
someone known to have died whose date is lost; those members are counted under
`DECEASED_WITHOUT_DEATH_DATE` instead of being given an invented anniversary.

**Ages are whole years**, computed from the reference day, decremented when the anniversary
has not yet occurred this year.

**Longevity median.** With an even count, the lower of the two middle values, not their mean.
These are whole-year counts; producing an 82.5 implies a precision the data does not have.

**Empty tree.** Every list is empty, every count zero, `Longevity` is null, `Depth` is 0.
The endpoint returns 200. A tenant with no tree at all still gets `FAMILY_TREE_NOT_FOUND`
from the shared loader, unchanged.

## 7. Member identity in report rows

A report row reading `داوود` identifies nobody: in this data model identity comes from the
lineage, which is why `frontend/src/features/members/fullName.ts` composes a four-part name by
walking `parentId`.

**Decision.** `MemberRef` carries `(Id, Name, ParentId)` and the SPA composes display names
with the existing, tested `fullName` helper against the member list the page already fetches.

**Rejected alternative.** Composing full names server-side would make the endpoint
self-sufficient, but it reimplements the naming rule — including `NAME_PART_COUNT`, the
stop-on-missing-parent behaviour, and the cycle bound — in a second language, where the two
copies will drift. If a future non-SPA consumer needs composed names, the right move is to
extract the rule into a shared Application helper used by both, not to duplicate it now.

## 8. Frontend

Route `/reports`, registered alongside the existing routes and gated on the same permission as
the tree screen. One page, five sections, following the established `MembersPage` and
`TreePage` patterns:

- TanStack Query through `useReports`, one request for the whole payload.
- Sections render their own loading and empty states, so a sparse tree shows "nothing to
  report here" per section rather than one blank page.
- Every label is an i18n key in both Arabic and English; no string is hardcoded.
- RTL-safe layout using logical properties, consistent with the rest of the app.
- Numbers and dates formatted through the existing locale setup, not `toString()`.

**Linking a report row to the tree.** A completeness worklist is only actionable if a row can
take you to the member, but `TreePage` currently holds `selectedId` in component state with no
URL representation, so no link can address a member today. This design adds one: `TreePage`
reads an optional `?memberId=` search parameter and preselects that member on mount, leaving
all existing behaviour unchanged when the parameter is absent. Report rows then link to
`/?memberId=<id>`. This is a deliberate, minimal extension of an existing screen, in scope
because without it the completeness report is a list of problems with no way to act on them.

## 9. Known gap: audit history

`Permissions.Audit.View` exists in the permission catalog and the SRS describes audit writes
inside the move and delete transactions, but no `AuditLog` entity exists anywhere in `src/`.
The permission is currently a promise with nothing behind it.

The recent-activity report is an explicit stand-in and is documented as such in §5. A real
audit-history report — who changed what, including deletions — is a follow-up blocked on that
entity being implemented. This design does not attempt to work around the gap.

## 10. Testing

TDD per the project workflow: test first, watch it fail, implement, refactor.

**Calculator unit tests** carry the edge cases named in §6 explicitly:

- empty tree; single member; single generation
- the structure invariants of §5: `Generations[0].Count == Branches.Count`, and
  `TotalMembers` equal to the member count the tree view would render
- a member whose `ParentId` names a missing row, asserted to be counted and *not* reported
  as an issue
- a cyclic `parentId` chain terminating within the member-count bound
- every date null; birth date only; death date without `IsDeceased`; `IsDeceased` without a date
- a birthday on 29 February evaluated in a leap year and in a non-leap year
- a birthday exactly on the reference day, and exactly at the window edge
- a window crossing a year boundary: a reference day in mid-December with occurrences in
  January, asserting the next occurrence is dated in the following year and `DaysAway` and
  `TurningAge` are computed against it — the case where "this year's occurrence" arithmetic
  most often goes wrong
- a member created and edited inside the same window, asserted to appear once, in `Added` only
- a member created before the window and edited inside it, asserted to appear in `Edited` only
- a list of exactly 50 and of 51, asserting truncation with the true count preserved
- even and odd longevity populations, asserting the median rule

**Integration tests** cover: the endpoint returns 200 with the expected shape; a caller
lacking `FamilyTree.View` receives 403; a second tenant's members never appear in any count
or list; a tenant with no tree receives `FAMILY_TREE_NOT_FOUND`.

**Frontend tests** cover each section rendering populated, empty, and truncated states; that a
capped list displays the true count rather than the row count; that `/reports` is served by the
route table; and that `TreePage` preselects the member named by `?memberId=`, ignores an id
that matches no member, and behaves exactly as before when the parameter is absent.
