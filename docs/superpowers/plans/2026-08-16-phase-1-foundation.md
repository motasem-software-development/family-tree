# Family Tree SaaS — Phase 1 (Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A running, containerized Family Tree SaaS skeleton in which a seeded user logs in, receives a JWT and refresh token, and calls a permission-protected endpoint whose tenant is resolved server-side — with cross-tenant isolation proven by an integration test against real PostgreSQL.

**Architecture:** Modular monolith. Five backend projects with inward-only dependencies (`Domain` ← `Application` ← `Infrastructure` ← `Api`, plus a `Contracts` DTO project). Tenant identity is resolved once per request from JWT claims into an injected `ITenantContext`, which drives EF Core global query filters so tenant scoping cannot be forgotten at a call site. The React client is a Vite SPA with bilingual AR/EN i18n and direction handling wired in from the first commit.

**Tech Stack:** .NET 10 / C# 14, ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, ASP.NET Core Identity, JWT bearer, Serilog, xUnit + Testcontainers + FluentAssertions; React 19 + TypeScript + Vite, React Router, TanStack Query, react-i18next, Vitest + Testing Library; PostgreSQL 17 in Docker.

**Spec:** `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md`

## Global Constraints

- Target framework `net10.0`; `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>` for every project.
- `FamilyTree.Domain` references **no** NuGet package outside the BCL. No EF Core, no ASP.NET Core. Enforced by a test in Task 2.
- Dependencies point inward only: `Domain` → nothing; `Application` → `Domain`, `Contracts`; `Infrastructure` → `Application`, `Domain`; `Api` → all.
- Folder organization is **feature-first inside every layer** (`Application/Tenants/`, `Api/Endpoints/Auth/`), never layer-first.
- All database identifiers are `snake_case`, produced by `EFCore.NamingConventions`, not by hand-written `ToTable`/`HasColumnName` calls.
- Every table owned by a tenant carries `tenant_id` and gets an EF global query filter. No exceptions.
- No application service method accepts a `tenantId` parameter. Tenant comes from injected `ITenantContext`.
- Cross-tenant access returns **404, never 403**.
- All API errors are RFC 7807 Problem Details carrying a stable machine-readable `code`. Human-readable message text is never part of the contract — the client translates from `code`.
- Integration tests run against **real PostgreSQL via Testcontainers**. The EF in-memory provider is never used.
- Files stay under 400 lines. Functions stay under 50.
- Secrets never enter source control. Local development uses `.env` (git-ignored) and `dotnet user-secrets`.
- Every task ends with a commit using Conventional Commits (`feat:`, `test:`, `chore:`, `docs:`, `fix:`).

## Deviation from the spec, and why

The technical specification §49 recommends `AddDbContextPool`. **This plan uses `AddDbContext` instead.** A pooled context reuses instances across requests, so a context holding per-request tenant state risks leaking one tenant's scope into another tenant's request — precisely the failure the isolation design exists to prevent. Pooling is a throughput optimization worth revisiting under measurement in Phase 7; it is not worth a tenant-isolation hazard in Phase 1.

## File structure

```
Directory.Build.props              shared MSBuild properties for all projects
FamilyTree.sln
docker-compose.yml                 postgres (api/frontend services added in Task 12)
.env.example                       documented, committed; .env is git-ignored

src/FamilyTree.Domain/
  Common/Entity.cs                 base: Id, CreatedAt, UpdatedAt
  Common/ITenantOwned.cs           marker: Guid TenantId { get; }
  Common/DomainException.cs        base for rule violations, carries a stable Code
  Tenants/Tenant.cs
  FamilyTrees/FamilyTree.cs        declares FamilyTreeAggregate — see Task 3
  Authorization/Permissions.cs     static catalog of permission code constants
  Authorization/Permission.cs  Role.cs  RolePermission.cs  UserRole.cs
  Authentication/RefreshToken.cs

src/FamilyTree.Contracts/
  Auth/LoginRequest.cs  LoginResponse.cs  RefreshRequest.cs  CurrentUserResponse.cs

src/FamilyTree.Application/
  Common/ITenantContext.cs
  Auth/ITokenService.cs  AuthService.cs
  Authorization/IPermissionResolver.cs

src/FamilyTree.Infrastructure/
  Persistence/ApplicationDbContext.cs
  Persistence/Configurations/*.cs  one file per entity
  Persistence/Seed/DatabaseSeeder.cs
  Identity/ApplicationUser.cs      the IdentityUser<Guid> subclass
  Auth/JwtTokenService.cs  JwtOptions.cs
  Authorization/PermissionResolver.cs
  DependencyInjection.cs

src/FamilyTree.Api/
  Program.cs
  Middleware/TenantContextMiddleware.cs  HttpTenantContext.cs
  Authorization/PermissionRequirement.cs  PermissionAuthorizationHandler.cs
  Authorization/EndpointExtensions.cs   .RequirePermission(...)
  Endpoints/Auth/AuthEndpoints.cs
  Endpoints/Me/MeEndpoints.cs
  Errors/ExceptionHandler.cs

tests/FamilyTree.Domain.Tests/
tests/FamilyTree.Application.Tests/
tests/FamilyTree.Api.IntegrationTests/
  Fixtures/PostgresFixture.cs      Testcontainers lifetime
  Fixtures/ApiFactory.cs           WebApplicationFactory wired to the container

frontend/
  src/i18n/index.ts  src/i18n/locales/ar.json  src/i18n/locales/en.json
  src/app/App.tsx  src/app/providers.tsx
  src/features/auth/               login page, token storage, auth context
  src/services/apiClient.ts        fetch wrapper + refresh interceptor
  src/routes/                      route table, ProtectedRoute
```

**Deferred to Phase 2 on purpose:** `family_members` and everything touching it — the composite `(id, family_tree_id)` foreign key, the `pg_trgm` extension and name index, and cycle detection. The `family_trees` table lands here because the seeded tenant needs a tree row, but tree *management* endpoints are Phase 2.

---

### Task 1: Repository scaffolding and toolchain

**Files:**
- Create: `Directory.Build.props`, `FamilyTree.sln`, `.env.example`, `docker-compose.yml`
- Create: `src/FamilyTree.{Domain,Contracts,Application,Infrastructure,Api}/*.csproj`
- Create: `tests/FamilyTree.{Domain,Application}.Tests/*.csproj`, `tests/FamilyTree.Api.IntegrationTests/*.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: the solution graph every later task builds into, and `docker compose up -d postgres` as the way to get a database on `localhost:5432`.

- [ ] **Step 1: Create the shared MSBuild properties**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the solution and all projects**

```bash
dotnet new sln -n FamilyTree

