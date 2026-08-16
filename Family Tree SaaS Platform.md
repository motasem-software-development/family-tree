# Family Tree SaaS Platform
## Software Requirements Specification — V1

**Document Status:** Baseline Requirements  
**Version:** 1.0  
**Application Type:** Multi-tenant SaaS Web Application

---

# 1. Introduction

The objective of the project is to develop a web-based SaaS application that allows families to create, manage, and visualize their family tree.

The application will represent the family genealogy as a hierarchical tree starting from a single **Root Family Name**. Male family members can then be added as nodes under the root or under another male family member.

The family tree supports an unlimited number of generations and an unlimited number of children per family member.

The application will support multiple customers/families using the same SaaS platform while maintaining strict data isolation between them.

The initial version will represent **male family members only**. Female family members, spouses, and marriage relationships are explicitly outside the scope of V1.

The resulting visualization should be similar in concept to the supplied reference image, with the family name at the top and family members displayed as connected hierarchical nodes.

---

# 2. Product Objectives

The primary objectives are:

1. Allow a customer to create and maintain their family tree.
2. Represent an unlimited number of generations.
3. Provide an intuitive graphical visualization of the family hierarchy.
4. Allow authorized administrators to maintain the tree.
5. Allow administrators to move members between parents.
6. Preserve an audit history of important changes.
7. Allow read-only public sharing of a family tree.
8. Support multiple users and administrators with different permissions.
9. Provide strong data isolation between customers.
10. Establish a SaaS-ready architecture from the first release.

---

# 3. Core Business Model

The SaaS platform will follow this logical structure:

```text
SaaS Platform
     |
     +-- Tenant / Customer
             |
             +-- Family Tree
                     |
                     +-- Root Family
                             |
                             +-- Male Member
                             |      |
                             |      +-- Male Member
                             |      +-- Male Member
                             |
                             +-- Male Member
                                    |
                                    +-- Male Member
```

### Business rule

**One Customer = One Family Tree.**

However, the database and application architecture should still maintain a separate `TenantId` and `FamilyTreeId`.

This provides a clean separation between:

- SaaS customer/account
- Family tree
- Family members

and prevents the family name from becoming a technical identifier.

---

# 4. Multi-Tenant Architecture

The application is a multi-tenant SaaS system.

Each customer owns exactly one family tree.

Example:

```text
Tenant A
   |
   +-- Family Tree A
          |
          +-- Al-Saqqa Family
                 |
                 +-- Members


Tenant B
   |
   +-- Family Tree B
          |
          +-- Al-Hassan Family
                 |
                 +-- Members
```

Tenant A must never be able to access data belonging to Tenant B.

## 4.1 Tenant Isolation

All family-related data must ultimately be associated with a `TenantId`.

For example:

```text
FamilyTree
------------
Id
TenantId
Name
```

and:

```text
FamilyMember
------------
Id
FamilyTreeId
TenantId
ParentId
Name
```

The backend must enforce tenant isolation.

The system must never rely solely on the frontend to prevent unauthorized access.

---

# 5. Family Tree

Each customer has exactly one Family Tree.

A Family Tree has exactly one Root Family Name.

Example:

```text
Al-Saqqa Family
```

The root represents the family and is **not a person**.

The root may have any number of first-generation male members.

Example:

```text
                    Al-Saqqa Family
                           |
          ---------------------------------
          |               |               |
       Suleiman          Omar            Ahmed
```

There is no predefined limit on the number of first-generation members.

---

# 6. Family Member Model

V1 represents male family members only.

Each family member is a male node in the hierarchy.

A member can have:

- Zero children.
- One child.
- Multiple children.

There is no maximum number of children.

Example:

```text
Suleiman
   |
   +-- Faris
   |
   +-- Youssef
   |
   +-- Mahmoud
   |
   +-- Ahmed
```

A member does not need to have children to be added to the tree.

---

# 7. Family Member Information

The system should be designed to support additional personal information in the future.

However, in V1:

### Required

- Name

### Future / Optional

The architecture should allow future fields such as:

- Full name
- Nickname
- Date of birth
- Date of death
- Photograph
- Place of birth
- Occupation
- Biography
- Notes

