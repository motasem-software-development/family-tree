# Family Tree SaaS

Multi-tenant family tree platform. Arabic/English, RTL-first.

- **Design spec:** `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md`
- **Current phase:** Phase 4 — Authorization

## Requirements

.NET 10 SDK, Node 24, Docker.

## Running locally

```bash
cp .env.example .env    # then edit: set JWT_SIGNING_KEY and SEED_ADMIN_PASSWORD
```

`JWT_SIGNING_KEY` must be at least 32 bytes (UTF-8) — `JwtTokenService` validates this eagerly
and the API will fail to start on a shorter key. `.env` is git-ignored; never commit it.

Migrations are applied deliberately, never on application startup — production schema
changes belong to the deployment pipeline. Bring postgres up first, apply migrations, then
start the rest of the stack:

```bash
docker compose up -d postgres

export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=familytree;Username=familytree;Password=devpassword"
dotnet ef database update --project src/FamilyTree.Infrastructure --startup-project src/FamilyTree.Api

docker compose up -d
```

The `AddNameTrigramIndex` migration runs `CREATE EXTENSION IF NOT EXISTS pg_trgm`, which
requires privileges a plain application role usually lacks. Local Docker and the Testcontainers
test image both run as superuser, so this is invisible in development. On a managed or
least-privilege database, have a superuser run `CREATE EXTENSION pg_trgm;` once before applying
migrations — the `IF NOT EXISTS` guard then makes the migration a no-op.

The SPA is on http://localhost:8080 and the API on http://localhost:5000.

The `api` service must stay single-instance: startup seeding has no advisory lock, so two
replicas booting at once could race on the same seeded tenant/admin.

The members screen is at `/members` once signed in, and the tree outline is at `/`. Search runs
server-side and returns each hit's ancestor path; the outline renders only the rows in view.

## Tests

```bash
dotnet test                    # unit + integration (integration needs Docker running)
cd frontend && npm test        # component tests
```

## Architecture

Modular monolith. Dependencies point inward: `Domain` → nothing, `Application` → `Domain`,
`Infrastructure` → `Application`, `Api` → all. Tenant isolation is enforced in three layers —
EF global query filters, service-level ownership assertions, and database constraints.

## API error codes

Errors are RFC 7807 Problem Details carrying a stable `code`. Clients translate from the code;
message text is not part of the contract.

| Code | Status | Meaning |
|---|---|---|
| `MEMBER_NAME_REQUIRED` | 400 | Name missing or whitespace |
| `MEMBER_NAME_TOO_LONG` | 400 | Name exceeds 200 characters |
| `MEMBER_PARENT_NOT_FOUND` | 400 | Parent id unknown within this family tree |
| `MEMBER_FIELD_NOT_UPDATABLE` | 400 | Attempt to change parent, tenant, or tree via PUT |
| `FAMILY_TREE_NAME_REQUIRED` | 400 | Family tree name missing or whitespace |
| `FAMILY_TREE_NAME_TOO_LONG` | 400 | Family tree name exceeds 200 characters |
| `MEMBER_NOT_FOUND` | 404 | No such member for this tenant |
| `FAMILY_TREE_NOT_FOUND` | 404 | This tenant has no family tree |
| `MEMBER_HAS_CHILDREN` | 409 | Cannot delete a member who has children |
| `CONCURRENCY_CONFLICT` | 409 | The member changed since it was read |
| `INVALID_CREDENTIALS` | 401 | Login failed |
| `INVALID_REFRESH_TOKEN` | 401 | Refresh token unknown, rotated, or revoked |
| `ACCOUNT_INACTIVE` | 401 | The authenticated account has been deactivated |
| `TENANT_INACTIVE` | 401 | The authenticated account's tenant subscription is inactive |
| `EXPORT_TREE_TOO_LARGE` | 413 | Tree exceeds the export member cap, or cannot fit one sheet legibly. The `reason` extension is `member-cap` or `sheet-overflow`. |

## User and role management

The first administrator is created by startup seeding, not through the UI — it is the only
account that exists before anyone can sign in, and is tied to the seeded tenant via
`SEED_TENANT_SLUG`/`SEED_ADMIN_PASSWORD` in `.env`.

From there, an administrator creates every other user through the Users screen. Each new user
gets a temporary password chosen by the administrator; the server marks the account
`mustChangePassword`, and the account cannot reach any screen but the forced change-password
page until it replaces that password at first sign-in. This is enforced server-side
(`PasswordChangeGateMiddleware`, keyed off a JWT claim) — the frontend redirect to
`/change-password` is UX only, not the enforcement point.

The built-in system roles cannot be edited or deleted; only custom roles can be. Every user
deactivation, role edit, and role deletion is checked against a last-administrator lockout
guard: the system refuses any change that would leave no active user holding both `User.Edit`
and `Role.Edit` (accumulated across that user's roles, not necessarily from a single one), so a
tenant can never lock itself out of user and role management.

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

## PDF export

`GET /api/v1/family-tree/export.pdf?rootId=<guid>&maxDepth=<n>&style=<xmind|clean>&page=<sheet|a4>`
requires `FamilyTree.View` and streams a PDF poster of the tree.

`style` chooses between the mind-map replica of `familytree.pdf` and a cleaner single-direction
design; `page` chooses one tall sheet or tiled A4. `rootId` selects a **subtree** — it does not
re-root, so no value reproduces the reference's centring of سليمان. Design:
`docs/superpowers/specs/2026-08-18-tree-pdf-export-design.md`.

Arabic is shaped with HarfBuzz against embedded Noto fonts, and the output keeps a `/ToUnicode`
map, so names in the PDF stay selectable and searchable.

A single sheet is capped at 14,400 pt by the PDF format. Past that the diagram is scaled down;
below a 6 pt font it is refused with `EXPORT_TREE_TOO_LARGE` rather than emitted illegibly.

The API image installs `libfontconfig1` and `libfreetype6` for SkiaSharp, and the project
references both `SkiaSharp.NativeAssets.Linux` and `HarfBuzzSharp.NativeAssets.Linux`. All four
are required: miss any one and startup still succeeds, but the endpoint throws at first use.