dotnet new classlib -o src/FamilyTree.Domain
dotnet new classlib -o src/FamilyTree.Contracts
dotnet new classlib -o src/FamilyTree.Application
dotnet new classlib -o src/FamilyTree.Infrastructure
dotnet new web      -o src/FamilyTree.Api

dotnet new xunit -o tests/FamilyTree.Domain.Tests
dotnet new xunit -o tests/FamilyTree.Application.Tests
dotnet new xunit -o tests/FamilyTree.Api.IntegrationTests

dotnet sln add $(find src tests -name "*.csproj")
```

Delete the `Class1.cs` that `dotnet new classlib` generates in each class library.

- [ ] **Step 3: Wire project references so dependencies point inward**

```bash
dotnet add src/FamilyTree.Application    reference src/FamilyTree.Domain src/FamilyTree.Contracts
dotnet add src/FamilyTree.Infrastructure reference src/FamilyTree.Application src/FamilyTree.Domain
dotnet add src/FamilyTree.Api            reference src/FamilyTree.Infrastructure src/FamilyTree.Application src/FamilyTree.Contracts src/FamilyTree.Domain

dotnet add tests/FamilyTree.Domain.Tests         reference src/FamilyTree.Domain
dotnet add tests/FamilyTree.Application.Tests    reference src/FamilyTree.Application
dotnet add tests/FamilyTree.Api.IntegrationTests reference src/FamilyTree.Api
```

`FamilyTree.Domain` gets **no** reference added — that is the point.

- [ ] **Step 4: Add the shared test package**

```bash
for p in tests/FamilyTree.Domain.Tests tests/FamilyTree.Application.Tests tests/FamilyTree.Api.IntegrationTests; do
  dotnet add $p package FluentAssertions
done
```

- [ ] **Step 5: Create the local database service and environment template**

Create `docker-compose.yml`:

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: familytree
      POSTGRES_USER: familytree
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-devpassword}
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U familytree -d familytree"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  pgdata:
```

Create `.env.example` — committed, because it documents the shape and holds no real values:

```bash
POSTGRES_PASSWORD=change-me-locally
ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=familytree;Username=familytree;Password=change-me-locally
Jwt__Issuer=https://localhost:5001
Jwt__Audience=familytree-api
Jwt__SigningKey=generate-a-32-byte-random-value-do-not-commit-a-real-one
```

- [ ] **Step 6: Verify the solution builds and the database starts**

```bash
dotnet build
docker compose up -d postgres
docker compose ps
```

Expected: build succeeds with zero warnings; `postgres` reports `healthy` within ~15 seconds.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution, project graph, and local postgres"
```

---

### Task 2: Domain — base types, Tenant, and the dependency guard

**Files:**
- Create: `src/FamilyTree.Domain/Common/Entity.cs`, `Common/ITenantOwned.cs`, `Common/DomainException.cs`
- Create: `src/FamilyTree.Domain/Tenants/Tenant.cs`
- Test: `tests/FamilyTree.Domain.Tests/Common/DomainDependencyTests.cs`, `tests/FamilyTree.Domain.Tests/Tenants/TenantTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `abstract class Entity` — `Guid Id { get; protected set; }`, `DateTimeOffset CreatedAt { get; protected set; }`, `DateTimeOffset UpdatedAt { get; protected set; }`, `protected void InitializeTimestamps(DateTimeOffset)`, `protected void Touch(DateTimeOffset)`.
  - `interface ITenantOwned { Guid TenantId { get; } }`
  - `class DomainException(string code, string message) : Exception` — `string Code { get; }`.
  - `Tenant.Create(string name, string slug, DateTimeOffset now) -> Tenant`; members `Name`, `Slug`, `IsActive`, `Rename`, `Deactivate`, `Activate`.

- [ ] **Step 1: Write the failing dependency-guard test**

Create `tests/FamilyTree.Domain.Tests/Common/DomainDependencyTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Tests.Common;

public class DomainDependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "Microsoft.Extensions.DependencyInjection"
    ];

    [Fact]
    public void Domain_assembly_references_no_infrastructure_packages()
    {
        var referenced = typeof(Entity).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        referenced.Should().NotContain(
            name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)),
            "the domain layer must stay free of infrastructure concerns");
    }
}
```

- [ ] **Step 2: Write the failing Tenant tests**

Create `tests/FamilyTree.Domain.Tests/Tenants/TenantTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.Tenants;

namespace FamilyTree.Domain.Tests.Tenants;

public class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_assigns_identity_timestamps_and_active_state()
    {
        var tenant = Tenant.Create("Al-Saqqa Family", "al-saqqa", Now);

        tenant.Id.Should().NotBeEmpty();
        tenant.Name.Should().Be("Al-Saqqa Family");
        tenant.Slug.Should().Be("al-saqqa");
        tenant.IsActive.Should().BeTrue();
        tenant.CreatedAt.Should().Be(Now);
        tenant.UpdatedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name)
    {
        var act = () => Tenant.Create(name, "al-saqqa", Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("TENANT_NAME_REQUIRED");
    }

    [Fact]
    public void Create_rejects_name_longer_than_200_characters()
    {
        var act = () => Tenant.Create(new string('x', 201), "al-saqqa", Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("TENANT_NAME_TOO_LONG");
    }

    [Theory]
    [InlineData("Al Saqqa")]
    [InlineData("al_saqqa")]
    [InlineData("-al-saqqa")]
    [InlineData("AL-SAQQA")]
    public void Create_rejects_slug_that_is_not_lowercase_kebab_case(string slug)
    {
        var act = () => Tenant.Create("Al-Saqqa Family", slug, Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("TENANT_SLUG_INVALID");
    }

    [Fact]
    public void Rename_changes_name_and_advances_updated_at()
    {
        var tenant = Tenant.Create("Old", "old", Now);
        var later = Now.AddDays(1);

        tenant.Rename("New", later);

        tenant.Name.Should().Be("New");
        tenant.UpdatedAt.Should().Be(later);
        tenant.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Deactivate_then_activate_round_trips_is_active()
    {
        var tenant = Tenant.Create("Al-Saqqa Family", "al-saqqa", Now);

        tenant.Deactivate(Now.AddHours(1));
        tenant.IsActive.Should().BeFalse();

        tenant.Activate(Now.AddHours(2));
        tenant.IsActive.Should().BeTrue();
        tenant.UpdatedAt.Should().Be(Now.AddHours(2));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Domain.Tests`
Expected: compilation failure — `Entity`, `DomainException`, and `Tenant` do not exist.

- [ ] **Step 4: Write the base types**

Create `src/FamilyTree.Domain/Common/Entity.cs`:

```csharp
namespace FamilyTree.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset UpdatedAt { get; protected set; }

    protected void InitializeTimestamps(DateTimeOffset now)
    {
        CreatedAt = now;
        UpdatedAt = now;
    }

    protected void Touch(DateTimeOffset now) => UpdatedAt = now;
}
```