Only `Name` is required for V1.

---

# 8. Root Family Rules

The following rules apply to the root:

### BR-001
Each Family Tree must have exactly one Root Family.

### BR-002
The Root Family represents the family name.

### BR-003
The Root Family is not a person.

### BR-004
A Root Family can have multiple first-generation male members.

### BR-005
A Family Tree cannot have more than one Root Family.

### BR-006
The Root Family belongs to exactly one Tenant.

Example:

```text
Al-Saqqa Family
      |
      +-- Suleiman
      +-- Omar
      +-- Ahmed
```

---

# 9. Adding Family Members

Authorized users can add a new member.

There are two scenarios.

## 9.1 Add First-Generation Member

The administrator selects the Root Family and chooses:

**Add Member**

The new member becomes a direct child of the Root Family.

Example:

```text
Al-Saqqa Family
      |
      +-- Suleiman
```

The administrator can repeat this operation any number of times.

---

## 9.2 Add Descendant

The administrator selects an existing member and chooses:

**Add Child**

Example:

```text
Suleiman
   |
   +-- Faris
```

The system creates Faris and assigns:

```text
ParentId = Suleiman.Id
```

---

# 10. Unlimited Generations

The system must support unlimited generations.

Example:

```text
Al-Saqqa Family
      |
Generation 1
      |
   Suleiman
      |
Generation 2
      |
    Faris
      |
Generation 3
      |
   Mahmoud
      |
Generation 4
      |
    Ahmed
      |
Generation 5
      |
   Youssef
      |
     ...
```

No application-level maximum generation number should be implemented.

The hierarchy should be represented using a recursive relationship rather than hard-coded generation columns.

---

# 11. Moving Family Members

Authorized administrators can move an existing member from one parent to another.

Example:

### Before

```text
Ahmed
  |
  +-- Mohamed
```

### After

```text
Ali
  |
  +-- Mohamed
```

The system must update the member's parent relationship.

## 11.1 Validation

The system must prevent:

- Moving a member under themselves.
- Moving a member under one of their descendants.
- Creating circular relationships.
- Moving a member to another Tenant.
- Moving a member to another Family Tree.

Because each customer owns exactly one tree, moving between Family Trees should not normally be available as an operation.

---

# 12. Relationship History

Whenever a member is moved, the system must preserve the previous relationship in the audit/history system.

Example:

```text
Member: Mohamed

Previous Parent:
Ahmed

New Parent:
Ali

Changed By:
Administrator

Changed At:
2026-08-16 14:30
```

This history should not be lost when the current `ParentId` is changed.

The purpose is to maintain the historical integrity of the family tree and provide accountability for administrative changes.

---

# 13. Deleting Family Members

A family member who has children **cannot be deleted**.

Example:

```text
Ahmed
  |
  +-- Mohamed
```

Ahmed cannot be deleted because he has a descendant.

The system should display a clear message:

> This member cannot be deleted because they have children.

A member with no children can be deleted if the current user has the required permission.

---

# 14. Editing Members

Authorized users can edit the member's information.

In V1, the only editable business field is:

- Name

Editing a member must not affect:

- Member ID
- Tenant
- Family Tree
- Parent relationship
- Children

Example:

```text
Before:
Mohamed

After:
Mohamed Ahmed
```

The relationship remains unchanged.

---

# 15. Family Tree Visualization

The application shall provide an interactive graphical visualization.

The visualization should be conceptually similar to the supplied reference image.

Example:

```text
                         Al-Saqqa Family
                                |
          -----------------------------------------
          |                   |                   |
       Suleiman              Omar                Ahmed
          |
      -----------
      |         |
    Faris     Youssef
                |
          -------------
          |           |
       Mahmoud       Ali
```

Each person should be represented by a visual node.

Parent-child relationships should be represented by connecting lines.

---

# 16. Visualization Features

The tree viewer should support:

### Zoom

Users can zoom in and out.

### Pan

Users can move around large trees.

### Expand

Users can expand a collapsed branch.

### Collapse

Users can collapse a branch to reduce visual complexity.

### Search

Users can search for a specific family member.

### Focus

Selecting a search result should navigate the visualization to the corresponding member.

---

