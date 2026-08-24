# Member Data, Filters, and Excel Export — Design

**Date:** 2026-08-24
**Status:** Approved
**Source requirement:** Family Tree System — Functional Specification v1.0 (2026-08-24)

Extends the family member record with contact and identification details, derives branch and
generation from the existing parent chain, adds a combinable filter set to the Family Tree and
Members pages, and exports the filtered result to `.xlsx`.

Section references of the form §N point at the source requirement document, not at this design.

---

## 1. Decisions taken against the source requirement

The source requirement was written without sight of this codebase. Four of its statements
conflict with decisions already made here. Each was resolved deliberately; a plan must not
re-open them.

### 1.1 Names stay a single stored field

The source requirement (§3, §20, §22) models four stored name columns — First, Father,
Grandfather, Family. This system stores **one given name per member** and derives the lineage
from the `parent_id` chain. That is a documented decision (`frontend/src/features/members/fullName.ts`)
and it matches the data: all 349 imported members carry a single Arabic given name.

**Resolution:** keep the single-name model. The export's `Full Name` column is composed by
walking the parent chain — own name, father, grandfather, then the family/tree name. Storing
four components would create a second source of truth for a fact the tree already holds, and
the two would drift on the first re-parent.

### 1.2 Generation is 1-based internally, root-relative at the edge

The source requirement numbers the root person 0. This system numbers a parentless member 1,
because the root is the `family_trees` row rather than a person (BR-003). The data has exactly
one parentless member (داوود), so the two schemes differ by exactly one.

**Resolution:** internal computation, the reports page, and the PDF caption keep their existing
absolute 1-based numbering. The generation **filter** and the export's Generation column are
expressed relative to the selected root, where the root reads 0, matching §21's table.

Two display sites move to root-relative numbering so a page cannot contradict its own filter:
the member detail panel (`MemberPanel.tsx:180`) and the search-hit subtitle
(`TreePage.tsx:163`). Reports and the PDF are tree-wide and have no selected root, so they are
untouched.

### 1.3 The branch root follows the tree page's existing `rootId`

Branches are the direct children of the currently selected root. With no `rootId`, the root is
the single parentless member and the branches are that member's children. If several parentless
members ever exist, each parentless member is itself a branch. This reuses the `rootId`
parameter `GET /api/v1/family-tree/view` already takes rather than introducing a second notion
of "root".

### 1.4 Contact data is guarded by `Member.View`

Per §27, the export follows the permissions of the Members page. No new permission is
introduced: anyone who can see the members list can see and export contact details. This suits
a private family tree whose viewers are family.

---

## 2. Data model

### 2.1 `countries` — new reference table

System-level reference data, **not tenant-owned**. It joins `Tenant` and `Permission` as an
entity with no global query filter.

| Column | Type | Notes |
|---|---|---|
| `id` | `int` identity | Small stable reference table; a Guid buys nothing |
| `code` | `varchar(2)` unique | ISO 3166-1 alpha-2 |
| `name_ar` | `varchar(100)` | |
| `name_en` | `varchar(100)` | |
| `dial_code` | `varchar(8)` | E.164 country code, leading `+` |

Seeded from a static list in `DatabaseSeeder` alongside the permission catalog, idempotent by
`code` so re-seeding a deployed database is a no-op.

The flag emoji is **not stored**. It is derivable from the alpha-2 code by regional-indicator
arithmetic, so the frontend computes it — no image assets and no third column to keep in sync.

### 2.2 New columns on `family_members`

| Column | Type | Notes |
|---|---|---|
| `national_id` | `varchar(9)` null | `^[0-9]{9}$`, stored as text |
| `mobile_number` | `varchar(20)` null | E.164, leading `+` |
| `whatsapp_number` | `varchar(20)` null | E.164, independent of mobile |
| `country_id` | `int` null | FK → `countries`, `OnDelete(Restrict)` |

All four are nullable. The 349 imported members have none of them, and a required column would
make every existing record unsaveable.

### 2.3 Constraints and indexes

- **Filtered unique index** on `(tenant_id, national_id) WHERE national_id IS NOT NULL`.
  Per-tenant, not global: two tenants are unrelated families, and a global unique index would
  leak the existence of a record across the tenant boundary.
