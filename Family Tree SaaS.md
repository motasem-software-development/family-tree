# Family Tree SaaS
## Technical Architecture & Development Specification

**Version:** 1.0  
**Backend:** ASP.NET Core / .NET 10  
**Frontend:** React 19 + TypeScript  
**Database:** PostgreSQL  
**ORM:** Entity Framework Core + Npgsql  
**Architecture:** Modular Monolith  
**Deployment:** Containerized / Docker-ready

---

# 1. Technology Stack

## 1.1 Backend

Use:

- .NET 10
- ASP.NET Core Web API
- C# 14
- Entity Framework Core 10
- Npgsql PostgreSQL provider
- ASP.NET Core Identity
- JWT authentication
- OpenAPI
- FluentValidation
- Serilog
- OpenTelemetry

.NET 10 is a good choice here because it is an LTS release and includes current ASP.NET Core and EF Core improvements.

Npgsql provides the PostgreSQL EF Core provider and integrates directly with EF Core through `UseNpgsql()`.

---

# 2. Frontend

Use:

- React 19
- TypeScript
- Vite
- React Router
- TanStack Query
- React Hook Form
- Zod
- Axios or native `fetch`
- CSS Modules / Tailwind CSS
- An SVG/canvas-based tree visualization library or a custom SVG renderer

React should be used as the UI framework, while TypeScript should be mandatory.

React's component model is well suited to separating the administration UI, tree viewer, member dialogs, permissions UI, and public viewer.

---

# 3. High-Level Architecture

The recommended architecture is:

```text
                        Internet
                           |
                           v
                    Reverse Proxy
                    / Load Balancer
                           |
             -----------------------------
             |                           |
             v                           v
       React Frontend              ASP.NET Core API
                                         |
                  -----------------------------------------
                  |          |          |        |         |
                  v          v          v        v         v
               Identity    Family     Members   Users    Audit
                           Tree
                              |
                              v
                         PostgreSQL
```

For V1, all backend modules should run inside one ASP.NET Core application.

This is a **modular monolith**, not a collection of tightly coupled services.

---

# 4. Why Modular Monolith

I do not recommend starting this application with:

```text
Auth Service
Family Service
Member Service
Audit Service
User Service
Notification Service
...
```

There is no need for that complexity at this stage.

The domain is relatively cohesive and the expected workload of an individual family tree is unlikely to justify microservices.

Instead:

```text
FamilyTree.Api
    |
    +-- Identity
    +-- Tenants
    +-- FamilyTrees
    +-- Members
    +-- Authorization
    +-- PublicAccess
    +-- Audit
```

Each module should have clear boundaries.

If the SaaS grows significantly later, individual modules can be extracted into services.

---

# 5. Recommended Solution Structure

```text
FamilyTree.sln

src/
│
├── FamilyTree.Api/
│   ├── Program.cs
│   ├── Middleware/
│   ├── Endpoints/
│   └── Configuration/
│
├── FamilyTree.Application/
│   ├── Tenants/
│   ├── FamilyTrees/
│   ├── Members/
│   ├── Users/
│   ├── Roles/
│   ├── Permissions/
│   ├── PublicAccess/
│   └── Audit/
│
├── FamilyTree.Domain/
│   ├── Entities/
│   ├── ValueObjects/
│   ├── Enums/
│   ├── Exceptions/
│   └── Interfaces/
│
├── FamilyTree.Infrastructure/
│   ├── Persistence/
│   ├── Identity/
│   ├── Repositories/
│   ├── Services/
│   └── Configurations/
│
└── FamilyTree.Contracts/
    ├── Members/
    ├── FamilyTrees/
    ├── Users/
    ├── Roles/
    └── Authentication/
```

Frontend:

```text
frontend/

src/
├── app/
├── components/
├── features/
│   ├── auth/
│   ├── family-tree/
│   ├── members/
│   ├── users/
│   ├── roles/
│   ├── audit/
│   └── public-tree/
│
├── hooks/
├── services/
├── types/
├── routes/
├── layouts/
└── utils/
```

