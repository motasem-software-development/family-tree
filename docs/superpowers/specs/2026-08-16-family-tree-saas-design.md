# Family Tree SaaS — V1 Design

**Date:** 2026-08-16
**Status:** Approved design, ready for implementation planning
**Source requirements:** `Family Tree SaaS Platform.md` (functional SRS), `Family Tree SaaS.md` (technical architecture)

This document records the design decisions for V1. Where it differs from the two source
specifications, the difference is stated explicitly and the reason given. Where the sources
already decided something, this document does not repeat the reasoning.

---

## 1. Scope

V1 delivers a multi-tenant web application in which a family maintains a hierarchy of male
family members under a single root family name, visualizes it interactively, controls access
through custom permission-based roles, shares a read-only public link, and keeps an audit
trail of changes.

### 1.1 Decisions taken during design

These resolve gaps in the source specifications.

| Question | Decision |
|---|---|
| Tenant provisioning | **Single seeded tenant.** The full multi-tenant schema (`TenantId` everywhere, isolation enforced at three layers) is built, but only one tenant is created, by seed. No public signup, no platform-admin UI, no email verification in V1. |
| Language | **Bilingual Arabic / English with a switcher.** i18n infrastructure and direction handling are Phase 1 work, not a retrofit. |
| Tree layout | **User-toggleable orientation:** sideways-RTL (matching the reference), sideways-LTR, and top-down. |
| Tree scale | **Full-tree load in V1, designed to grow.** Optional `rootId` / `maxDepth` parameters implemented from the start; frontend data access behind an interface. |
| Seed data | **Import the real ~350-member tree** from `familytree.pdf` as the seeded tenant's tree, behind a human verification gate (§7.2). |
| Billing | **Out of scope.** `Tenant.IsActive` is the only account-level gate. |
| Public link search | **Per-link toggle**, set at link creation. |
| Member deletion | **Hard delete.** The audit row's `old_values` is the record that the member existed. |

### 1.2 Out of scope

As listed in SRS §37 (female members, spouses, marriage, photos, dates, biography, documents,
events, GEDCOM import/export, multiple trees per customer, native mobile), plus billing,
self-service signup, Redis, and background jobs. The data model must not make these
impossible; it must not implement them.

---

## 2. Architecture

A modular monolith. One ASP.NET Core application, internally divided by feature, deployable
as a single container.

### 2.1 Backend projects

```
src/
├── FamilyTree.Domain/          entities, business rules, domain exceptions — no EF, no ASP.NET
├── FamilyTree.Application/     use-case services, validators, permission evaluation
├── FamilyTree.Infrastructure/  EF Core, Identity, repositories, persistence configuration
├── FamilyTree.Api/             endpoints, middleware, composition root
└── FamilyTree.Contracts/       request/response DTOs
```

Dependencies point inward only. `Domain` references nothing.

**Folder organization is feature-first within every layer**, not layer-first. `Api/Endpoints/Members/`,
`Application/Members/`, `Domain/Entities/`, and the mirrored test projects. Rationale: a change
to one feature touches adjacent directories rather than five scattered ones, and files stay
small.

**Not used:** microservices, MediatR/CQRS, Redis, background job scheduler. With roughly eight
use-case groups, MediatR adds indirection without paying for itself; plain injected service
classes are more readable and equally testable. The others are ruled out by the source specs.

### 2.2 Frontend

```
frontend/src/
├── app/
├── components/
├── features/
│   ├── auth/  family-tree/  members/  users/  roles/  audit/  public-tree/
├── i18n/          locale resources, direction handling
├── hooks/  services/  types/  routes/  layouts/  utils/
```

`features/family-tree/layout/` is a **pure, dependency-free module**: it imports no React and
knows nothing about the API. It takes a node graph and returns coordinates.

### 2.3 Tenant context

A `TenantContext` is resolved once per request, in middleware, from the authenticated
principal's claims. It is **never** read from a header, query string, or route value.

```csharp
public interface ITenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
}
```