- **Check constraint** `ck_member_national_id_digits` mirroring the regex, following the
  belt-and-braces precedent of `ck_member_death_after_birth`: the bulk import writes in volume
  and cannot be relied on to route through the aggregate.
- Indexes on `country_id` and `is_deceased` per §25. `national_id` is covered by the unique
  index.

### 2.4 Domain

`FamilyMember` gains the four properties with private setters, validated by a
`ValidateContactDetails` helper shaped like the existing `ValidateLifeDetails`: validate
everything, then mutate, so a rejected edit leaves the entity — `Version` included — exactly as
it was.

`Update()` grows to carry contact details alongside name and life details. It remains **one**
command: one form submission is one edit and one `Version` bump, per the reasoning already in
that method's doc comment.

New error codes: `MEMBER_NATIONAL_ID_INVALID`, `MEMBER_NATIONAL_ID_DUPLICATE`,
`MEMBER_PHONE_INVALID`, `MEMBER_COUNTRY_NOT_FOUND`.

### 2.5 What is deliberately not added

- **No `Status` column.** `IsDeceased` already is the alive/deceased flag §13 asks for. A second
  representation of the same fact is how two representations drift apart.
- **No stored branch or generation.** Both stay derived, per §9 and §10, preserving the existing
  property that a moved subtree renumbers itself on the next read with no backfill.

---

## 3. Deriving branch and generation