---

# 6. Domain Model

The primary entities are:

```text
Tenant
   |
   +---- FamilyTree
   |
   +---- User
   |
   +---- Role
   |
   +---- AuditLog
```

Family hierarchy:

```text
FamilyTree
   |
   +---- FamilyMember
             |
             +---- FamilyMember
                       |
                       +---- FamilyMember
```

Authorization:

```text
User
  |
  +---- UserRole
           |
           +---- Role
                  |
                  +---- RolePermission
                           |
                           +---- Permission
```

Public access:

```text
FamilyTree
   |
   +---- PublicAccessLink
```

---

# 7. Entity: Tenant

Represents the SaaS customer.

```text
Tenant
--------------------
Id
Name
Slug
IsActive
CreatedAt
UpdatedAt
```

### Requirements

- `Id` should be a UUID.
- `Slug` should be unique.
- `IsActive` controls whether the tenant can use the application.
- A tenant owns exactly one family tree in V1.

---

# 8. Entity: FamilyTree

```text
FamilyTree
--------------------
Id
TenantId
Name
IsActive
CreatedAt
UpdatedAt
```

### Constraints

```text
TenantId UNIQUE
```

because:

```text
One Tenant = One FamilyTree
```

### Important

Do not use the family name as the primary key.

Correct:

```text
FamilyTree.Id = UUID
FamilyTree.Name = "Al-Saqqa Family"
```

Incorrect:

```text
FamilyTree.Id = "Al-Saqqa"
```

The name may change.

The ID should not.

---

# 9. Entity: FamilyMember

```text
FamilyMember
--------------------
Id
TenantId
FamilyTreeId
ParentId
Name
CreatedAt
UpdatedAt
```

Relationships:

```text
FamilyMember.ParentId
        |
        v
FamilyMember.Id
```

This is a self-referencing relationship.

Example:

```text
Suleiman
   |
   +-- Faris
         |
         +-- Mahmoud
```

Database:

```text
Faris.ParentId = Suleiman.Id
Mahmoud.ParentId = Faris.Id
```

---

# 10. Root Representation

The root family is not a `FamilyMember`.

Do NOT create:

```text
FamilyMember
Name = "Al-Saqqa Family"
```

Instead:

```text
FamilyTree
Name = "Al-Saqqa Family"
```

First-generation members have:

```text
ParentId = NULL
```

Example:

```text
FamilyTree
Name = Al-Saqqa Family

Members:

Suleiman
ParentId = NULL

Omar
ParentId = NULL

Ahmed
ParentId = NULL
```

The API interprets `ParentId = NULL` as a first-generation member belonging directly to the root family.

---

# 11. Database Schema

Recommended PostgreSQL structure:

```sql
CREATE TABLE tenants (
    id UUID PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    slug VARCHAR(100) NOT NULL UNIQUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE family_trees (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT fk_family_tree_tenant
        FOREIGN KEY (tenant_id)
        REFERENCES tenants(id)
);

CREATE TABLE family_members (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    family_tree_id UUID NOT NULL,
    parent_id UUID NULL,
    name VARCHAR(200) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT fk_member_tenant
        FOREIGN KEY (tenant_id)
        REFERENCES tenants(id),

    CONSTRAINT fk_member_tree
        FOREIGN KEY (family_tree_id)
        REFERENCES family_trees(id),

    CONSTRAINT fk_member_parent
        FOREIGN KEY (parent_id)
        REFERENCES family_members(id)
);
```

---

# 12. Database Indexes

At minimum:

```sql
CREATE INDEX ix_family_members_tree
ON family_members(family_tree_id);

CREATE INDEX ix_family_members_parent
ON family_members(parent_id);

CREATE INDEX ix_family_members_tree_parent
ON family_members(family_tree_id, parent_id);

CREATE INDEX ix_family_members_name
ON family_members(family_tree_id, name);
```