Application services receive it by injection. No service method accepts a `tenantId`
parameter, so no caller can supply the wrong one. This is a structural guarantee, not a
convention to remember.

---

## 3. Data model

Entities follow the technical specification §7–§11. This section records only the additions
and the constraints that need a specific mechanism.

### 3.1 Additions to the specified entities

| Entity | Field | Purpose |
|---|---|---|
| `family_members` | `version` | Optimistic concurrency token. A stale update returns 409 rather than silently overwriting. |
| `public_access_links` | `allow_search` | Per-link public search toggle. |
| `roles` | `is_system` | Marks the four seeded roles so they cannot be deleted or stripped of permissions. Custom roles alongside them remain fully editable. |
| `AspNetUsers` | `tenant_id`, `is_active`, `last_login_at` | Tenant membership and account state. |
| new table | `refresh_tokens` | Hashed refresh tokens, one row per device, with rotation and revocation. |

ASP.NET Identity's own role tables are unused. Roles here are tenant-scoped and
permission-backed; Identity's global roles cannot express that.

### 3.2 Tenant isolation — three layers

1. **EF Core global query filters** on every tenant-owned entity, keyed off the injected
   `TenantContext`. A forgotten `.Where(x => x.TenantId == ...)` is therefore not a
   vulnerability, because the filter is always applied.
2. **Explicit ownership assertion** in the application service before any mutation.
3. **Database constraints** — foreign keys and the composite constraint in §3.3.

PostgreSQL Row-Level Security is a documented future option, not V1 work.

### 3.3 Parent and child must belong to the same tree

Enforced physically, not by service code:

```sql
ALTER TABLE family_members
    ADD CONSTRAINT uq_member_id_tree UNIQUE (id, family_tree_id);

ALTER TABLE family_members
    ADD CONSTRAINT fk_member_parent
    FOREIGN KEY (parent_id, family_tree_id)
    REFERENCES family_members (id, family_tree_id);
```

A cross-tree parent link becomes unrepresentable — the database rejects it even if every
layer above has a bug. Cost: one redundant index.

### 3.4 Indexes

Those listed in the technical specification §12, plus a `pg_trgm` GIN index on `name`.
A B-tree index only helps prefix matches; users search by fragment, and Arabic names make
that more pronounced. The real tree also contains many repeated names (dozens of محمد,
أحمد), which drives the ancestor-path requirement in §5.4.

### 3.5 Cycle detection

A recursive CTE walking upward from the proposed parent, executed inside the move
transaction. Not an in-memory loop: it is one query regardless of tree depth, and it reads
the same snapshot the update writes.

### 3.6 Generation

Never stored. Computed during tree assembly and returned in the DTO, per SRS §32.

### 3.7 Audit

`audit_logs` rows are insert-only — no update or delete path exists in the codebase.
`old_values` and `new_values` are `jsonb`. Because deletion is a hard delete, the audit row
is the only remaining record that a member existed; its `old_values` carries the full member
snapshot rather than only the changed field.

---

## 4. API and authorization

### 4.1 Shape

Minimal API endpoints under `/api/v1`, grouped in feature files, returning DTOs from
`Contracts`. OpenAPI generated at build; interactive UI exposed in development only.
Endpoints follow the technical specification §21–§31.

### 4.2 Authentication

ASP.NET Core Identity for the user store and password hashing. The API issues a short-lived
JWT access token (15 minutes) plus a refresh token stored **hashed** in `refresh_tokens`,
with rotation on use and a revocation flag. One row per device, so "sign out everywhere" and
forced revocation on account deactivation both work.

**No email in V1** — a consequence of having no signup and no background job system. This
makes user provisioning and password recovery administrator-driven: a user with
`User.Create` creates the account and sets an initial password, which the new user changes at
first login; password reset is likewise performed by an administrator. Identity's
token-based email flows are left unwired rather than removed, so adding SMTP later is a
configuration and endpoint change, not a redesign.

### 4.3 Authorization