One recursive walk downward from the root produces both values in a single pass:

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
```

`COALESCE(t.branch_id, c.id)` is the entire branch rule. A direct child of the root has a null
parent branch, so it becomes its own branch; every descendant inherits it unchanged. That is
§9's "determined by the first direct child encountered", at any depth, with no depth limit. The
root keeps `branch_id IS NULL`, which renders as **Root** per §21. Generation falls out of the
same walk, 0 at the root.

### 3.1 Tenant isolation

The `tenant_id` predicate appears in **both** the anchor and the recursive term. This is
required, not defensive: raw SQL bypasses the EF global query filter, and without the predicate
in the recursive term a walk starting on a permitted row could descend into another tenant's
members. This follows the argument documented in `FamilyMemberSearchQuery`, which is the house
pattern for tenant-safe recursive SQL.

---

## 4. Filtering

### 4.1 One query, two callers

`FamilyMemberQuery` (Infrastructure, beside `FamilyMemberSearchQuery`) wraps the CTE above,
joins back to `family_members` and `countries`, and applies the filter set — name search,
`is_deceased`, `branch_id`, `generation`, `country_id` — as `WHERE` predicates. Every value is
parameterised; `NULL` means "no filter". §15's combinability is a plain `AND` across the
supplied predicates.

Two callers: the members list endpoint and the export endpoint. **The export is never handed a
client-supplied id list** — it re-runs the same filters server-side, which is what makes §18's
"export respects filters" and §27's "export respects permissions" one guarantee rather than two.

### 4.2 The Family Tree page

The tree keeps its existing whole-tree load and in-memory assembly, applying the same filter
semantics during assembly where branch and generation are already known for free. A second
query path would have to return matches *plus* their ancestor chains, which is a materially
harder query for a 349-member tree.

**Ancestor rule:** on the tree page a member who fails the filter but has a matching descendant
**stays visible**, dimmed and non-selectable. Dropping them would detach the subtree and render
the outline as garbage. The Members list and the export have no such rule — they show only
matches.

---

## 5. API

### 5.1 Shared filter shape

A `MemberFilterRequest` record in Contracts — `Search`, `Status` (`all` | `alive` | `deceased`),
`BranchId`, `Generation`, `CountryId`, `RootId` — bound from the query string via
`[AsParameters]`, and used by every endpoint below. Sharing the record is what keeps
combinability honest: a filter added later cannot reach the list but miss the export.

```
GET /api/v1/family-members             ?search=&status=&branchId=&generation=&countryId=&rootId=
GET /api/v1/family-members/export.xlsx ?…same…
GET /api/v1/family-tree/view           ?…same… &maxDepth=
GET /api/v1/family-tree/branches       ?rootId=
GET /api/v1/family-tree/generations    ?rootId=
GET /api/v1/countries
```

An absent parameter means "no filter". An unrecognised `status` value is a 400
`FILTER_INVALID_STATUS`, following the precedent `export.pdf` set with `EXPORT_INVALID_STYLE`:
silently defaulting an invalid value returns a result the caller did not ask for with nothing
to say so.

### 5.2 Guards

| Endpoint | Permission |
|---|---|
| `family-members`, `family-members/export.xlsx` | `Member.View` |
| `family-tree/view`, `branches`, `generations` | `FamilyTree.View` |
| `countries` | Authenticated only |

`countries` carries no permission: it is a public reference list, and gating it would break the
member form's dropdown for a user who can edit but not view members.

### 5.3 Pagination

The members list stays unpaginated. 349 filtered members is a small payload and the page
already renders every row. `FamilyMemberQuery` takes limit/offset internally, so adding
pagination later changes one file rather than the contract.

### 5.4 Validation

| Rule | Enforced in | Code → status |
|---|---|---|
| National ID `^[0-9]{9}$` | Domain + DB check constraint | `MEMBER_NATIONAL_ID_INVALID` → 400 |
| National ID unique per tenant | DB filtered unique index | `MEMBER_NATIONAL_ID_DUPLICATE` → **409** |
| Phone is E.164 and agrees with the country dial code | Domain | `MEMBER_PHONE_INVALID` → 400 |
| `countryId` exists | Service, before the aggregate call | `MEMBER_COUNTRY_NOT_FOUND` → 400 |

Duplicate national ID raises `ConflictException` (409), not a plain `DomainException` (400): it
depends on current state rather than on the request being malformed, which is the distinction
`ConflictException` documents. It is caught from the unique-index violation rather than checked
with a prior `SELECT`, because check-then-insert races and only the index holds the invariant.

### 5.5 Phone normalisation

The client sends dial code and local number separately, per §5.2's picker. The server
concatenates, strips separators, and stores one E.164 string — §5.1's "shall not store the
dialing code separately". Validation is format-level only: leading `+`, 8–15 digits, dial-code
prefix agreement. §28 puts carrier verification out of scope, so no `libphonenumber` dependency.

---

## 6. Frontend

### 6.1 Shared filter module — `features/filters/`

Both pages need the same five controls, the same URL round-trip, and the same reset. Building
it twice guarantees drift.

- **`useMemberFilters()`** — filter state lives in the **URL query string** via
  `useSearchParams`, not component state. A filtered view is then linkable and survives a
  refresh, and the export button builds its download URL by passing the same params straight
  through instead of re-deriving them. §15's Reset Filters is one `setSearchParams({})`.
- **`FilterBar`** — the five controls plus Reset, backed by `useBranches()`,
  `useGenerations()`, `useCountries()` so both pages share cached reference data.
- **`filterParams.ts`** — pure serialisation to the query string the API expects. Unit-tested:
  it is the seam where client and server could disagree about what `status=alive` means.

### 6.2 Responsive behaviour

Five dropdowns fit a desktop row and cannot fit 320px. Below `COMPACT_MAX_WIDTH` the bar
collapses to a single **Filters** button opening a sheet, carrying an **active-count badge** so
a user is never filtered without knowing why the list looks short — the failure mode of a hidden
filter panel. Above the breakpoint, an inline bar. This reuses the existing `useIsCompact`
breakpoint rather than introducing a second one.

### 6.3 Member form

`MemberForm` gains a contact section: national ID with inline 9-digit validation, a
country-of-residence select, and two phone inputs each rendered as `[flag +dial ▼][local
number]` per §5.2. Dial codes come from the `countries` response; the flag emoji is computed
from the alpha-2 code. A "same as mobile" checkbox on WhatsApp, since §6 allows them to differ
but they usually will not.

The form still submits one `PUT` carrying contact and life details together, because the server
bumps `Version` once per edit.

### 6.4 Members table

Gains Country and Branch columns. Full Name stays derived client-side by the existing
`fullName()` helper — the same composition the export performs server-side.

### 6.5 Internationalisation

Every new label lands in both `en.json` and `ar.json`; `locales.test.ts` already enforces key
parity. Country names arrive from the API already localised (`nameAr` / `nameEn` selected by
active language) rather than from a frontend table.

---

## 7. Excel export

### 7.1 Structure

`IMemberExcelExporter` in Application holds the interface and the row-building logic, pure and
testable without producing a workbook. `ClosedXmlMemberExporter` in Infrastructure is the only
file that touches the library. This is the same split as `IFamilyTreeExporter` →
`SkiaTreeRenderer`.

`ClosedXML` is a new package reference in Infrastructure — MIT, no native assets, so unlike
SkiaSharp it needs no Linux native packages and the Docker image is unaffected.

### 7.2 Endpoint

`GET /api/v1/family-members/export.xlsx`, guarded by `Member.View`, taking the same
`MemberFilterRequest` as the list and calling the same `FamilyMemberQuery`. Returns via
`Results.File` with `fileDownloadName: $"{familyTreeName}.xlsx"`, relying on the RFC 5987
encoding the PDF endpoint already depends on to carry an Arabic family name through the header.

### 7.3 Columns

In §19's order: National ID, Full Name, Mobile Number, WhatsApp Number, Country of Residence,
Branch, Generation, Status.

- **Full Name** — composed server-side by walking the parent chain: own name, father,
  grandfather, then the family/tree name. The server-side twin of `nameParts`. §20's
  no-double-spaces rule falls out of joining a filtered list rather than concatenating with
  separators.
- **Branch** — the branch root's name, or **Root** for the root member, per §21.
- **Generation** — the root-relative number from the CTE; the root reads 0.
- **Status** — Alive / Deceased from `IsDeceased`, localised.
- **National ID and both phone numbers are written as text cells, not numbers.** Otherwise Excel
  reads `123456789` as a number and `+970599123456` as a broken formula, and a leading-zero
  identifier loses its zero. This carries §3's "store as strings rather than numeric types"
  through to the output.

### 7.4 Localisation and size

Headers and Status follow the request's `Accept-Language` through the existing
`CaptionLanguageResolver` that the PDF export uses. For Arabic the worksheet is set
right-to-left so column order reads correctly.

No streaming and no size cap: 349 members is a small in-memory workbook. Should a tenant
outgrow that, `TooLargeException` is the established way to say so.

---

## 8. Testing

| Project | Covers |
|---|---|
| **Domain.Tests** | National ID and phone validation at their boundaries (8/9/10 digits, letters, empty); a rejected contact edit leaves `Version` untouched; one `Update()` carrying life *and* contact details bumps `Version` exactly once |
| **Application.Tests** | Branch and generation derivation against §21's worked example, asserted as a table; full-name composition with a missing middle component (§20); the filter predicates in combination (§15's four-way AND) |
| **Api.IntegrationTests** | The recursive CTE against real PostgreSQL via Testcontainers; **a cross-tenant branch-walk test asserting a walk started in one tenant cannot descend into another**; the filtered unique index producing 409 on a duplicate national ID; export-respects-filters asserted by row count |
| **Frontend (vitest)** | `filterParams` serialisation round-trip; the compact filter sheet's active-count badge; the phone input composing dial code + local number into one E.164 value |

§21's worked example is reproduced as a literal test table rather than paraphrased: it is the
clearest statement of the branch-vs-generation distinction §30 calls fundamental.

---

## 9. Decomposition

One coherent feature, too large for a single implementation plan. Four plans, in dependency
order, each independently shippable and reviewable:

1. **Schema and member data** — `countries` table and seed, the four member columns, migration,
   domain validation, contracts, form UI. Ships alone: contact details can be recorded before
   any filter exists.
2. **Derivation and the shared query** — the recursive CTE, `FamilyMemberQuery`, the branches
   and generations endpoints, filter parameters on the list and tree endpoints. No UI. **This
   plan carries the tenant-isolation risk and warrants the most scrutiny.**
3. **Filter UI** — the shared filter module, both pages, the responsive sheet, and the two
   root-relative generation labels from §1.2.
4. **Excel export** — ClosedXML, the exporter, the endpoint, the download button.

Work proceeds on the `member-data-filters-export` branch, cut from `main`.

---

## 10. Out of scope

Restating §28 so no plan quietly absorbs it:

- Verifying that a phone number belongs to the person, or that it has an active WhatsApp
  account.
- WhatsApp Business API integration.
- Verifying a Palestinian National ID against a government service.
- Automatic synchronisation of country data with an external service.
- Genealogy features beyond the filtering and member information described here.
- Importing family members from Excel.