# 17. Large Tree Handling

Family trees can become very large.

The system should therefore avoid assuming that the entire tree will always fit on a screen.

The UI should support:

- Expand/collapse.
- Zoom.
- Pan.
- Search.
- Focus on selected node.
- Efficient loading/rendering.

For very large trees, lazy loading or partial tree loading may be introduced.

---

# 18. User Management

The system shall support multiple users.

A customer may have:

- Multiple administrators.
- Multiple editors.
- Multiple viewers.

Example:

```text
Al-Saqqa Family

Users
 |
 +-- Ahmed       Super Admin
 +-- Mohamed     Administrator
 +-- Ali         Editor
 +-- Omar        Viewer
```

Users belong to a Tenant.

Because one Tenant owns one Family Tree, users associated with that Tenant automatically operate within that family's context.

---

# 19. Role-Based Access Control

The system shall support Role-Based Access Control (RBAC).

The initial system may provide predefined roles such as:

- Super Admin
- Administrator
- Editor
- Viewer

However, the architecture should support **custom roles**.

---

# 20. Custom Roles

Administrators with the appropriate permission can create custom roles.

For example:

```text
Role:
Family Data Editor

Permissions:
✓ View Tree
✓ Search
✓ Add Member
✓ Edit Member
✓ Move Member
✗ Delete Member
✗ Manage Users
✗ Manage Roles
```

Another role could be:

```text
Role:
Family Viewer

Permissions:
✓ View Tree
✓ Search
✗ Add Member
✗ Edit Member
✗ Move Member
✗ Delete Member
```

Permissions should be represented as individual capabilities rather than hard-coded role checks.

---

# 21. Recommended Permissions

The initial permission catalog should include:

```text
FamilyTree.View
FamilyTree.Edit

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

This allows the authorization system to grow without redesigning the security model.

---

# 22. Public Read-Only Access

An administrator can generate a public read-only link for the family tree.

Example:

```text
https://app.example.com/public/family/{public-id}
```

The exact URL structure is implementation-specific.

The public link allows visitors to:

- View the family tree.
- Zoom.
- Pan.
- Expand/collapse branches.
- Search, if enabled.

The public user cannot:

- Add members.
- Edit members.
- Move members.
- Delete members.
- Manage users.
- Manage permissions.
- Access administration functions.

---

# 23. Public Link Security

The public link should not expose internal identifiers unnecessarily.

For example, instead of:

```text
/family/123
```

the application should use a dedicated public identifier/token.

Example:

```text
/public/family/a8Kx92Pm...
```

The administrator should be able to:

- Generate the public link.
- Disable/revoke the public link.
- Generate a new link if necessary.

Once revoked, the old link must no longer provide access.

---

# 24. Authentication

Administrative functionality requires authentication.

The system should support:

- Login.
- Logout.
- Password management.
- Account activation/deactivation.
- Authorization.

Public read-only visitors do not require an account when accessing an enabled public link.

---

# 25. Audit Trail

The system shall maintain an audit trail for important operations.

Examples:

### Member Created

```text
User: Ahmed
Action: Create Member
Member: Mohamed
Date: 2026-08-16
```

### Member Updated

```text
User: Ahmed
Action: Update Member
Member: Mohamed
Old Name: Mohamed
New Name: Mohamed Ahmed
Date: 2026-08-16
```

### Member Moved

```text
User: Ahmed
Action: Move Member
Member: Mohamed

Previous Parent: Ali
New Parent: Mahmoud

Date: 2026-08-16
```

### Member Deleted

```text
User: Ahmed
Action: Delete Member
Member: Mohamed
Date: 2026-08-16
```

---

# 26. Audit Requirements

Audit records should contain at minimum:

| Field | Description |
|---|---|
| Id | Audit record ID |
| TenantId | Customer |
| UserId | User who performed the action |
| Action | Operation performed |
| EntityType | Type of entity |
| EntityId | Entity affected |
| OldValue | Previous state where applicable |
| NewValue | New state where applicable |
| CreatedAt | Timestamp |

Audit records should normally be immutable.

---

# 27. Proposed Data Model

The recommended initial database model is:

```text
Tenant
  |
  | 1:1
  |