Permission-based, never role-name-based. Each permission code from SRS §21 is registered as
an authorization policy at startup. A single handler resolves the user's effective permission
set — the union of their roles' permissions — cached for the request. Endpoints declare the
permission they require:

```csharp
.RequirePermission(Permissions.Member.Move)
```

Adding a permission later means adding a constant and a seed row. No handler changes.

### 4.4 Cross-tenant responses

A request for an entity belonging to another tenant returns **404, not 403**. A 403 confirms
the identifier exists, which itself leaks information. This is the uniform rule across
members, trees, users, roles, audit records, and public links.

### 4.5 Tree endpoint

`GET /api/v1/family-tree/view` returns the whole tree in V1, and accepts optional `rootId`
and `maxDepth` parameters from the start, with a `hasMoreChildren` flag on truncated nodes.
Server-side this is a small amount of work during tree assembly, and it makes the growth path
real rather than aspirational. The frontend calls it through a `TreeDataSource` interface, so
moving to incremental fetching later never touches the renderer.

### 4.6 Mutating operations

`POST /api/v1/family-members/{id}/move` is a dedicated command, as specified. `PUT` on a
member **rejects** any attempt to change `parentId`, `tenantId`, or `familyTreeId` — it does
not silently ignore them.

Move and delete each run in a single transaction covering the state change and the audit
insert. If the audit insert fails, the operation fails.

### 4.7 Public endpoints

Under `/api/v1/public/`, unauthenticated, with their own stricter rate limit. The response
DTO **shares no type** with the authenticated tree DTO, so a field added for administrators
cannot accidentally leak publicly. Token lookup is by hash; revoked or inactive links return
404.

### 4.8 Errors

RFC 7807 Problem Details with a stable machine-readable `code` (`MEMBER_HAS_CHILDREN`,
`MOVE_CREATES_CYCLE`, `CONCURRENCY_CONFLICT`, …). The frontend maps codes to translated
messages. Message text is not part of the contract — it cannot be, given the bilingual UI.

---

## 5. Tree visualization

### 5.1 Rendering approach

A custom SVG renderer using `d3-hierarchy` for layout mathematics only.

Rejected alternatives: node-graph libraries (React Flow and similar) carry interaction
machinery for user-arranged graphs that is not needed here, and resist both RTL and automatic
tidy layout; charting libraries render a tree but not permission-aware per-node context
menus. `d3-hierarchy` supplies exactly the Reingold–Tilford tidy-tree algorithm — correct
sibling packing, no overlaps, variable node sizes — in roughly 8 KB, with no DOM opinions.

### 5.2 Orientation-agnostic layout

The layout module computes in abstract axes — *depth* (generation) and *breadth* (sibling
spread) — and knows nothing about screens. A separate projection step maps those to screen
x/y for each mode:

- **sideways-RTL** — root at right, generations flowing left (matches the reference document)
- **sideways-LTR** — mirrored
- **top-down** — root at top

Because this is one transform rather than three layout implementations, the orientation
toggle is inexpensive and the modes cannot drift out of sync.

### 5.3 Node sizing

Node width depends on rendered text width. Arabic metrics differ enough from Latin that a
fixed width either clips or wastes space. Text is measured with canvas `measureText` against
the actually loaded font, memoized per string — measured once, reused across re-layouts.

### 5.4 Performance and interaction

- Layout is a pure function memoized on (tree data, collapse state, orientation).
- Rendering is viewport-virtualized: only nodes whose projected bounds intersect the visible
  rectangle plus a margin are in the DOM.
- Zoom and pan are a single `transform` on the root `<g>` — no re-layout, no React re-render.
- **Search** queries the server (trigram index). Each result carries its **ancestor path**,
  which is required rather than decorative: many members share a name. Selecting a result
  expands collapsed ancestors, animates the viewport to center the node, and highlights it.

### 5.5 Node component contract

`TreeNode` is presentational. It receives data and permission flags and emits events; it
makes no API calls and imports no query hooks. The context menu displays only the actions the
user's permissions allow, and the server enforces those permissions independently regardless
of what the UI displayed.