The `(family_tree_id, parent_id)` index is particularly important because tree traversal will frequently query:

```text
Give me children of this member within this family.
```

---

# 13. Tenant Isolation

Tenant isolation is one of the most important technical requirements.

Every authenticated request should establish:

```text
TenantContext
```

Example:

```csharp
public interface ITenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
}
```

Application services must use the current tenant context.

Never trust:

```http
X-Tenant-Id: some-other-tenant
```

sent by the frontend.

The server must determine the tenant from the authenticated user's membership.

---

# 14. Tenant Isolation Pattern

Every query should effectively be:

```csharp
_context.FamilyMembers
    .Where(x => x.TenantId == tenantContext.TenantId);
```

Do not allow application services to arbitrarily choose the tenant.

Prefer:

```csharp
familyMemberService.GetMember(id);
```

over:

```csharp
familyMemberService.GetMember(tenantId, id);
```

where possible.

The service obtains the tenant from the authenticated context.

---

# 15. Defense in Depth

Tenant isolation should exist at multiple layers:

```text
JWT/User Identity
       |
       v
Tenant Context
       |
       v
Application Service
       |
       v
EF Core Query
       |
       v
PostgreSQL
```

For additional protection, PostgreSQL Row-Level Security can be considered later for high-assurance SaaS isolation.

For V1, application-level tenant isolation with strong integration tests is sufficient, provided it is implemented consistently.

---

# 16. Authentication

Use ASP.NET Core Identity for:

- Users
- Password hashing
- Account activation
- Password reset
- Email confirmation
- Lockout
- Authentication state

The API can issue JWT access tokens for the React application.

Recommended:

```text
Access Token
+
Refresh Token
```

Access tokens should be short-lived.

Refresh tokens should be stored securely and revocable.

---

# 17. User Entity

ASP.NET Identity can provide the basic user entity.

Conceptually:

```text
ApplicationUser
-------------------
Id
Email
UserName
PasswordHash
IsActive
CreatedAt
LastLoginAt
```

The user is associated with a Tenant.

Because V1 uses one family tree per tenant:

```text
User
  |
  +-- Tenant
         |
         +-- FamilyTree
```

---

# 18. Roles and Permissions

Do not implement authorization as:

```csharp
if (user.Role == "Admin")
```

Instead use permissions.

Example:

```text
Member.View
Member.Create
Member.Edit
Member.Move
Member.Delete

User.View
User.Create
User.Edit
User.Deactivate

Role.View
Role.Create
Role.Edit
Role.Delete

Audit.View

PublicLink.Create
PublicLink.Revoke
```

This allows custom roles.

---

# 19. Authorization Data Model

```text
roles
----------------
id
tenant_id
name
description
created_at


permissions
----------------
id
code
description


role_permissions
----------------
role_id
permission_id


user_roles
----------------
user_id
role_id
```

A role belongs to a tenant.

Permissions are system-level definitions.

---

# 20. Custom Roles

Example:

```text
Role:
Genealogy Editor
```

Permissions:

```text
Member.View
Member.Create
Member.Edit
Member.Move
```

The backend should provide:

```text
POST /api/roles
PUT  /api/roles/{id}
DELETE /api/roles/{id}
```

Deleting a role that is still assigned to users should be prevented unless users are reassigned.

---

# 21. Family Member API

Recommended endpoints:

```http
GET /api/v1/family-tree
```

Returns family tree information.

```http
GET /api/v1/family-members
```

Returns members, preferably with optional tree-loading parameters.

```http
GET /api/v1/family-members/{id}
```

Returns one member.

```http
POST /api/v1/family-members
```

Creates a first-generation or descendant member.

Request:

```json
{
  "name": "Faris",
  "parentId": "..."
}
```

`parentId = null` means the member is directly under the root family.

---

# 22. Add Member

```http
POST /api/v1/family-members
```

Request:

```json
{
  "name": "Faris",
  "parentId": "..."
}
```

Backend validation:

1. Name is required.
2. Name length is valid.
3. Parent exists if supplied.
4. Parent belongs to current tenant.
5. Parent belongs to current family tree.
6. User has `Member.Create`.

---

# 23. Update Member

```http
PUT /api/v1/family-members/{id}
```

Request:

```json
{
  "name": "Faris Ahmed"
}
```

The operation must not permit changing:

- TenantId
- FamilyTreeId
- Id

through the normal update endpoint.

---

# 24. Move Member

Use a dedicated command rather than allowing `ParentId` to be changed through the normal update endpoint.

```http
POST /api/v1/family-members/{id}/move
```

Request:

```json
{
  "newParentId": "..."
}
```

This makes the business operation explicit.

The service should:

1. Load the member.
2. Validate tenant.
3. Validate permission.
4. Validate new parent.
5. Detect cycles.
6. Record previous parent.
7. Change parent.
8. Save audit record.
9. Commit the transaction.

All of this should happen in one database transaction.

---

# 25. Cycle Detection

Before moving:

```text
Ahmed
 |
 +-- Mohamed
       |
       +-- Ali
```

If the request is:

```text
Move Ahmed under Ali
```

the operation must be rejected.

Algorithm:

1. Start with proposed parent.
2. Walk upward using `ParentId`.
3. If the moving member is encountered, reject.
4. Continue until `ParentId = NULL`.

For very large trees, this should be implemented carefully and tested for performance.

---

# 26. Delete Member

```http
DELETE /api/v1/family-members/{id}
```

Before deletion:

```text
SELECT EXISTS
FROM family_members
WHERE parent_id = @memberId;
```

If children exist:

```text
HTTP 409 Conflict
```

Response:

```json
{
  "code": "MEMBER_HAS_CHILDREN",
  "message": "This member cannot be deleted because they have children."
}
```

Do not rely only on the UI to enforce this rule.

---

# 27. Tree Query

The frontend should not necessarily receive the entire database entity graph.

Create a tree-specific DTO:

```csharp
public sealed record FamilyTreeNodeDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Generation,
    IReadOnlyList<FamilyTreeNodeDto> Children);
```

For moderate trees, the backend can return the complete hierarchy.

For very large trees, introduce:

```text
GET /api/v1/family-tree/nodes
```

with:

```text
rootId
depth
includeChildren
```

to support partial loading.

---

# 28. Recommended Initial Tree API

```http
GET /api/v1/family-tree/view
```

Response:

```json
{
  "id": "...",
  "name": "Al-Saqqa Family",
  "rootMembers": [
    {
      "id": "...",
      "name": "Suleiman",
      "parentId": null,
      "children": [
        {
          "id": "...",
          "name": "Faris",
          "children": []
        }
      ]
    }
  ]
}
```

This is easier for React to consume than exposing raw EF entities.

---

# 29. Public Tree API

Public access should use a separate endpoint.

```http
GET /api/v1/public/family-trees/{publicToken}
```

No authentication required.

Response should contain only public-safe information.

Do not expose:

- TenantId
- Internal user IDs
- Audit information
- Permissions
- Internal database IDs unless necessary

---

# 30. Public Access Links

Entity:

```text
PublicAccessLink
----------------------
Id
TenantId
FamilyTreeId
TokenHash
IsActive
CreatedAt
CreatedBy
RevokedAt
```

Do not store a raw public token if avoidable.

Generate a cryptographically secure random token.

Store its hash.

The raw token is returned to the administrator when the link is created.

---

# 31. Public Link API

Create:

```http
POST /api/v1/public-links
```

Revoke:

```http
DELETE /api/v1/public-links/{id}
```

List:

```http
GET /api/v1/public-links
```

Public access:

```http
GET /api/v1/public/family-trees/{token}
```

---

# 32. Audit Architecture

Create an `AuditLog` entity.

