# Family Tree SaaS

Multi-tenant family tree platform. Arabic/English, RTL-first.

- **Design spec:** `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md`
- **Current phase:** Phase 2 — Family Tree

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

The SPA is on http://localhost:8080 and the API on http://localhost:5000.

The `api` service must stay single-instance: startup seeding has no advisory lock, so two
replicas booting at once could race on the same seeded tenant/admin.

The members screen is at `/members` once signed in. Tree visualization is Phase 3.

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
| `MEMBER_NOT_FOUND` | 404 | No such member for this tenant |
| `FAMILY_TREE_NOT_FOUND` | 404 | This tenant has no family tree |
| `MEMBER_HAS_CHILDREN` | 409 | Cannot delete a member who has children |
| `CONCURRENCY_CONFLICT` | 409 | The member changed since it was read |
| `INVALID_CREDENTIALS` | 401 | Login failed |
| `INVALID_REFRESH_TOKEN` | 401 | Refresh token unknown, rotated, or revoked |