FamilyTree
  |
  | 1:N
  |
FamilyMember
  |
  | ParentId
  |
  +------> FamilyMember
```

Users and permissions:

```text
Tenant
  |
  +-- Users
  |
  +-- Roles
  |
  +-- UserRoles
```

Audit:

```text
Tenant
  |
  +-- AuditLogs
```

Public sharing:

```text
FamilyTree
  |
  +-- PublicAccessLinks
```

---

# 28. Tenant Entity

Suggested fields:

| Field | Description |
|---|---|
| Id | Unique Tenant ID |
| Name | Customer/account name |
| CreatedAt | Creation date |
| IsActive | Tenant status |

A Tenant represents the SaaS customer.

---

# 29. FamilyTree Entity

Suggested fields:

| Field | Description |
|---|---|
| Id | Unique Family Tree ID |
| TenantId | Owning tenant |
| Name | Root family name |
| CreatedAt | Creation date |
| UpdatedAt | Last modification |
| IsActive | Tree status |

Constraint:

```text
UNIQUE(TenantId)
```

because V1 follows:

> One Customer = One Family Tree.

This constraint can be removed in a future version if customers are allowed to own multiple trees.

---

# 30. FamilyMember Entity

Suggested fields:

| Field | Description |
|---|---|
| Id | Unique member ID |
| TenantId | Owning tenant |
| FamilyTreeId | Owning tree |
| ParentId | Parent member ID |
| Name | Member name |
| CreatedAt | Creation timestamp |
| UpdatedAt | Modification timestamp |

`ParentId` is a self-referencing relationship.

Conceptually:

```text
FamilyMember
     |
     +-- ParentId --> FamilyMember.Id
```

---

# 31. Important Database Constraint

The database should enforce that the parent and child belong to the same Family Tree/Tenant.

The application should also validate this at the service layer.

This prevents invalid relationships such as:

```text
Tenant A
  |
  +-- Ahmed
       |
       +-- Parent from Tenant B   ❌
```

---

# 32. Generation Calculation

The system should **not store Generation as a permanent field** in V1.

For example:

```text
Root
 |
Ahmed       -> Generation 1
 |
Mohamed     -> Generation 2
 |
Ali         -> Generation 3
```

Generation can be calculated from the hierarchy.

This is important because members can be moved.

If:

```text
Ali
```

is moved to another parent, his generation may change.

Therefore storing:

```text
Generation = 3
```

could become incorrect.

---

# 33. API Requirements

The backend should expose APIs for the major operations.

Example API structure:

```text
/api/auth
/api/tenants
/api/family-tree
/api/family-members
/api/users
/api/roles
/api/permissions
/api/audit
/api/public
```

Example operations:

### Family Tree

```text
GET    /api/family-tree
PUT    /api/family-tree
```

### Members

```text
GET    /api/family-members
GET    /api/family-members/{id}

POST   /api/family-members
PUT    /api/family-members/{id}