`Guid.CreateVersion7()` produces time-ordered UUIDs, keeping B-tree index inserts sequential rather than scattered — this matters once `family_members` grows.

Create `src/FamilyTree.Domain/Common/ITenantOwned.cs`:

```csharp
namespace FamilyTree.Domain.Common;

/// <summary>Marks an entity scoped to a single tenant, which must carry a global query filter.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
```

Create `src/FamilyTree.Domain/Common/DomainException.cs`:

```csharp
namespace FamilyTree.Domain.Common;

public class DomainException(string code, string message) : Exception(message)
{
    /// <summary>Stable machine-readable code. Surfaces in Problem Details; clients translate from it.</summary>
    public string Code { get; } = code;
}
```

- [ ] **Step 5: Write the Tenant entity**

Create `src/FamilyTree.Domain/Tenants/Tenant.cs`:

```csharp
using System.Text.RegularExpressions;
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Tenants;

public sealed partial class Tenant : Entity
{
    public const int MaxNameLength = 200;
    public const int MaxSlugLength = 100;

    private Tenant() { }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static Tenant Create(string name, string slug, DateTimeOffset now)
    {
        var tenant = new Tenant { Slug = ValidateSlug(slug), IsActive = true };
        tenant.Name = ValidateName(name);
        tenant.InitializeTimestamps(now);
        return tenant;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = ValidateName(name);
        Touch(now);
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        Touch(now);
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        Touch(now);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("TENANT_NAME_REQUIRED", "Tenant name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException("TENANT_NAME_TOO_LONG", $"Tenant name exceeds {MaxNameLength} characters.");
        return trimmed;
    }

    private static string ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > MaxSlugLength || !SlugPattern().IsMatch(slug))
            throw new DomainException("TENANT_SLUG_INVALID", "Tenant slug must be lowercase kebab-case.");
        return slug;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Domain.Tests`
Expected: PASS — 9 tests, including the dependency guard.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Domain tests/FamilyTree.Domain.Tests
git commit -m "feat: add domain base types and Tenant entity"
```

---

### Task 3: Domain — FamilyTree aggregate

**Files:**
- Create: `src/FamilyTree.Domain/FamilyTrees/FamilyTree.cs`
- Test: `tests/FamilyTree.Domain.Tests/FamilyTrees/FamilyTreeTests.cs`

**Interfaces:**
- Consumes: `Entity`, `ITenantOwned`, `DomainException` from Task 2.
- Produces: `FamilyTreeAggregate.Create(Guid tenantId, string name, DateTimeOffset now)`; members `TenantId`, `Name`, `IsActive`, `Rename(string, DateTimeOffset)`.

**Naming note:** a class named `FamilyTree` inside namespace `FamilyTree.Domain.FamilyTrees` collides with the root namespace at every unqualified reference. Declare it `FamilyTreeAggregate` and map it to the `family_trees` table in Task 5. One clear type name beats `global::` aliases scattered across every consumer.

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Domain.Tests/FamilyTrees/FamilyTreeTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;

namespace FamilyTree.Domain.Tests.FamilyTrees;

public class FamilyTreeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Fact]
    public void Create_binds_the_tree_to_its_tenant_and_activates_it()
    {
        var tree = FamilyTreeAggregate.Create(TenantId, "عائلة السقا", Now);

        tree.Id.Should().NotBeEmpty();
        tree.TenantId.Should().Be(TenantId);
        tree.Name.Should().Be("عائلة السقا");
        tree.IsActive.Should().BeTrue();
        tree.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_id()
    {
        var act = () => FamilyTreeAggregate.Create(Guid.Empty, "Al-Saqqa Family", Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("FAMILY_TREE_TENANT_REQUIRED");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => FamilyTreeAggregate.Create(TenantId, name, Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("FAMILY_TREE_NAME_REQUIRED");
    }

    [Fact]
    public void Create_rejects_a_name_longer_than_200_characters()
    {
        var act = () => FamilyTreeAggregate.Create(TenantId, new string('x', 201), Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("FAMILY_TREE_NAME_TOO_LONG");
    }

    [Fact]
    public void Rename_changes_the_root_family_name_without_changing_the_tenant()
    {
        var tree = FamilyTreeAggregate.Create(TenantId, "Old Family", Now);
        var later = Now.AddDays(1);

        tree.Rename("عائلة السقا", later);

        tree.Name.Should().Be("عائلة السقا");
        tree.TenantId.Should().Be(TenantId);
        tree.UpdatedAt.Should().Be(later);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Domain.Tests --filter FullyQualifiedName~FamilyTreeTests`
Expected: compilation failure — `FamilyTreeAggregate` does not exist.

- [ ] **Step 3: Write the entity**

Create `src/FamilyTree.Domain/FamilyTrees/FamilyTree.cs`:

```csharp
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.FamilyTrees;

/// <summary>
/// The root family. Named <c>FamilyTreeAggregate</c> rather than <c>FamilyTree</c> to avoid
/// colliding with the root namespace. Mapped to the <c>family_trees</c> table.
/// Per BR-003 the root family is not a person and is never a FamilyMember.
/// </summary>
public sealed class FamilyTreeAggregate : Entity, ITenantOwned
{
    public const int MaxNameLength = 200;

    private FamilyTreeAggregate() { }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static FamilyTreeAggregate Create(Guid tenantId, string name, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("FAMILY_TREE_TENANT_REQUIRED", "A family tree must belong to a tenant.");

        var tree = new FamilyTreeAggregate { TenantId = tenantId, IsActive = true };
        tree.Name = ValidateName(name);
        tree.InitializeTimestamps(now);
        return tree;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = ValidateName(name);
        Touch(now);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("FAMILY_TREE_NAME_REQUIRED", "Family tree name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException("FAMILY_TREE_NAME_TOO_LONG", $"Family tree name exceeds {MaxNameLength} characters.");
        return trimmed;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Domain.Tests`