```text
AuditLog
--------------------
Id
TenantId
UserId
Action
EntityType
EntityId
OldValues
NewValues
CreatedAt
```

For `OldValues` and `NewValues`, PostgreSQL `jsonb` is appropriate.

Example:

```json
{
  "parentId": "old-parent-id"
}
```

to:

```json
{
  "parentId": "new-parent-id"
}
```

This is especially useful for member movement.

---

# 33. Transactions

Operations that change multiple pieces of state should use a database transaction.

For example:

### Move Member

```text
BEGIN

Update Member.ParentId

Insert AuditLog

COMMIT
```

If audit insertion fails, the member move should also fail.

Likewise:

### Delete Member

```text
BEGIN

Validate no children
Delete Member
Insert AuditLog

COMMIT
```

---

# 34. Frontend Architecture

React application:

```text
App
 |
 +-- Authentication
 |
 +-- Dashboard
 |
 +-- Family Tree
 |      |
 |      +-- Tree Canvas
 |      +-- Node
 |      +-- Node Context Menu
 |      +-- Search
 |      +-- Zoom Controls
 |
 +-- Members
 |
 +-- Users
 |
 +-- Roles
 |
 +-- Audit
 |
 +-- Public Link
```

---

# 35. React State Management

Use:

### TanStack Query

For:

- API calls.
- Server state.
- Caching.
- Mutation handling.
- Invalidating tree data.

Use local React state for:

- Selected node.
- Zoom.
- Pan.
- Dialog state.
- Tree UI state.

Avoid introducing Redux unless the application later develops a genuine need for a global client-side state store.

---

# 36. Tree Visualization

The tree renderer is the most specialized frontend component.

I recommend rendering the tree using SVG rather than regular HTML elements.

Conceptually:

```text
<svg>
    <line />
    <line />

    <g>
        <rect />
        <text>Suleiman</text>
    </g>

    <g>
        <rect />
        <text>Faris</text>
    </g>
</svg>
```

SVG provides:

- Connecting lines.
- Zooming.
- Panning.
- Precise positioning.
- Large-tree rendering.
- Node interaction.

The tree layout engine should be isolated behind a component interface so the visualization library can be replaced later.

---

# 37. Tree Component Structure

Recommended:

```text
FamilyTreePage
      |
      +-- TreeToolbar
      |
      +-- TreeCanvas
             |
             +-- TreeRenderer
                    |
                    +-- TreeNode
                    |
                    +-- TreeEdge
```

`TreeNode` should not know anything about API calls.

It receives data and emits events:

```typescript
interface TreeNodeProps {
    node: FamilyTreeNode;
    onSelect: (id: string) => void;
    onAddChild: (id: string) => void;
}
```

This keeps the visualization independent from the backend.

---

# 38. Node Interaction

Clicking a node should open a contextual panel/menu:

```text
+------------------------+
| Faris                  |
+------------------------+
| View                   |
| Edit                   |
| Add Child              |
| Move                   |
| Delete                 |
+------------------------+
```

The frontend should determine whether to display actions based on the user's permissions.

However, the backend must independently enforce those permissions.

---

# 39. Search UX

The search box should search only within the current tenant/family tree.

Example:

```text
[ Search family members... ]
```

Results:

```text
Mahmoud
Ahmed Mahmoud
Mahmoud Ali
```

Selecting a result:

1. Closes search results.
2. Expands necessary ancestors.
3. Centers the tree on the selected node.
4. Highlights the node.

This is particularly important when the tree is large.

---

# 40. RTL / Arabic

Since the supplied family tree is Arabic, the frontend should support RTL from the beginning.

Use:

```html
<html dir="rtl">
```

or dynamically:

```text
direction: rtl
```

The tree visualization should be tested carefully with Arabic names.

The underlying hierarchy should remain language-neutral.

---

# 41. Error Handling

Use standardized API errors.

Recommended format:

```json
{
  "type": "https://api.example.com/errors/member-has-children",
  "title": "Member cannot be deleted",
  "status": 409,
  "code": "MEMBER_HAS_CHILDREN",
  "detail": "The member has one or more children."
}
```

ASP.NET Core Problem Details should be used as the standard error envelope.

---

# 42. Validation

Validation should exist on:

### Frontend

For immediate UX.

### Backend

For actual enforcement.

Example:

```text
Name
Required
Minimum: 1
Maximum: 200
```

The backend remains authoritative.

---

# 43. Concurrency

Two administrators may edit the same member.

Example:

```text
Admin A → changes Ahmed → "Ahmed Ali"

Admin B → changes Ahmed → "Ahmed Mohamed"
```

The system should use optimistic concurrency.

Add:

```text
xmin
```

or an application-managed concurrency token.

A practical EF Core approach is a version/concurrency field.

The API should return:

```text
409 Conflict
```

when a stale update is detected.

---

# 44. Observability

The backend should include:

- Structured logging.
- Correlation ID.
- OpenTelemetry.
- Request duration.
- Database query duration.
- Exception tracking.
- Authentication failures.
- Authorization failures.

Important operations such as moving and deleting members should produce structured logs.

---

# 45. Security

The application should implement:

- HTTPS only.
- Secure password hashing.
- JWT validation.
- Refresh token rotation/revocation.
- Rate limiting.
- CORS restrictions.
- Input validation.
- Authorization checks.
- Tenant isolation.
- Secure public tokens.
- Audit logging.
- Protection against IDOR.
- Protection against SQL injection through EF Core/parameterization.
- Security headers.

---

# 46. IDOR Protection

This is especially important for a SaaS application.

This request:

```http
GET /api/v1/family-members/ABC
```

must not return a member simply because the ID exists.

The server must verify:

```text
Member.TenantId == CurrentUser.TenantId
```

The same rule applies to:

- Members.
- Family trees.
- Users.
- Roles.
- Audit records.
- Public links.

---

# 47. Testing Strategy

## Unit Tests

Test:

- Member creation.
- Member movement.
- Cycle detection.
- Delete validation.
- Permission evaluation.
- Generation calculation.

## Integration Tests

Test:

- PostgreSQL database.
- EF Core queries.
- Tenant isolation.
- Authentication.
- Authorization.
- Transactions.

## API Tests

Test:

```text
401 Unauthorized
403 Forbidden
404 Not Found
409 Conflict
400 Bad Request
200 OK
201 Created
204 No Content
```

## Critical Tenant Test

Create:

```text
Tenant A
Member A
```

and:

```text
Tenant B
Member B
```

Authenticate as Tenant A and attempt:

```http
GET /api/v1/family-members/{MemberB}
```

Expected:

```text
404 Not Found
```

or an equivalent non-disclosing response.

Never return Member B.

---

# 48. Database Migration

Use EF Core migrations.

Development:

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

Production:

```text
Migration deployment should be part of CI/CD.
```

Do not allow the application to silently modify production schema on startup.

---

# 49. PostgreSQL Configuration

Use Npgsql with EF Core.

Example:

```csharp
builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"));
});
```

Connection pooling should be enabled and configured appropriately for the deployment environment.

Npgsql supports EF Core integration and connection/data-source configuration.

---

# 50. Caching

Do not introduce Redis in V1 unless performance testing demonstrates a need.

The tree data can initially be served directly from PostgreSQL.

If the application later has:

- Thousands of concurrent viewers.
- Large public trees.
- Heavy repeated reads.

then introduce:

```text
Redis
```

for:

- Public tree caching.
- Search caching.
- Session-related data where appropriate.

---

# 51. Background Jobs

No background job system is required for the initial core functionality.

If future features introduce:

- Email invitations.
- Notifications.
- Scheduled reports.
- Data exports.
- Large PDF generation.

then introduce a background job system.

---

# 52. API Versioning

Use:

```text
/api/v1/...
```

