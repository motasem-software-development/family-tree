# Excel Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Export the filtered members list to `.xlsx`, with the eight columns specification §19
asks for, in the interface's language, respecting every filter and permission the list respects.

**Architecture:** The same Application/Infrastructure split the PDF export already uses.
`IMemberExcelExporter` in Application holds the interface and the row-building logic — pure and
testable without producing a workbook — and `ClosedXmlMemberExporter` in Infrastructure is the
only file that touches the library, exactly as `IFamilyTreeExporter` → `SkiaTreeRenderer` does.
The endpoint re-runs `FamilyMemberQuery` with the same `MemberFilterRequest` the list binds, so
"export respects filters" and "export respects permissions" are one guarantee rather than two.

**Tech Stack:** .NET 10, ClosedXML (new), xunit + FluentAssertions, React 19, react-i18next,
vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-08-24-member-data-filters-export-design.md`

This is **Plan 4 of 4** from the spec's §9 decomposition, and the last. Plans 1–3 are complete on
this branch — read their STATUS blocks and `git log --oneline main..HEAD` before starting, and do
not re-run them.

Section references of the form §N point at the design spec above, or — where the text says
"specification §N" — at the source requirement document it implements.

---

## Global Constraints

- Target framework `net10.0`; `Nullable` enable; `TreatWarningsAsErrors` true (Directory.Build.props) — a warning fails the build.
- Branch: `member-data-filters-export`, already cut from `main`. Do not create another branch.
- **No migration and no schema change.** Everything the export reads already exists.
- Test frameworks are fixed: xunit 2.9.3 + FluentAssertions 7.2.0 (backend), vitest 4 + Testing Library (frontend). Do not add test packages.
- **ClosedXML is the only new package**, and it belongs in Infrastructure alone. It is MIT and
  has no native assets, so unlike SkiaSharp it needs no Linux native packages and the Docker
  image is unaffected (spec §7.1). Do not reference it from Application, Contracts, or Api.
- Every new user-facing string must be added to **both** `frontend/src/i18n/locales/en.json` and `ar.json`. `locales.test.ts` enforces key parity and will fail the suite otherwise.
- Arabic test fixtures use real Arabic names (`سليمان`, `داوود`), matching existing tests.
- The endpoint is guarded by `Member.View` — the Members page's own permission. **No new
  permission** (spec §1.4).

### The columns, in specification §19's order

| # | Column | Source |
|---|---|---|
| 1 | National ID | `NationalId`, **text cell** |
| 2 | Full Name | composed server-side by walking the parent chain |
| 3 | Mobile Number | `MobileNumber`, **text cell** |
| 4 | WhatsApp Number | `WhatsAppNumber`, **text cell** |
| 5 | Country of Residence | the country's localised name |
| 6 | Branch | `BranchName`, or **Root** for the root member (§21) |
| 7 | Generation | root-relative; the root reads 0 |
| 8 | Status | Alive / Deceased from `IsDeceased`, localised |

**The three identifier columns are written as text, not numbers** (spec §7.3). Otherwise Excel
reads `123456789` as a number, `+970599123456` as a broken formula, and a leading-zero identifier
loses its zero. This is what carries specification §3's "store as strings rather than numeric
types" through to the output.

### Refinement of spec §7.3 — Full Name has no server-side twin yet

Spec §7.3 says Full Name is "the server-side twin of `nameParts`". No such server-side code
exists: `nameParts` lives in `frontend/src/features/members/fullName.ts` and has never had a
counterpart.

**Resolution:** Task 2 writes one, `MemberNameComposer`, in Application. It must match
`fullName.ts` exactly, and the two rules that are easy to get wrong are stated there and
asserted here:

- **Four parts maximum** — own name, father, grandfather, great-grandfather (`NAME_PART_COUNT`).
  Four is the customary length of an Arabic name, not a limit of the data; the walk stops there
  even when the tree goes deeper.
- **The walk stops on a missing parent** rather than throwing, and is bounded so a cyclic
  `parent_id` cannot loop.

Spec §7.3 adds "then the family/tree name". `fullName.ts` does **not** append it, and the members
list on screen does not show it. **Resolution: follow the frontend.** Appending the tree name
server-side would make the exported name differ from the name the same user just read on the
page, for every one of the 351 members. Specification §20's no-double-spaces rule still falls out
of joining a filtered list rather than concatenating with separators.

### Refinement of spec §7.2 — the download filename

Spec §7.2 says `fileDownloadName: $"{familyTreeName}.xlsx"`, relying on the RFC 5987 encoding the
PDF endpoint already depends on to carry an Arabic family name through the header.

That much is unchanged. Note only that the **frontend** names the file too, in
`downloadTreePdf`'s `fileName` argument, and the browser's `download` attribute wins over the
header for a blob URL. Both must be set, and Task 6 sets the client one from the same family name.

---

## Task 1: The ClosedXML package reference

Its own task and its own commit, so a reviewer sees the dependency arrive on its own.

**Files:**
- Modify: `src/FamilyTree.Infrastructure/FamilyTree.Infrastructure.csproj`

- [x] **Step 1: Add the reference** — `ClosedXML` version `0.105.1`, in Infrastructure only.
- [x] **Step 2: Verify** — `dotnet build` is clean, and `dotnet list package --include-transitive`
  shows no native-asset package arriving with it. If one does, stop: spec §7.1's "the Docker
  image is unaffected" claim is the reason this package was chosen over the alternatives.
- [x] **Step 3: Commit** — `build: add ClosedXML for the members export`

---

## Task 2: `MemberNameComposer` — Full Name, server-side

**Files:**
- Create: `src/FamilyTree.Application/FamilyMembers/MemberNameComposer.cs`
- Test: `tests/FamilyTree.Application.Tests/FamilyMembers/MemberNameComposerTests.cs`

**Interfaces:**
- Consumes: nothing but ids and names.
- Produces: `MemberNameComposer.Compose(Guid id, IReadOnlyDictionary<Guid, (string Name, Guid? ParentId)> byId) -> string`
  and `MemberNameComposer.MaxParts` (4). Used by Task 3.

- [x] **Step 1: Write the failing test**

Mirror `fullName.test.ts` case for case, and add the ones the export makes reachable:

- A root member composes to their own name alone — padding it would invent ancestors.
- Three generations compose to three parts, in order own-name-first.
- **Five generations compose to four parts**, not five. This is the rule the frontend states and
  the one an unbounded walk would silently break.
- A missing parent ends the walk rather than throwing: a filtered or partial map still yields a
  name worth showing.
- A cyclic `parent_id` terminates. Impossible through the move command, which validates with a
  recursive CTE, but the export must answer rather than hang on a corrupt import.
- Parts are joined with exactly one space, and a name with surrounding whitespace does not
  produce a double space (specification §20).

- [x] **Step 2: Implement**

A bounded upward walk, structurally identical to `nameParts`. Keep the doc comment pointing at
`fullName.ts` in both directions — two implementations of one rule need to say so.

- [x] **Step 3: Run the tests** — `dotnet test tests/FamilyTree.Application.Tests` passes.
- [x] **Step 4: Commit** — `feat: compose a member's full name server-side`