Expected: PASS — 14 tests total.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyTree.Domain tests/FamilyTree.Domain.Tests
git commit -m "feat: add FamilyTree aggregate"
```

---

### Task 4: Domain — permission catalog and authorization entities

**Files:**
- Create: `src/FamilyTree.Domain/Authorization/Permissions.cs`, `Permission.cs`, `Role.cs`, `RolePermission.cs`, `UserRole.cs`
- Create: `src/FamilyTree.Domain/Authentication/RefreshToken.cs`
- Test: `tests/FamilyTree.Domain.Tests/Authorization/PermissionsCatalogTests.cs`, `Authorization/RoleTests.cs`, `Authentication/RefreshTokenTests.cs`

**Interfaces:**
- Consumes: `Entity`, `ITenantOwned`, `DomainException` from Task 2.
- Produces:
  - `static class Permissions` — nested classes `FamilyTree`, `Member`, `User`, `Role`, `Audit`, `PublicLink` each holding `const string` codes; `static IReadOnlyList<string> All { get; }`.
  - `Permission` — `Code`, `Description`. System-level, **not** tenant-owned.
  - `Role.Create(Guid tenantId, string name, string? description, DateTimeOffset now)`, `Role.CreateSystem(...)`; members `TenantId`, `Name`, `Description`, `IsSystem`, `Rename`, `EnsureDeletable()`.
  - `RolePermission(Guid RoleId, Guid PermissionId)`, `UserRole(Guid UserId, Guid RoleId)` — join entities.
  - `RefreshToken.Issue(Guid userId, Guid tenantId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)`; members `TokenHash`, `ExpiresAt`, `RevokedAt`, `ReplacedByTokenHash`, `IsActive(DateTimeOffset)`, `Revoke(DateTimeOffset, string? replacedBy)`.

- [ ] **Step 1: Write the failing catalog test**

Create `tests/FamilyTree.Domain.Tests/Authorization/PermissionsCatalogTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Domain.Tests.Authorization;

public class PermissionsCatalogTests
{
    [Fact]
    public void All_contains_every_permission_from_the_specification()
    {
        Permissions.All.Should().BeEquivalentTo(new[]
        {
            "FamilyTree.View", "FamilyTree.Edit",
            "Member.View", "Member.Create", "Member.Edit", "Member.Move", "Member.Delete",
            "User.View", "User.Create", "User.Edit", "User.Deactivate",
            "Role.View", "Role.Create", "Role.Edit", "Role.Delete",
            "Audit.View",
            "PublicLink.Create", "PublicLink.Revoke"
        });
    }