from the beginning.

This avoids having to redesign the URL structure when V2 is introduced.

---

# 53. OpenAPI

The API should expose OpenAPI documentation.

Recommended development URL:

```text
/swagger
```

or equivalent OpenAPI UI.

The API contracts should use DTOs rather than EF Core entities.

---

# 54. Deployment Architecture

Initial production deployment can be:

```text
                 Internet
                    |
                Nginx/Caddy
                    |
          --------------------
          |                  |
        React              API
                             |
                             |
                         PostgreSQL
```

Docker:

```text
docker-compose.yml

services:

  frontend
  api
  postgres
```

For production, PostgreSQL should preferably be managed separately or placed on reliable persistent storage.

---

# 55. Environment Configuration

Use separate configuration for:

```text
Development
Staging
Production
```

Sensitive values must not be committed to Git.

Examples:

```text
Database connection string
JWT signing configuration
Email credentials
Public token secrets
```

Use environment variables or a proper secret manager.

---

# 56. CI/CD

Recommended pipeline:

```text
Git Push
   |
   v
Build
   |
   v
Unit Tests
   |
   v
Integration Tests
   |
   v
Frontend Build
   |
   v
Docker Build
   |
   v
Security Scan
   |
   v
Deploy Staging
   |
   v
Production
```

---

# 57. Development Phases

## Phase 1 — Foundation

- Solution setup.
- .NET 10 API.
- React application.
- PostgreSQL.
- EF Core.
- Authentication.
- Tenant model.
- Database migrations.

## Phase 2 — Family Tree

- Family Tree creation.
- Root family.
- Member creation.
- Member editing.
- Member deletion.
- Parent-child hierarchy.

## Phase 3 — Tree Visualization

- SVG tree.
- Zoom.
- Pan.
- Expand/collapse.
- Search.
- Node actions.

## Phase 4 — Authorization

- Roles.
- Permissions.
- Custom roles.
- User management.

## Phase 5 — Advanced Tree Operations

- Move member.
- Cycle detection.
- Relationship history.
- Audit logs.

## Phase 6 — Public Access

- Public link creation.
- Public tree viewer.
- Link revocation.

## Phase 7 — Production Hardening

- Observability.
- Security.
- Performance.
- Integration testing.
- CI/CD.
- Backup/recovery.

---

# 58. Definition of Done

A feature is not considered complete until:

- Backend implementation is complete.
- Frontend implementation is complete.
- Authorization is implemented.
- Tenant isolation is verified.
- Validation is implemented.
- Unit tests exist for business rules.
- Integration tests exist for database behavior.
- API documentation is updated.
- Error handling is implemented.
- Audit requirements are implemented where applicable.
- The UI works correctly in RTL.
- The feature works against PostgreSQL.
- No business rule is enforced only in the frontend.

---

# 59. Final Recommended Architecture

The final V1 architecture is:

```text
                    FAMILY TREE SaaS
                          │
                    ┌─────┴─────┐
                    │   React   │
                    │ TypeScript│
                    └─────┬─────┘
                          │ HTTPS
                          │ REST / JSON
                    ┌─────▼─────┐
                    │ ASP.NET   │
                    │ Core .NET │
                    │    10     │
                    └─────┬─────┘
                          │
             ┌────────────┼────────────┐
             │            │            │
          Identity     Family       Audit
                       Tree
             │            │            │
             │       Members/RBAC     │
             │            │            │
             └────────────┼────────────┘
                          │
                    ┌─────▼─────┐
                    │ PostgreSQL│
                    └───────────┘
```

The core domain is deliberately simple:

```text
Tenant
  │
  ├── Users
  ├── Roles
  ├── AuditLogs
  │
  └── FamilyTree
         │
         ├── Root Family
         │
         └── FamilyMembers
                │
                ├── Parent
                └── Children
```

This gives us a strong foundation for the current requirement while leaving room for future genealogy features without prematurely turning the application into a complicated system.