---

## Task 3: `IMemberExcelExporter` and the row builder

The pure half: everything except producing a workbook. Spec §7.1 puts the row-building logic in
Application precisely so it is testable without ClosedXML.

**Files:**
- Create: `src/FamilyTree.Application/Export/IMemberExcelExporter.cs`
- Create: `src/FamilyTree.Application/Export/MemberExportRows.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/MemberExportRowsTests.cs`

**Interfaces:**
- Consumes: `FamilyMemberListItem`, `CountryResponse`, `CaptionLanguage`, `MemberNameComposer`.
- Produces: `MemberExportRow` (eight string-or-int fields, in §19's order);
  `MemberExportRows.Build(items, countries, language) -> IReadOnlyList<MemberExportRow>`;
  `MemberExportRows.Headers(language) -> IReadOnlyList<string>`;
  `IMemberExcelExporter.ExportAsync(MemberFilter, CaptionLanguage, CancellationToken) -> Task<ExcelExportResult>`.
  Used by Tasks 4 and 5.

- [x] **Step 1: Write the failing test**

- The eight headers come back in §19's order, in both languages.
- A fully populated member produces all eight cells.
- The **root** member's Branch cell reads the localised "Root", not blank (§21).
- Generation is the root-relative number the list already carries — assert 0 for the root.
- Status is "Alive"/"Deceased" localised, driven by `IsDeceased` alone. There is no Status
  column in the database and there must not be one here either (spec §2.5).
- A member with no country produces an empty Country cell, not the word "null".
- The country name follows the language: the same member exports "فلسطين" in Arabic and
  "Palestine" in English.
- A country id with no matching country produces an empty cell rather than throwing. The list and
  the country catalog are two responses and can disagree for one request.
- The three identifier fields stay **strings** on the row — `"012345678"` keeps its leading zero
  through the row builder. The cell-type decision is Task 4's, but a row that has already lost
  the zero cannot be saved by it.

- [x] **Step 2: Implement**

`MemberExportRow` holds strings for everything except Generation. Deciding the *cell type* is the
workbook's job; deciding the *text* is this one's.

Localisation follows `CaptionLocalizer`: a small lookup, not a framework. Nothing else in this
codebase is localised server-side, and one more `.resx` for eight headers is not the moment to
start.

- [x] **Step 3: Run the tests** — passes.
- [x] **Step 4: Commit** — `feat: build the member export rows`

---

## Task 4: `ClosedXmlMemberExporter`

The only file that touches ClosedXML.

**Files:**
- Create: `src/FamilyTree.Infrastructure/Export/ClosedXmlMemberExporter.cs`
- Modify: `src/FamilyTree.Api/Program.cs` (registration, beside the PDF exporter's)
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/MemberExcelExportTests.cs`

**Interfaces:**
- Consumes: `FamilyMemberQuery`, `MemberExportRows`, `ICountryService`.
- Produces: `ClosedXmlMemberExporter : IMemberExcelExporter`.

- [ ] **Step 1: Write the failing test**

Read the produced workbook back with ClosedXML and assert:

- One header row plus one row per member, in the list's order.
- **The three identifier columns are text cells.** Assert the *cell data type*, not just the
  displayed value: `123456789` as a number and `123456789` as text render identically in a test
  that only reads `.Value.ToString()`, and the whole point of spec §7.3 is the type.
- A leading-zero national ID survives the round trip with its zero.
- A phone number keeps its `+` and is not read as a formula.
- Generation is a **number** cell — it is a count, and the one column that should sort and filter
  numerically in Excel.
- For Arabic, the worksheet is right-to-left so the column order reads correctly (spec §7.4).
  For English it is not.
- The export respects the filter: assert by **row count** against the same filter run through the
  list endpoint (spec §8).
- An empty result still produces a workbook with its header row. A zero-byte file, or one with no
  headers, reads as a broken download rather than as an empty answer.

- [ ] **Step 2: Implement**

`XLWorkbook`, one worksheet, headers bolded, `SetDataType(XLDataType.Text)` on the three
identifier columns before writing, `worksheet.RightToLeft = language is CaptionLanguage.Ar`,
`AdjustToContents()` on the columns, and `SaveAs(stream)`.

No streaming and no size cap (spec §7.4): 351 members is a small in-memory workbook. Should a
tenant outgrow that, `TooLargeException` is the established way to say so — the PDF exporter's
`MemberCap` is the precedent, and it is deliberately **not** copied here, because a workbook of
strings is nothing like a rendered document of the same size.

- [ ] **Step 3: Run the tests** — passes. Docker must be running.
- [ ] **Step 4: Commit** — `feat: write the members workbook`

---

## Task 5: `GET /api/v1/family-members/export.xlsx`

**Files:**
- Modify: `src/FamilyTree.Api/Endpoints/FamilyMembers/FamilyMemberEndpoints.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/MemberExcelExportTests.cs` (extend)

**Interfaces:**
- Consumes: `MemberFilterBinding`, `CaptionLanguageResolver`, `IMemberExcelExporter`.
- Produces: the endpoint.

- [ ] **Step 1: Write the failing test**

- 200 with the `.xlsx` content type for a `Member.View` holder; **403 without it**, in
  `AuthorizationTests` alongside the existing export guard test.
- 401 unauthenticated.
- `?status=dead` is the same 400 `FILTER_INVALID_STATUS` the list and the tree view give. Three
  callers, one code — this is the third caller `MemberFilterBinding` was extracted for.
- The `Content-Disposition` header carries the family name, percent-encoded per RFC 5987 for
  Arabic. Assert the raw `filename` is **not** the Arabic string: that is the bug the PDF
  endpoint's own test pins, and a second endpoint must not reintroduce it.

- [ ] **Step 2: Implement**

Binds `[AsParameters] MemberFilterRequest`, resolves the language through `CaptionLanguageResolver`
— the same resolver the PDF uses, so one header controls both exports — and returns
`Results.File(..., fileDownloadName: $"{familyTreeName}.xlsx")`.

- [ ] **Step 3: Run the tests** — passes.
- [ ] **Step 4: Commit** — `feat: add the members Excel export endpoint`

---

## Task 6: The download button

**Files:**
- Create: `frontend/src/features/members/membersExportApi.ts`
- Modify: `frontend/src/features/members/MembersPage.tsx`
- Modify: `frontend/src/i18n/locales/en.json`, `ar.json`
- Test: `frontend/src/features/members/membersExportApi.test.ts`
- Test: `frontend/src/features/members/MembersPageExport.test.tsx`

**Interfaces:**
- Consumes: `apiFetchBlob`, `toFilterParams`.
- Produces: `downloadMembersXlsx(filters, language, fileName)`.

- [ ] **Step 1: Write the failing test**

- The request URL carries the **current filters**, built by `toFilterParams` — not re-derived.
  A second serialisation is a second chance to disagree with the server.
- The `Accept-Language` header comes from the app's language toggle, not the browser's locale,
  for the reason `downloadTreePdf` already documents: someone reading the app in Arabic on an
  English-locale browser must not get English headers.
- The object URL is revoked after the click. `downloadTreePdf` does this and says why; a second
  downloader that forgets pins a blob for the tab's lifetime.
- On the page: the button appears for a `Member.View` holder, is hidden without it, and clicking
  it calls the downloader with the filters currently in the URL.
- The button is disabled while the list is empty. Exporting zero rows produces a header-only
  workbook, which is a confusing thing to hand someone who clicked Export.

- [ ] **Step 2: Implement**

`membersExportApi.ts` mirrors `exportApi.ts` — same blob-and-revoke shape, same comment about the
language header, so the two read as siblings.

The button sits in the page header beside Add Member.

- [ ] **Step 3: Add the locale keys** — `members.export`, `members.exportFailed`.
- [ ] **Step 4: Run the checks** — `npm test && npm run lint && npm run build` in `frontend/`.
- [ ] **Step 5: Commit** — `feat: download the filtered members list as a workbook`

---

## Verification

- `dotnet build` — clean, no warnings.
- `dotnet test` — all five projects. The integration suite needs Docker.
- `cd frontend && npm test && npm run lint && npm run build`.
- Manually, against the running stack (rebuild both containers first; confirm the served bundle
  hash matches `frontend/dist/assets/`):
  - Export with no filter downloads a workbook of every member plus a header row.
  - Export with `?status=deceased&generation=2` downloads exactly the rows the page is showing.
  - Open the file: the national ID and both phone numbers are text — the leading zero survives
    and no phone cell shows a formula error. Generation sorts numerically.
  - In Arabic the sheet is right-to-left and the headers are Arabic; in English neither.
  - The downloaded filename carries the Arabic family name.

## What this plan does not do

- No import from Excel. Specification §28 puts it out of scope and spec §10 restates that.
- No export from the Family Tree page. Specification §19's columns are the Members list's; the
  tree already exports to PDF.
- No streaming, no paging, and no member cap on the workbook (spec §7.4).
- No `Status` column in the database, and no stored branch or generation (spec §2.5).