POST   /api/family-members/{id}/move
DELETE /api/family-members/{id}
```

### Public Access

```text
POST   /api/public-links
GET    /api/public/{token}
DELETE /api/public-links/{id}
```

Exact API naming and structure will be finalized during the technical design phase.

---

# 34. Authorization Enforcement

Authorization must be enforced on the server.

For example:

```text
POST /api/family-members
```

must verify:

1. User is authenticated.
2. User belongs to the Tenant.
3. User has `Member.Create`.
4. Requested parent belongs to the same Family Tree.
5. The operation is valid.

Similarly:

```text
DELETE /api/family-members/{id}
```

must verify:

1. User is authenticated.
2. User belongs to the Tenant.
3. User has `Member.Delete`.
4. Member belongs to the Tenant.
5. Member has no children.
6. The deletion is valid.

---

# 35. Business Rules

| ID | Business Rule |
|---|---|
| BR-001 | One customer owns exactly one Family Tree in V1. |
| BR-002 | Every Family Tree has exactly one Root Family. |
| BR-003 | The Root Family is not a person. |
| BR-004 | The Root Family can have unlimited first-generation male members. |
| BR-005 | Only male family members are represented in V1. |
| BR-006 | A member can have zero or unlimited children. |
| BR-007 | Unlimited generations are supported. |
| BR-008 | Name is the only required member field in V1. |
| BR-009 | A member with children cannot be deleted. |
| BR-010 | A member without children can be deleted if authorized. |
| BR-011 | Authorized users can move members between parents. |
| BR-012 | A member cannot become their own parent. |
| BR-013 | A member cannot be moved under a descendant. |
| BR-014 | Circular relationships are prohibited. |
| BR-015 | Every family-related entity belongs to one Tenant. |
| BR-016 | Every member belongs to one Family Tree. |
| BR-017 | Users can have different roles and permissions. |
| BR-018 | Custom roles are supported. |
| BR-019 | Public read-only links can be generated by authorized administrators. |
| BR-020 | Public access provides read-only access only. |
| BR-021 | Public links can be revoked. |
| BR-022 | Member relationship changes must be audited. |
| BR-023 | Important administrative operations must be audited. |
| BR-024 | Tenant isolation must be enforced on the backend. |
| BR-025 | Family name is a business/display value and not the technical isolation identifier. |

---

# 36. V1 Scope

## Included

### SaaS

- Multi-tenant architecture.
- One customer → one family tree.
- Tenant isolation.

### Family Tree

- One root family.
- Root family name.
- Unlimited first-generation members.
- Unlimited generations.
- Unlimited children.

### Members

- Create.
- Edit.
- Move.
- Delete when no children exist.
- Name field.

### Visualization

- Tree visualization.
- Parent-child relationships.
- Zoom.
- Pan.
- Expand/collapse.
- Search.

### Users

- Multiple users.
- Multiple administrators.
- Viewers.
- Role-based permissions.
- Custom roles.

### Public Access

- Generate public link.
- Read-only access.
- Revoke public link.

### Audit

- Member creation.
- Member editing.
- Member movement.
- Member deletion.
- Administrative changes.

---

# 37. Explicitly Out of Scope for V1

The following are not part of the first version:

- Female family members.
- Wives.
- Daughters.
- Marriage relationships.
- Spouse relationships.
- Multiple relationship types.
- Photos.
- Date of birth.
- Date of death.
- Biography.
- Documents.
- Family events.
- Genealogy standards/import formats.
- Multiple trees per customer.
- Native mobile application.

The data model should nevertheless avoid making future expansion impossible.

---

# 38. Future Expansion

The architecture should leave room for future capabilities such as:

```text
                 Family Tree
                      |
        -----------------------------
        |             |             |
      Males        Females       Spouses
        |             |             |
        -------- Relationships -------
                      |
                  Events
                      |
             Birth / Death / Marriage
```

However, these relationships should **not be implemented in V1 unless explicitly added to scope**.

---

# 39. Recommended V1 User Experience

The administrator's main screen should be centered around the family tree.

A typical flow:

```text
Login
  |
  v
Dashboard
  |
  v
Family Tree
  |
  +-- View Tree
  |
  +-- Add Member
  |
  +-- Search
  |
  +-- Manage Users
  |
  +-- Manage Roles
  |
  +-- Audit History
  |
  +-- Public Link
```

Selecting a node should provide contextual actions:

```text
+----------------------+
| Ahmed                |
+----------------------+
| View                 |
| Edit                 |
| Add Child            |
| Move                 |
| Delete               |
+----------------------+
```

The actions displayed should depend on the current user's permissions.

---

# 40. Success Criteria

The V1 implementation will be considered functionally complete when:

1. A customer can create their family tree.
2. The tree contains exactly one root family.
3. Administrators can add unlimited first-generation members.
4. Administrators can add unlimited descendants.
5. The system supports unlimited generations.
6. Members can be edited.
7. Members can be moved.
8. Members with children cannot be deleted.
9. Invalid/circular relationships are prevented.
10. Multiple users can access the same family tree.
11. Different users can have different permissions.
12. Custom roles can be created.
13. Users cannot access another customer's data.
14. The family tree can be displayed interactively.
15. The tree can be searched, zoomed, panned, expanded, and collapsed.
16. A public read-only link can be generated and revoked.
17. Important changes are recorded in the audit history.
18. The architecture is ready for future SaaS expansion.