---

## 6. Testing

**Unit tests** cover the rules that carry real risk: cycle detection, delete-with-children,
move validation, permission evaluation, generation calculation, and the layout engine —
including that every orientation produces non-overlapping node bounds.

**Integration tests** run against real PostgreSQL via Testcontainers, never an in-memory
provider. Recursive CTEs, composite foreign keys, trigram search, and transaction behavior do
not exist in a fake.

**Tenant isolation is a standing test requirement**: every tenant-owned endpoint has a
cross-tenant case asserting 404, per technical specification §47. Note that although
production seeds exactly one tenant (§1.1), the integration test fixtures seed **two** — the
isolation guarantee is untestable otherwise.

**Frontend** gets component tests for tree interactions plus a Playwright suite covering
login → add member → move member → generate and open public link.

Coverage target 80%, with the domain layer expected near 100%.

---

## 7. Data import

### 7.1 Situation

The original `.xmind` file is unavailable; `familytree.pdf` is the only source. It is an
XMind 10 PDF export containing approximately 350 male Arabic names with their full hierarchy.
The hierarchy is present only as drawn connector geometry, and the text is stored as reversed
Arabic presentation-form glyphs. Exploratory decoding has confirmed that node rectangles,
text runs, and connector polylines are all extractable and the coordinates are clean.

### 7.2 Approach and verification gate

1. Extract node rectangles, text runs, and connector polylines from the PDF content stream.
2. Assign each text run to its containing rectangle.
3. Derive parent-child links from each elbow connector's endpoints.
4. Normalize Arabic from reversed presentation forms to logical Unicode.
5. **Emit a human-readable indented tree file for review.**
6. Only after that file is confirmed correct does the result become seed data.

Step 5 is a gate, not a formality. If reconstruction is ambiguous anywhere, it surfaces in a
reviewable artifact rather than in production data.

**Risk, stated plainly:** this is the one V1 item whose effort cannot be estimated precisely
in advance, because it depends on how regular the exported geometry turns out to be. It is
sequenced early (§8) so that any difficulty is discovered before later phases depend on it.

---

## 8. Delivery sequence

Following technical specification §57, with three changes.

| Phase | Contents |
|---|---|
| 1 — Foundation | Solution setup, .NET 10 API, React app, PostgreSQL, EF Core, migrations, authentication, tenant model, tenant context middleware, **i18n infrastructure** |
| 2 — Family tree | Tree and root family, member create / edit / delete, parent-child hierarchy |
| **2.5 — Data import** | PDF reconstruction, verification artifact, seed migration |
| 3 — Visualization | **Layout engine first**, then SVG renderer, zoom, pan, expand/collapse, search, node actions, orientation toggle |
| 4 — Authorization | Permissions, roles, custom roles, user management |
| 5 — Advanced operations | Move member, cycle detection, relationship history, audit logs |
| 6 — Public access | Link creation with search toggle, public viewer, revocation |
| 7 — Hardening | Observability, security, performance, integration testing, CI/CD, backup |

**Why these three changes:** i18n moves into Phase 1 because bilingual support cannot be
retrofitted cheaply across every screen. The import slots in after Phase 2 so that every
later phase is exercised against 350 real Arabic names rather than synthetic data. The layout
engine is built before the renderer so it never accumulates React dependencies.

---

## 9. Definition of done

Per technical specification §58, unchanged: backend and frontend complete, authorization
implemented, tenant isolation verified, validation implemented, unit tests for business
rules, integration tests for database behavior, API documentation updated, error handling
implemented, audit requirements implemented where applicable, UI correct in RTL, feature
verified against PostgreSQL, and no business rule enforced only in the frontend.

---

## 10. Traceability

All twenty-five business rules (SRS §35, BR-001 … BR-025) and all eighteen success criteria
(SRS §40) are carried forward unchanged. The design decisions in §1.1 do not alter any of
them; they resolve questions the source documents left open.