    [Fact]
    public void All_contains_no_duplicates()
    {
        Permissions.All.Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Write the failing Role and RefreshToken tests**

Create `tests/FamilyTree.Domain.Tests/Authorization/RoleTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Tests.Authorization;

public class RoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Fact]
    public void Create_makes_a_tenant_scoped_custom_role()
    {
        var role = Role.Create(TenantId, "Genealogy Editor", "Can edit members", Now);

        role.TenantId.Should().Be(TenantId);
        role.Name.Should().Be("Genealogy Editor");
        role.Description.Should().Be("Can edit members");
        role.IsSystem.Should().BeFalse();
    }

    [Fact]
    public void CreateSystem_marks_the_role_as_system_owned()
    {
        var role = Role.CreateSystem(TenantId, "Super Admin", "All permissions", Now);

        role.IsSystem.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => Role.Create(TenantId, name, null, Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("ROLE_NAME_REQUIRED");
    }

    [Fact]
    public void EnsureDeletable_rejects_deleting_a_system_role()
    {
        var role = Role.CreateSystem(TenantId, "Super Admin", null, Now);

        var act = role.EnsureDeletable;

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("ROLE_IS_SYSTEM");
    }

    [Fact]
    public void EnsureDeletable_allows_deleting_a_custom_role()
    {
        var role = Role.Create(TenantId, "Genealogy Editor", null, Now);

        var act = role.EnsureDeletable;

        act.Should().NotThrow();
    }

    [Fact]
    public void Rename_rejects_renaming_a_system_role()
    {
        var role = Role.CreateSystem(TenantId, "Viewer", null, Now);

        var act = () => role.Rename("Something Else", Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("ROLE_IS_SYSTEM");
    }
}
```

Create `tests/FamilyTree.Domain.Tests/Authentication/RefreshTokenTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Domain.Authentication;

namespace FamilyTree.Domain.Tests.Authentication;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private static RefreshToken Issue() =>
        RefreshToken.Issue(UserId, TenantId, "hash-of-token", Now, Lifetime);

    [Fact]
    public void Issue_sets_expiry_from_the_lifetime_and_leaves_the_token_active()
    {
        var token = Issue();

        token.UserId.Should().Be(UserId);
        token.TenantId.Should().Be(TenantId);
        token.TokenHash.Should().Be("hash-of-token");
        token.ExpiresAt.Should().Be(Now + Lifetime);
        token.RevokedAt.Should().BeNull();
        token.IsActive(Now).Should().BeTrue();
    }

    [Fact]
    public void A_token_is_inactive_once_it_expires()
    {
        var token = Issue();

        token.IsActive(Now + Lifetime).Should().BeFalse();
        token.IsActive(Now + Lifetime + TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void Revoke_deactivates_the_token_and_records_its_replacement()
    {
        var token = Issue();
        var later = Now.AddHours(1);

        token.Revoke(later, "hash-of-next-token");

        token.RevokedAt.Should().Be(later);
        token.ReplacedByTokenHash.Should().Be("hash-of-next-token");
        token.IsActive(later).Should().BeFalse();
    }

    [Fact]
    public void Revoking_an_already_revoked_token_keeps_the_first_revocation_time()
    {
        var token = Issue();
        var first = Now.AddHours(1);

        token.Revoke(first, null);
        token.Revoke(Now.AddHours(5), null);

        token.RevokedAt.Should().Be(first);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Domain.Tests`
Expected: compilation failure — `Permissions`, `Role`, and `RefreshToken` do not exist.

- [ ] **Step 4: Write the permission catalog**

Create `src/FamilyTree.Domain/Authorization/Permissions.cs`:

```csharp
namespace FamilyTree.Domain.Authorization;

/// <summary>
/// The permission catalog from SRS §21. Authorization is evaluated against these codes,
/// never against role names — that is what makes custom roles possible.
/// Adding a capability means adding a constant here plus a seed row; no handler changes.
/// </summary>
public static class Permissions
{
    public static class FamilyTree
    {
        public const string View = "FamilyTree.View";
        public const string Edit = "FamilyTree.Edit";
    }

    public static class Member
    {
        public const string View = "Member.View";
        public const string Create = "Member.Create";
        public const string Edit = "Member.Edit";
        public const string Move = "Member.Move";
        public const string Delete = "Member.Delete";
    }

    public static class User
    {
        public const string View = "User.View";
        public const string Create = "User.Create";
        public const string Edit = "User.Edit";
        public const string Deactivate = "User.Deactivate";
    }

    public static class Role
    {
        public const string View = "Role.View";
        public const string Create = "Role.Create";
        public const string Edit = "Role.Edit";
        public const string Delete = "Role.Delete";
    }

    public static class Audit
    {
        public const string View = "Audit.View";
    }

    public static class PublicLink
    {
        public const string Create = "PublicLink.Create";
        public const string Revoke = "PublicLink.Revoke";
    }

    public static IReadOnlyList<string> All { get; } =
    [
        FamilyTree.View, FamilyTree.Edit,
        Member.View, Member.Create, Member.Edit, Member.Move, Member.Delete,
        User.View, User.Create, User.Edit, User.Deactivate,
        Role.View, Role.Create, Role.Edit, Role.Delete,
        Audit.View,
        PublicLink.Create, PublicLink.Revoke
    ];
}
```

- [ ] **Step 5: Write the authorization entities**

Create `src/FamilyTree.Domain/Authorization/Permission.cs`:

```csharp
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Authorization;

/// <summary>A system-level capability definition. Not tenant-owned — the catalog is global.</summary>
public sealed class Permission : Entity
{
    private Permission() { }

    public string Code { get; private set; } = null!;
    public string? Description { get; private set; }

    public static Permission Create(string code, string? description, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("PERMISSION_CODE_REQUIRED", "Permission code is required.");

        var permission = new Permission { Code = code.Trim(), Description = description };
        permission.InitializeTimestamps(now);
        return permission;
    }
}
```

Create `src/FamilyTree.Domain/Authorization/Role.cs`:

```csharp
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Authorization;

public sealed class Role : Entity, ITenantOwned
{
    public const int MaxNameLength = 100;

    private Role() { }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>Seeded roles cannot be renamed or deleted, so a tenant cannot lock itself out.</summary>
    public bool IsSystem { get; private set; }

    public static Role Create(Guid tenantId, string name, string? description, DateTimeOffset now) =>
        Build(tenantId, name, description, isSystem: false, now);

    public static Role CreateSystem(Guid tenantId, string name, string? description, DateTimeOffset now) =>
        Build(tenantId, name, description, isSystem: true, now);

    public void Rename(string name, DateTimeOffset now)
    {
        EnsureDeletable();
        Name = ValidateName(name);
        Touch(now);
    }

    /// <summary>Throws when the role is system-owned. Also guards renaming.</summary>
    public void EnsureDeletable()
    {
        if (IsSystem)
            throw new DomainException("ROLE_IS_SYSTEM", "System roles cannot be modified or deleted.");
    }

    private static Role Build(Guid tenantId, string name, string? description, bool isSystem, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("ROLE_TENANT_REQUIRED", "A role must belong to a tenant.");

        var role = new Role { TenantId = tenantId, Description = description, IsSystem = isSystem };
        role.Name = ValidateName(name);
        role.InitializeTimestamps(now);
        return role;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("ROLE_NAME_REQUIRED", "Role name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException("ROLE_NAME_TOO_LONG", $"Role name exceeds {MaxNameLength} characters.");
        return trimmed;
    }
}
```

Create `src/FamilyTree.Domain/Authorization/RolePermission.cs`:

```csharp
namespace FamilyTree.Domain.Authorization;

/// <summary>Join entity. Composite key (RoleId, PermissionId) is configured in Task 5.</summary>
public sealed class RolePermission
{
    private RolePermission() { }

    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    public static RolePermission Create(Guid roleId, Guid permissionId) =>
        new() { RoleId = roleId, PermissionId = permissionId };
}
```

Create `src/FamilyTree.Domain/Authorization/UserRole.cs`:

```csharp
namespace FamilyTree.Domain.Authorization;

/// <summary>Join entity. Composite key (UserId, RoleId) is configured in Task 5.</summary>
public sealed class UserRole
{
    private UserRole() { }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }

    public static UserRole Create(Guid userId, Guid roleId) =>
        new() { UserId = userId, RoleId = roleId };
}
```

- [ ] **Step 6: Write the RefreshToken entity**

Create `src/FamilyTree.Domain/Authentication/RefreshToken.cs`:

```csharp
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Authentication;

/// <summary>
/// One row per issued refresh token. Only the hash is stored, never the raw token.
/// Rotation on use means a replaced token records what superseded it, so a replayed
/// old token is detectable.
/// </summary>
public sealed class RefreshToken : Entity, ITenantOwned
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public static RefreshToken Issue(
        Guid userId, Guid tenantId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("REFRESH_TOKEN_HASH_REQUIRED", "Refresh token hash is required.");

        var token = new RefreshToken
        {
            UserId = userId,
            TenantId = tenantId,
            TokenHash = tokenHash,
            ExpiresAt = now + lifetime
        };
        token.InitializeTimestamps(now);
        return token;
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now, string? replacedByTokenHash)
    {
        if (RevokedAt is not null) return;

        RevokedAt = now;
        ReplacedByTokenHash = replacedByTokenHash;
        Touch(now);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Domain.Tests`
Expected: PASS — 26 tests total.

- [ ] **Step 8: Commit**

```bash
git add src/FamilyTree.Domain tests/FamilyTree.Domain.Tests
git commit -m "feat: add permission catalog, role, and refresh token entities"
```

---

### Task 5: Persistence — DbContext, configurations, and the initial migration

**Files:**
- Create: `src/FamilyTree.Infrastructure/Identity/ApplicationUser.cs`
- Create: `src/FamilyTree.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `src/FamilyTree.Infrastructure/Persistence/Configurations/{Tenant,FamilyTree,Permission,Role,RolePermission,UserRole,RefreshToken}Configuration.cs`
- Create: `src/FamilyTree.Application/Common/ITenantContext.cs`
- Modify: `src/FamilyTree.Api/Program.cs` (register the DbContext so `dotnet ef` can build a design-time model)
- Create: `src/FamilyTree.Infrastructure/Persistence/Migrations/*` (generated)

**Interfaces:**
- Consumes: every entity from Tasks 2–4.
- Produces:
  - `interface ITenantContext { Guid TenantId { get; } Guid UserId { get; } bool IsAuthenticated { get; } }`
  - `ApplicationDbContext(DbContextOptions<ApplicationDbContext>, ITenantContext)` exposing `DbSet<Tenant> Tenants`, `DbSet<FamilyTreeAggregate> FamilyTrees`, `DbSet<Permission> Permissions`, `DbSet<Role> Roles`, `DbSet<RolePermission> RolePermissions`, `DbSet<UserRole> UserRoles`, `DbSet<RefreshToken> RefreshTokens`, plus the Identity sets.
  - `class ApplicationUser : IdentityUser<Guid>` with `Guid TenantId`, `bool IsActive`, `DateTimeOffset CreatedAt`, `DateTimeOffset? LastLoginAt`.

- [ ] **Step 1: Add the persistence packages**

```bash
dotnet add src/FamilyTree.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/FamilyTree.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/FamilyTree.Infrastructure package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/FamilyTree.Infrastructure package EFCore.NamingConventions
dotnet add src/FamilyTree.Api package Microsoft.EntityFrameworkCore.Design
dotnet tool install --global dotnet-ef
```

- [ ] **Step 2: Define the tenant context abstraction**

Create `src/FamilyTree.Application/Common/ITenantContext.cs`:

```csharp
namespace FamilyTree.Application.Common;

/// <summary>
/// The tenant and user for the current request, resolved server-side from the authenticated
/// principal. Never populated from a header, query string, or route value — see spec §2.3.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}
```

- [ ] **Step 3: Define the Identity user**

Create `src/FamilyTree.Infrastructure/Identity/ApplicationUser.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace FamilyTree.Infrastructure.Identity;

/// <summary>
/// Identity supplies the credential store. Roles are NOT Identity roles — they are
/// tenant-scoped and permission-backed, which Identity's global roles cannot express.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
```

- [ ] **Step 4: Write the DbContext**

Create `src/FamilyTree.Infrastructure/Persistence/ApplicationDbContext.cs`:

```csharp
using FamilyTree.Application.Common;
using FamilyTree.Domain.Authentication;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantContext tenantContext)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    /// <summary>
    /// Read once per context instance and referenced by the global query filters below.
    /// EF re-evaluates this field per query, so filters follow the request's tenant.
    /// The context is registered scoped (not pooled) precisely so this stays correct.
    /// </summary>
    private readonly Guid _tenantId = tenantContext.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<FamilyTreeAggregate> FamilyTrees => Set<FamilyTreeAggregate>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Global query filters — the reason a forgotten Where clause is not a vulnerability.
        builder.Entity<FamilyTreeAggregate>().HasQueryFilter(x => x.TenantId == _tenantId);
        builder.Entity<Role>().HasQueryFilter(x => x.TenantId == _tenantId);
        builder.Entity<RefreshToken>().HasQueryFilter(x => x.TenantId == _tenantId);
        builder.Entity<ApplicationUser>().HasQueryFilter(x => x.TenantId == _tenantId);

        // Tenant and Permission are deliberately unfiltered: Tenant is the filter's own subject,
        // and the permission catalog is system-level rather than tenant-owned.
    }
}
```

- [ ] **Step 5: Write the entity configurations**

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/TenantConfiguration.cs`:

```csharp
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(Tenant.MaxNameLength);
        builder.Property(x => x.Slug).IsRequired().HasMaxLength(Tenant.MaxSlugLength);
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}
```

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/FamilyTreeConfiguration.cs`:

```csharp
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class FamilyTreeConfiguration : IEntityTypeConfiguration<FamilyTreeAggregate>
{
    public void Configure(EntityTypeBuilder<FamilyTreeAggregate> builder)
    {
        builder.ToTable("family_trees");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(FamilyTreeAggregate.MaxNameLength);

        // BR-001: one customer owns exactly one family tree in V1.
        builder.HasIndex(x => x.TenantId).IsUnique();

        builder.HasOne<Tenant>()
               .WithMany()
               .HasForeignKey(x => x.TenantId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
```

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/PermissionConfiguration.cs`:

```csharp
using FamilyTree.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(100);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
```

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/RoleConfiguration.cs`:

```csharp
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(Role.MaxNameLength);
        builder.Property(x => x.Description).HasMaxLength(500);

        // Role names are unique within a tenant, not globally.
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();

        builder.HasOne<Tenant>()
               .WithMany()
               .HasForeignKey(x => x.TenantId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
```

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/RolePermissionConfiguration.cs`:

```csharp
using FamilyTree.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(x => new { x.RoleId, x.PermissionId });

        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/UserRoleConfiguration.cs`:

```csharp
using FamilyTree.Domain.Authorization;
using FamilyTree.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        // Named app_user_roles to avoid colliding with Identity's AspNetUserRoles.
        builder.ToTable("app_user_roles");
        builder.HasKey(x => new { x.UserId, x.RoleId });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

`OnDelete(Restrict)` on the role side is what makes "a role still assigned to users cannot be deleted" (technical spec §20) a database guarantee, not just a service check.

Create `src/FamilyTree.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs`:

```csharp
using FamilyTree.Domain.Authentication;
using FamilyTree.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(200);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.RevokedAt });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 6: Register the DbContext and a design-time tenant context**

Create `src/FamilyTree.Infrastructure/DependencyInjection.cs`:

```csharp
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // AddDbContext, not AddDbContextPool: the context holds per-request tenant state,
        // and a pooled instance reused across requests would leak tenant scope. See plan header.
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
                   .UseSnakeCaseNamingConvention());

        return services;
    }
}
```

Replace `src/FamilyTree.Api/Program.cs` with a minimal host that can build the model:

```csharp
using FamilyTree.Api.Middleware;
using FamilyTree.Application.Common;
using FamilyTree.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program;
```

The trailing `public partial class Program;` is what lets `WebApplicationFactory<Program>` find the entry point in Task 6.

Create `src/FamilyTree.Api/Middleware/HttpTenantContext.cs`:

```csharp
using System.Security.Claims;
using FamilyTree.Application.Common;

namespace FamilyTree.Api.Middleware;

/// <summary>
/// Reads tenant and user identity from the authenticated principal's claims only.
/// Anything the client can set — headers, query strings, route values — is ignored by design.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public const string TenantIdClaim = "tenant_id";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid TenantId =>
        Guid.TryParse(Principal?.FindFirstValue(TenantIdClaim), out var id) ? id : Guid.Empty;

    public Guid UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
```

An unauthenticated request yields `Guid.Empty`, which matches no row — so the filters fail closed rather than open.

- [ ] **Step 7: Generate and apply the initial migration**

```bash
docker compose up -d postgres
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=familytree;Username=familytree;Password=devpassword"

dotnet ef migrations add InitialCreate \
  --project src/FamilyTree.Infrastructure \
  --startup-project src/FamilyTree.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/FamilyTree.Infrastructure \
  --startup-project src/FamilyTree.Api
```

- [ ] **Step 8: Verify the schema landed with snake_case names**

```bash
docker compose exec postgres psql -U familytree -d familytree -c "\dt"
```

Expected tables include: `tenants`, `family_trees`, `permissions`, `roles`, `role_permissions`, `app_user_roles`, `refresh_tokens`, `asp_net_users`, plus the remaining Identity tables. Every identifier is lower snake_case.

- [ ] **Step 9: Commit**

```bash
git add src/FamilyTree.Infrastructure src/FamilyTree.Application src/FamilyTree.Api
git commit -m "feat: add DbContext, entity configurations, and initial migration"
```

---

### Task 6: Integration test harness and the tenant isolation proof

This is the most important test in Phase 1. Everything else in the system assumes it holds.

**Files:**
- Create: `tests/FamilyTree.Api.IntegrationTests/Fixtures/PostgresFixture.cs`, `Fixtures/StubTenantContext.cs`, `Fixtures/DatabaseTestBase.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Persistence/TenantIsolationTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `ITenantContext`, `Tenant`, `FamilyTreeAggregate`, `Role` from Tasks 2–5.
- Produces:
  - `PostgresFixture : IAsyncLifetime` — `string ConnectionString { get; }`, registered as an xUnit collection fixture named `"postgres"`.
  - `StubTenantContext(Guid tenantId, Guid userId) : ITenantContext` — lets a test act as a specific tenant.
  - `DatabaseTestBase` — `ApplicationDbContext ContextFor(Guid tenantId)` and `Task ResetAsync()`.

- [ ] **Step 1: Add the integration test packages**

```bash
dotnet add tests/FamilyTree.Api.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/FamilyTree.Api.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/FamilyTree.Api.IntegrationTests package Microsoft.EntityFrameworkCore.Design
dotnet add tests/FamilyTree.Api.IntegrationTests reference src/FamilyTree.Infrastructure
```

- [ ] **Step 2: Write the container fixture**

Create `tests/FamilyTree.Api.IntegrationTests/Fixtures/PostgresFixture.cs`:

```csharp
using Testcontainers.PostgreSql;

namespace FamilyTree.Api.IntegrationTests.Fixtures;

/// <summary>
/// One real PostgreSQL container shared by the whole test collection. Real Postgres, never the
/// in-memory provider — recursive CTEs, composite foreign keys, and transaction behavior do not
/// exist in a fake, and those are exactly what these tests verify.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("familytree_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

- [ ] **Step 3: Write the tenant context stub and the test base**

Create `tests/FamilyTree.Api.IntegrationTests/Fixtures/StubTenantContext.cs`:

```csharp
using FamilyTree.Application.Common;

namespace FamilyTree.Api.IntegrationTests.Fixtures;

public sealed class StubTenantContext(Guid tenantId, Guid userId) : ITenantContext
{
    public Guid TenantId { get; } = tenantId;
    public Guid UserId { get; } = userId;
    public bool IsAuthenticated => TenantId != Guid.Empty;
}
```

Create `tests/FamilyTree.Api.IntegrationTests/Fixtures/DatabaseTestBase.cs`:

```csharp
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Fixtures;

[Collection("postgres")]
public abstract class DatabaseTestBase(PostgresFixture fixture) : IAsyncLifetime
{
    protected static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A context scoped to one tenant. Passing Guid.Empty models an unauthenticated caller,
    /// which must see nothing.
    /// </summary>
    protected ApplicationDbContext ContextFor(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options, new StubTenantContext(tenantId, Guid.CreateVersion7()));
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = ContextFor(Guid.Empty);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Each test class gets a freshly migrated database. That is slower than truncating tables, but it also verifies the migration applies cleanly on every run — worth the seconds at this size.

- [ ] **Step 4: Write the failing isolation tests**

Create `tests/FamilyTree.Api.IntegrationTests/Persistence/TenantIsolationTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Persistence;

public sealed class TenantIsolationTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private async Task<(Guid TenantA, Guid TenantB)> SeedTwoTenantsAsync()
    {
        // Seeding runs through an unfiltered context; production seeds exactly one tenant,
        // but the isolation guarantee is untestable with fewer than two (spec §6).
        await using var context = ContextFor(Guid.Empty);

        var a = Tenant.Create("Al-Saqqa Family", "al-saqqa", Now);
        var b = Tenant.Create("Al-Hassan Family", "al-hassan", Now);
        context.Tenants.AddRange(a, b);

        context.FamilyTrees.AddRange(
            FamilyTreeAggregate.Create(a.Id, "عائلة السقا", Now),
            FamilyTreeAggregate.Create(b.Id, "عائلة الحسن", Now));

        context.Roles.AddRange(
            Role.CreateSystem(a.Id, "Super Admin", null, Now),
            Role.CreateSystem(b.Id, "Super Admin", null, Now));

        await context.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task A_tenant_sees_only_its_own_family_tree()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync();

        await using var context = ContextFor(tenantA);
        var trees = await context.FamilyTrees.ToListAsync();

        trees.Should().ContainSingle();
        trees[0].TenantId.Should().Be(tenantA);
        trees[0].Name.Should().Be("عائلة السقا");
    }

    [Fact]
    public async Task Fetching_another_tenants_tree_by_its_exact_id_returns_null()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        Guid foreignTreeId;
        await using (var unfiltered = ContextFor(Guid.Empty))
        {
            foreignTreeId = await unfiltered.FamilyTrees
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantB)
                .Select(x => x.Id)
                .SingleAsync();
        }

        await using var context = ContextFor(tenantA);
        var found = await context.FamilyTrees.FirstOrDefaultAsync(x => x.Id == foreignTreeId);

        // Null is what lets the endpoint layer answer 404 rather than 403 — a 403 would
        // confirm the id exists, which is itself a disclosure.
        found.Should().BeNull();
    }

    [Fact]
    public async Task Roles_are_scoped_to_the_requesting_tenant()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync();

        await using var context = ContextFor(tenantA);
        var roles = await context.Roles.ToListAsync();

        roles.Should().ContainSingle().Which.TenantId.Should().Be(tenantA);
    }

    [Fact]
    public async Task An_unauthenticated_context_sees_no_tenant_owned_rows()
    {
        await SeedTwoTenantsAsync();

        await using var context = ContextFor(Guid.Empty);

        // Fails closed: an empty tenant id matches no row rather than matching every row.
        (await context.FamilyTrees.CountAsync()).Should().Be(0);
        (await context.Roles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_permission_catalog_is_visible_regardless_of_tenant()
    {
        await SeedTwoTenantsAsync();

        await using (var seed = ContextFor(Guid.Empty))
        {
            seed.Permissions.Add(Permission.Create(Permissions.Member.View, "View members", Now));
            await seed.SaveChangesAsync();
        }

        await using var context = ContextFor(Guid.CreateVersion7());
        (await context.Permissions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task One_tenant_cannot_own_two_family_trees()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync();

        await using var context = ContextFor(Guid.Empty);
        context.FamilyTrees.Add(FamilyTreeAggregate.Create(tenantA, "A Second Tree", Now));

        var act = () => context.SaveChangesAsync();

        // BR-001 enforced by the unique index on tenant_id, not by service code.
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests`
Expected: compilation failure — the fixtures do not exist yet if steps 2–3 were skipped; otherwise all six tests run and any missing filter shows up as a specific assertion failure.

Docker must be running. If the container cannot start, fix that before continuing — falling back to a local database or an in-memory provider defeats the purpose of these tests.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests`
Expected: PASS — 6 tests. First run pulls the `postgres:17-alpine` image and takes noticeably longer.

- [ ] **Step 7: Commit**

```bash
git add tests/FamilyTree.Api.IntegrationTests
git commit -m "test: prove tenant isolation against real postgres"
```

---

### Task 7: JWT issuance and refresh token rotation

**Files:**
- Create: `src/FamilyTree.Infrastructure/Auth/JwtOptions.cs`, `Auth/JwtTokenService.cs`
- Create: `src/FamilyTree.Application/Auth/ITokenService.cs`
- Test: `tests/FamilyTree.Application.Tests/Auth/JwtTokenServiceTests.cs`

**Interfaces:**
- Consumes: `ApplicationUser` (Task 5), `HttpTenantContext.TenantIdClaim` (Task 5).
- Produces:
  - `record AccessToken(string Value, DateTimeOffset ExpiresAt)`
  - `record RefreshTokenPair(string RawToken, string TokenHash)`
  - `interface ITokenService` — `AccessToken CreateAccessToken(Guid userId, Guid tenantId, string email, IReadOnlyCollection<string> permissions)`, `RefreshTokenPair CreateRefreshToken()`, `string HashRefreshToken(string rawToken)`.

- [ ] **Step 1: Add the JWT packages**

```bash
dotnet add src/FamilyTree.Infrastructure package Microsoft.IdentityModel.JsonWebTokens
dotnet add src/FamilyTree.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add tests/FamilyTree.Application.Tests reference src/FamilyTree.Infrastructure
dotnet add tests/FamilyTree.Application.Tests package Microsoft.IdentityModel.JsonWebTokens
dotnet add tests/FamilyTree.Application.Tests package Microsoft.Extensions.Options
```

- [ ] **Step 2: Write the failing tests**

Create `tests/FamilyTree.Application.Tests/Auth/JwtTokenServiceTests.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using FamilyTree.Application.Auth;
using FamilyTree.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace FamilyTree.Application.Tests.Auth;

public class JwtTokenServiceTests
{
    private const string SigningKey = "test-signing-key-that-is-at-least-32-bytes-long!!";
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();

    private static JwtTokenService CreateService() =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "https://localhost:5001",
            Audience = "familytree-api",
            SigningKey = SigningKey,
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 14
        }), TimeProvider.System);

    private static JsonWebToken Parse(string token) => new JsonWebTokenHandler().ReadJsonWebToken(token);

    [Fact]
    public void CreateAccessToken_embeds_the_user_tenant_and_permissions()
    {
        var token = CreateService().CreateAccessToken(
            UserId, TenantId, "admin@example.com", ["Member.View", "Member.Create"]);

        var jwt = Parse(token.Value);

        jwt.GetClaim(ClaimTypes.NameIdentifier).Value.Should().Be(UserId.ToString());
        jwt.GetClaim("tenant_id").Value.Should().Be(TenantId.ToString());
        jwt.Claims.Where(c => c.Type == "permission").Select(c => c.Value)
           .Should().BeEquivalentTo("Member.View", "Member.Create");
    }

    [Fact]
    public void CreateAccessToken_expires_in_fifteen_minutes()
    {
        var token = CreateService().CreateAccessToken(UserId, TenantId, "admin@example.com", []);

        token.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateAccessToken_produces_a_token_that_validates_against_the_signing_key()
    {
        var token = CreateService().CreateAccessToken(UserId, TenantId, "admin@example.com", []);

        var result = new JsonWebTokenHandler().ValidateTokenAsync(token.Value, new TokenValidationParameters
        {
            ValidIssuer = "https://localhost:5001",
            ValidAudience = "familytree-api",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateIssuerSigningKey = true
        }).GetAwaiter().GetResult();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateRefreshToken_returns_a_raw_token_and_its_hash_which_differ()
    {
        var pair = CreateService().CreateRefreshToken();

        pair.RawToken.Should().NotBeNullOrWhiteSpace();
        pair.TokenHash.Should().NotBeNullOrWhiteSpace();
        pair.TokenHash.Should().NotBe(pair.RawToken, "only the hash is ever persisted");
    }

    [Fact]
    public void CreateRefreshToken_never_repeats_a_value()
    {
        var service = CreateService();

        var tokens = Enumerable.Range(0, 100).Select(_ => service.CreateRefreshToken().RawToken).ToArray();

        tokens.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void HashRefreshToken_is_deterministic_so_a_presented_token_can_be_looked_up()
    {
        var service = CreateService();
        var pair = service.CreateRefreshToken();

        service.HashRefreshToken(pair.RawToken).Should().Be(pair.TokenHash);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Application.Tests`
Expected: compilation failure — `JwtOptions`, `JwtTokenService`, and `ITokenService` do not exist.

- [ ] **Step 4: Write the token service contract**

Create `src/FamilyTree.Application/Auth/ITokenService.cs`:

```csharp
namespace FamilyTree.Application.Auth;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>The raw token goes to the client exactly once; only the hash is persisted.</summary>
public sealed record RefreshTokenPair(string RawToken, string TokenHash);

public interface ITokenService
{
    AccessToken CreateAccessToken(
        Guid userId, Guid tenantId, string email, IReadOnlyCollection<string> permissions);

    RefreshTokenPair CreateRefreshToken();

    string HashRefreshToken(string rawToken);
}
```

- [ ] **Step 5: Write the implementation**

Create `src/FamilyTree.Infrastructure/Auth/JwtOptions.cs`:

```csharp
namespace FamilyTree.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = null!;
    public string Audience { get; init; } = null!;
    public string SigningKey { get; init; } = null!;
    public int AccessTokenLifetimeMinutes { get; init; } = 15;
    public int RefreshTokenLifetimeDays { get; init; } = 14;
}
```

Create `src/FamilyTree.Infrastructure/Auth/JwtTokenService.cs`:

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FamilyTree.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FamilyTree.Infrastructure.Auth;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    public const string TenantIdClaim = "tenant_id";
    public const string PermissionClaim = "permission";

    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateAccessToken(
        Guid userId, Guid tenantId, string email, IReadOnlyCollection<string> permissions)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(TenantIdClaim, tenantId.ToString())
        };
        claims.AddRange(permissions.Select(p => new Claim(PermissionClaim, p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = expires.UtcDateTime,
            SigningCredentials = credentials
        };

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public RefreshTokenPair CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new RefreshTokenPair(raw, HashRefreshToken(raw));
    }

    /// <summary>
    /// SHA-256, not a password hash. The token is 256 bits of cryptographic randomness, so it
    /// is not brute-forceable and needs no work factor — but hashing still means a database
    /// leak yields no usable tokens. It must stay deterministic so a presented token is findable.
    /// </summary>
    public string HashRefreshToken(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests`
Expected: PASS — 6 tests.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Application src/FamilyTree.Infrastructure tests/FamilyTree.Application.Tests
git commit -m "feat: add JWT issuance and refresh token hashing"
```

---

*Tasks 8–12 are appended in the sections that follow.*
