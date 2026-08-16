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
  Auth/ITokenService.cs  IAuthService.cs      interfaces only; implementations live in Infrastructure
  Authorization/IPermissionResolver.cs

src/FamilyTree.Infrastructure/
  Persistence/ApplicationDbContext.cs
  Persistence/Configurations/*.cs  one file per entity
  Persistence/Seed/SeedOptions.cs  SystemRoles.cs  DatabaseSeeder.cs
  Identity/ApplicationUser.cs      the IdentityUser<Guid> subclass
  Auth/JwtTokenService.cs  JwtOptions.cs  AuthService.cs
  Authorization/PermissionResolver.cs
  DependencyInjection.cs

src/FamilyTree.Api/
  Program.cs
  Middleware/HttpTenantContext.cs  scoped ITenantContext over IHttpContextAccessor —
                                   no custom middleware class is needed
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
Expected: PASS — 11 tests (1 dependency guard plus 10 Tenant cases; each `[Theory]` row counts separately).

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
Expected: PASS — 17 tests total.

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
Expected: PASS — 30 tests total.

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

### Task 8: Permission resolution and the database seeder

**Files:**
- Create: `src/FamilyTree.Application/Authorization/IPermissionResolver.cs`
- Create: `src/FamilyTree.Infrastructure/Authorization/PermissionResolver.cs`
- Create: `src/FamilyTree.Infrastructure/Persistence/Seed/SeedOptions.cs`, `Seed/SystemRoles.cs`, `Seed/DatabaseSeeder.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Authorization/PermissionResolverTests.cs`, `Persistence/DatabaseSeederTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `Permissions`, `Role`, `RolePermission`, `UserRole`, `ApplicationUser`, `Tenant`, `FamilyTreeAggregate`.
- Produces:
  - `interface IPermissionResolver { Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default); }`
  - `static class SystemRoles` — `const string SuperAdmin/Administrator/Editor/Viewer`, and `IReadOnlyDictionary<string, IReadOnlyList<string>> Definitions`.
  - `DatabaseSeeder.SeedAsync(CancellationToken)` — idempotent.

- [ ] **Step 1: Write the failing permission resolver test**

Create `tests/FamilyTree.Api.IntegrationTests/Authorization/PermissionResolverTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Authorization;
using FamilyTree.Infrastructure.Identity;

namespace FamilyTree.Api.IntegrationTests.Authorization;

public sealed class PermissionResolverTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private async Task<(Guid TenantId, Guid UserId)> SeedUserWithRolesAsync(
        params (string RoleName, string[] Permissions)[] roles)
    {
        await using var context = ContextFor(Guid.Empty);

        var tenant = Tenant.Create("Al-Saqqa Family", "al-saqqa", Now);
        context.Tenants.Add(tenant);

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Email = "admin@example.com",
            UserName = "admin@example.com",
            CreatedAt = Now
        };
        context.Users.Add(user);

        var catalog = Permissions.All
            .Select(code => Permission.Create(code, null, Now))
            .ToDictionary(p => p.Code);
        context.Permissions.AddRange(catalog.Values);

        foreach (var (roleName, permissionCodes) in roles)
        {
            var role = Role.Create(tenant.Id, roleName, null, Now);
            context.Roles.Add(role);
            context.UserRoles.Add(UserRole.Create(user.Id, role.Id));
            context.RolePermissions.AddRange(
                permissionCodes.Select(code => RolePermission.Create(role.Id, catalog[code].Id)));
        }

        await context.SaveChangesAsync();
        return (tenant.Id, user.Id);
    }

    [Fact]
    public async Task Resolves_the_permissions_granted_by_a_users_single_role()
    {
        var (tenantId, userId) = await SeedUserWithRolesAsync(
            ("Editor", [Permissions.Member.View, Permissions.Member.Create]));

        await using var context = ContextFor(tenantId);
        var resolver = new PermissionResolver(context, new StubTenantContext(tenantId, userId));

        var permissions = await resolver.GetPermissionsAsync(userId);

        permissions.Should().BeEquivalentTo("Member.View", "Member.Create");
    }

    [Fact]
    public async Task Resolves_the_union_of_multiple_roles_without_duplicates()
    {
        var (tenantId, userId) = await SeedUserWithRolesAsync(
            ("Viewer", [Permissions.Member.View]),
            ("Mover",  [Permissions.Member.View, Permissions.Member.Move]));

        await using var context = ContextFor(tenantId);
        var resolver = new PermissionResolver(context, new StubTenantContext(tenantId, userId));

        var permissions = await resolver.GetPermissionsAsync(userId);

        permissions.Should().BeEquivalentTo("Member.View", "Member.Move");
        permissions.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Returns_empty_for_a_user_with_no_roles()
    {
        var (tenantId, userId) = await SeedUserWithRolesAsync();

        await using var context = ContextFor(tenantId);
        var resolver = new PermissionResolver(context, new StubTenantContext(tenantId, userId));

        (await resolver.GetPermissionsAsync(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_empty_for_a_user_id_belonging_to_another_tenant()
    {
        var (_, userId) = await SeedUserWithRolesAsync(("Editor", [Permissions.Member.View]));
        var otherTenant = Guid.CreateVersion7();

        await using var context = ContextFor(otherTenant);
        var resolver = new PermissionResolver(context, new StubTenantContext(otherTenant, userId));

        // The tenant predicate excludes the other tenant's roles, so no permission leaks across.
        (await resolver.GetPermissionsAsync(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_explicit_tenant_overload_resolves_permissions_without_an_ambient_tenant()
    {
        var (tenantId, userId) = await SeedUserWithRolesAsync(
            ("Editor", [Permissions.Member.View, Permissions.Member.Create]));

        // Models login: no authenticated principal yet, so the ambient tenant is empty.
        await using var context = ContextFor(Guid.Empty);
        var resolver = new PermissionResolver(context, new StubTenantContext(Guid.Empty, Guid.Empty));

        var permissions = await resolver.GetPermissionsAsync(userId, tenantId);

        permissions.Should().BeEquivalentTo("Member.View", "Member.Create");
    }

    [Fact]
    public async Task The_explicit_tenant_overload_returns_empty_when_the_tenant_does_not_match()
    {
        var (_, userId) = await SeedUserWithRolesAsync(("Editor", [Permissions.Member.View]));

        await using var context = ContextFor(Guid.Empty);
        var resolver = new PermissionResolver(context, new StubTenantContext(Guid.Empty, Guid.Empty));

        (await resolver.GetPermissionsAsync(userId, Guid.CreateVersion7())).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~PermissionResolverTests`
Expected: compilation failure — `PermissionResolver` does not exist.

- [ ] **Step 3: Write the resolver**

Create `src/FamilyTree.Application/Authorization/IPermissionResolver.cs`:

```csharp
namespace FamilyTree.Application.Authorization;

public interface IPermissionResolver
{
    /// <summary>
    /// The union of every permission granted by every role the user holds, within the
    /// current request's tenant.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The same, for an explicitly supplied tenant. Needed during login, where no
    /// authenticated principal exists yet and the ambient tenant is therefore empty.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default);
}
```

Create `src/FamilyTree.Infrastructure/Authorization/PermissionResolver.cs`:

```csharp
using FamilyTree.Application.Authorization;
using FamilyTree.Application.Common;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Authorization;

public sealed class PermissionResolver(
    ApplicationDbContext context, ITenantContext tenantContext) : IPermissionResolver
{
    public Task<IReadOnlyCollection<string>> GetPermissionsAsync(
        Guid userId, CancellationToken ct = default) =>
        GetPermissionsAsync(userId, tenantContext.TenantId, ct);

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return [];

        // IgnoreQueryFilters with an explicit TenantId predicate: the tenant is stated rather
        // than ambient, which is what makes this usable during login. The predicate is not
        // optional — dropping it would cross tenants.
        var codes = await (
            from userRole in context.UserRoles
            join role in context.Roles.IgnoreQueryFilters() on userRole.RoleId equals role.Id
            join rolePermission in context.RolePermissions on role.Id equals rolePermission.RoleId
            join permission in context.Permissions on rolePermission.PermissionId equals permission.Id
            where userRole.UserId == userId && role.TenantId == tenantId
            select permission.Code)
            .Distinct()
            .ToListAsync(ct);

        return codes;
    }
}
```

- [ ] **Step 4: Write the failing seeder test**

Create `tests/FamilyTree.Api.IntegrationTests/Persistence/DatabaseSeederTests.cs`:

```csharp
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Authorization;
using FamilyTree.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using FamilyTree.Infrastructure.Identity;

namespace FamilyTree.Api.IntegrationTests.Persistence;

public sealed class DatabaseSeederTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly SeedOptions Options = new()
    {
        TenantName = "Al-Saqqa Family",
        TenantSlug = "al-saqqa",
        FamilyTreeName = "عائلة السقا",
        AdminEmail = "admin@example.com",
        AdminPassword = "Str0ng!Seed#Password"
    };

    private async Task RunSeederAsync()
    {
        await using var context = ContextFor(Guid.Empty);
        var hasher = new PasswordHasher<ApplicationUser>();
        var seeder = new DatabaseSeeder(context, hasher, Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);
        await seeder.SeedAsync();
    }

    [Fact]
    public async Task Seeds_one_tenant_one_tree_the_full_catalog_and_four_system_roles()
    {
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);

        (await context.Tenants.CountAsync()).Should().Be(1);
        (await context.FamilyTrees.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await context.Permissions.CountAsync()).Should().Be(Permissions.All.Count);
        (await context.Roles.IgnoreQueryFilters().CountAsync()).Should().Be(4);
        (await context.Roles.IgnoreQueryFilters().CountAsync(r => r.IsSystem)).Should().Be(4);
    }

    [Fact]
    public async Task Grants_the_super_admin_role_every_permission_in_the_catalog()
    {
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);
        var superAdmin = await context.Roles.IgnoreQueryFilters()
            .SingleAsync(r => r.Name == SystemRoles.SuperAdmin);

        var granted = await context.RolePermissions.CountAsync(rp => rp.RoleId == superAdmin.Id);

        granted.Should().Be(Permissions.All.Count);
    }

    [Fact]
    public async Task Creates_the_admin_user_bound_to_the_tenant_with_the_super_admin_role()
    {
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);
        var tenantId = await context.Tenants.Select(t => t.Id).SingleAsync();
        var user = await context.Users.IgnoreQueryFilters().SingleAsync();

        user.Email.Should().Be("admin@example.com");
        user.TenantId.Should().Be(tenantId);
        user.IsActive.Should().BeTrue();
        user.PasswordHash.Should().NotBeNullOrWhiteSpace();
        user.PasswordHash.Should().NotContain("Str0ng!Seed#Password", "the password is hashed, never stored");

        var superAdmin = await context.Roles.IgnoreQueryFilters()
            .SingleAsync(r => r.Name == SystemRoles.SuperAdmin);
        (await context.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == superAdmin.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Running_the_seeder_twice_changes_nothing()
    {
        await RunSeederAsync();
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);

        (await context.Tenants.CountAsync()).Should().Be(1);
        (await context.Users.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await context.Roles.IgnoreQueryFilters().CountAsync()).Should().Be(4);
        (await context.Permissions.CountAsync()).Should().Be(Permissions.All.Count);
    }

    [Fact]
    public async Task Viewer_role_receives_only_read_permissions()
    {
        await RunSeederAsync();

        await using var context = ContextFor(Guid.Empty);
        var viewer = await context.Roles.IgnoreQueryFilters().SingleAsync(r => r.Name == SystemRoles.Viewer);

        var codes = await (from rp in context.RolePermissions
                           join p in context.Permissions on rp.PermissionId equals p.Id
                           where rp.RoleId == viewer.Id
                           select p.Code).ToListAsync();

        codes.Should().BeEquivalentTo("FamilyTree.View", "Member.View");
    }
}
```

- [ ] **Step 5: Write the seed configuration and role definitions**

Create `src/FamilyTree.Infrastructure/Persistence/Seed/SeedOptions.cs`:

```csharp
namespace FamilyTree.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string TenantName { get; init; } = null!;
    public string TenantSlug { get; init; } = null!;
    public string FamilyTreeName { get; init; } = null!;
    public string AdminEmail { get; init; } = null!;

    /// <summary>Supplied by environment variable or user-secrets. Never committed.</summary>
    public string AdminPassword { get; init; } = null!;
}
```

Create `src/FamilyTree.Infrastructure/Persistence/Seed/SystemRoles.cs`:

```csharp
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Infrastructure.Persistence.Seed;

/// <summary>
/// The four predefined roles from SRS §19, expressed as permission sets rather than as
/// hard-coded role checks. Custom roles created later sit alongside these as equals.
/// </summary>
public static class SystemRoles
{
    public const string SuperAdmin = "Super Admin";
    public const string Administrator = "Administrator";
    public const string Editor = "Editor";
    public const string Viewer = "Viewer";

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Definitions { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [SuperAdmin] = Permissions.All,

            [Administrator] =
            [
                Permissions.FamilyTree.View, Permissions.FamilyTree.Edit,
                Permissions.Member.View, Permissions.Member.Create, Permissions.Member.Edit,
                Permissions.Member.Move, Permissions.Member.Delete,
                Permissions.User.View, Permissions.User.Create, Permissions.User.Edit,
                Permissions.User.Deactivate,
                Permissions.Role.View,
                Permissions.Audit.View,
                Permissions.PublicLink.Create, Permissions.PublicLink.Revoke
            ],

            [Editor] =
            [
                Permissions.FamilyTree.View,
                Permissions.Member.View, Permissions.Member.Create,
                Permissions.Member.Edit, Permissions.Member.Move
            ],

            [Viewer] =
            [
                Permissions.FamilyTree.View,
                Permissions.Member.View
            ]
        };
}
```

Only Super Admin can manage roles — that is the deliberate difference between it and Administrator.

- [ ] **Step 6: Write the seeder**

Create `src/FamilyTree.Infrastructure/Persistence/Seed/DatabaseSeeder.cs`:

```csharp
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FamilyTree.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the single V1 tenant, its family tree, the permission catalog, the four system
/// roles, and the first Super Admin. Idempotent: safe to run on every startup.
/// </summary>
public sealed class DatabaseSeeder(
    ApplicationDbContext context,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IOptions<SeedOptions> options,
    TimeProvider timeProvider)
{
    private readonly SeedOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        await SeedPermissionCatalogAsync(now, ct);
        var tenant = await SeedTenantAsync(now, ct);
        await SeedFamilyTreeAsync(tenant.Id, now, ct);
        var roleIds = await SeedSystemRolesAsync(tenant.Id, now, ct);
        await SeedAdminUserAsync(tenant.Id, roleIds[SystemRoles.SuperAdmin], now, ct);

        await transaction.CommitAsync(ct);
    }

    private async Task SeedPermissionCatalogAsync(DateTimeOffset now, CancellationToken ct)
    {
        var existing = await context.Permissions.Select(p => p.Code).ToListAsync(ct);
        var missing = Permissions.All.Except(existing).ToList();
        if (missing.Count == 0) return;

        context.Permissions.AddRange(missing.Select(code => Permission.Create(code, null, now)));
        await context.SaveChangesAsync(ct);
    }

    private async Task<Tenant> SeedTenantAsync(DateTimeOffset now, CancellationToken ct)
    {
        var existing = await context.Tenants.FirstOrDefaultAsync(t => t.Slug == _options.TenantSlug, ct);
        if (existing is not null) return existing;

        var tenant = Tenant.Create(_options.TenantName, _options.TenantSlug, now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(ct);
        return tenant;
    }

    private async Task SeedFamilyTreeAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var exists = await context.FamilyTrees.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId, ct);
        if (exists) return;

        context.FamilyTrees.Add(FamilyTreeAggregate.Create(tenantId, _options.FamilyTreeName, now));
        await context.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<string, Guid>> SeedSystemRolesAsync(
        Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var catalog = await context.Permissions.ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        var roleIds = new Dictionary<string, Guid>();

        foreach (var (roleName, permissionCodes) in SystemRoles.Definitions)
        {
            var role = await context.Roles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == roleName, ct);

            if (role is null)
            {
                role = Role.CreateSystem(tenantId, roleName, null, now);
                context.Roles.Add(role);
                await context.SaveChangesAsync(ct);
            }

            roleIds[roleName] = role.Id;

            var alreadyGranted = await context.RolePermissions
                .Where(rp => rp.RoleId == role.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);

            var toGrant = permissionCodes
                .Select(code => catalog[code])
                .Except(alreadyGranted)
                .Select(permissionId => RolePermission.Create(role.Id, permissionId))
                .ToList();

            if (toGrant.Count > 0)
            {
                context.RolePermissions.AddRange(toGrant);
                await context.SaveChangesAsync(ct);
            }
        }

        return roleIds;
    }

    private async Task SeedAdminUserAsync(
        Guid tenantId, Guid superAdminRoleId, DateTimeOffset now, CancellationToken ct)
    {
        var normalizedEmail = _options.AdminEmail.ToUpperInvariant();

        var user = await context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Email = _options.AdminEmail,
                NormalizedEmail = normalizedEmail,
                UserName = _options.AdminEmail,
                NormalizedUserName = normalizedEmail,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = now,
                SecurityStamp = Guid.CreateVersion7().ToString()
            };
            user.PasswordHash = passwordHasher.HashPassword(user, _options.AdminPassword);

            context.Users.Add(user);
            await context.SaveChangesAsync(ct);
        }

        var hasRole = await context.UserRoles
            .AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == superAdminRoleId, ct);

        if (!hasRole)
        {
            context.UserRoles.Add(UserRole.Create(user.Id, superAdminRoleId));
            await context.SaveChangesAsync(ct);
        }
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests`
Expected: PASS — 17 tests (6 isolation + 6 resolver + 5 seeder).

- [ ] **Step 8: Commit**

```bash
git add src/FamilyTree.Application src/FamilyTree.Infrastructure tests/FamilyTree.Api.IntegrationTests
git commit -m "feat: add permission resolution and idempotent database seeder"
```

---

### Task 9: Authentication service and endpoints

**Files:**
- Create: `src/FamilyTree.Contracts/Auth/{LoginRequest,LoginResponse,RefreshRequest,CurrentUserResponse}.cs`
- Create: `src/FamilyTree.Application/Auth/IAuthService.cs`
- Create: `src/FamilyTree.Infrastructure/Auth/AuthService.cs`
- Create: `src/FamilyTree.Api/Errors/ExceptionHandler.cs`, `Endpoints/Auth/AuthEndpoints.cs`
- Modify: `src/FamilyTree.Api/Program.cs`, `src/FamilyTree.Infrastructure/DependencyInjection.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Fixtures/ApiFactory.cs`, `Endpoints/AuthEndpointsTests.cs`

**Interfaces:**
- Consumes: `ITokenService`, `IPermissionResolver`, `RefreshToken`, `ApplicationUser`, `ApplicationDbContext`.
- Produces:
  - `record LoginRequest(string Email, string Password)`, `record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken)`, `record RefreshRequest(string RefreshToken)`, `record CurrentUserResponse(Guid Id, string Email, Guid TenantId, string FamilyTreeName, IReadOnlyCollection<string> Permissions)`.
  - `record AuthResult(LoginResponse? Response, string? ErrorCode)` with `bool Succeeded => ErrorCode is null`.
  - `interface IAuthService` — `Task<AuthResult> LoginAsync(LoginRequest, CancellationToken)`, `Task<AuthResult> RefreshAsync(string rawRefreshToken, CancellationToken)`, `Task LogoutAsync(string rawRefreshToken, CancellationToken)`.

- [ ] **Step 1: Write the contracts**

Create the four files under `src/FamilyTree.Contracts/Auth/`:

```csharp
// LoginRequest.cs
namespace FamilyTree.Contracts.Auth;
public sealed record LoginRequest(string Email, string Password);
```

```csharp
// LoginResponse.cs
namespace FamilyTree.Contracts.Auth;
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken);
```

```csharp
// RefreshRequest.cs
namespace FamilyTree.Contracts.Auth;
public sealed record RefreshRequest(string RefreshToken);
```

```csharp
// CurrentUserResponse.cs
namespace FamilyTree.Contracts.Auth;

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    Guid TenantId,
    string FamilyTreeName,
    IReadOnlyCollection<string> Permissions);
```

- [ ] **Step 2: Write the failing endpoint tests**

Create `tests/FamilyTree.Api.IntegrationTests/Fixtures/ApiFactory.cs`:

```csharp
using FamilyTree.Infrastructure.Persistence;
using FamilyTree.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FamilyTree.Api.IntegrationTests.Fixtures;

public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "Str0ng!Seed#Password";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Jwt:Issuer", "https://localhost:5001");
        builder.UseSetting("Jwt:Audience", "familytree-api");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-that-is-at-least-32-bytes-long!!");
        builder.UseSetting("Seed:TenantName", "Al-Saqqa Family");
        builder.UseSetting("Seed:TenantSlug", "al-saqqa");
        builder.UseSetting("Seed:FamilyTreeName", "عائلة السقا");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
    }

    /// <summary>Migrates and seeds a clean database for the test class.</summary>
    public async Task ResetAndSeedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }
}
```

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/AuthEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class AuthEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<LoginResponse> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_an_access_token_and_a_refresh_token()
    {
        var login = await LoginAsync();

        login.AccessToken.Should().NotBeNullOrWhiteSpace();
        login.RefreshToken.Should().NotBeNullOrWhiteSpace();
        login.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_returns_401_with_a_stable_code()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, "not-the-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_with_an_unknown_email_returns_the_same_401_and_code()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("nobody@example.com", "whatever"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Identical to the wrong-password response: the API must not reveal which emails exist.
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_with_a_blank_email_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("", "whatever"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_returns_a_new_token_pair()
    {
        var login = await LoginAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(login.RefreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        refreshed.RefreshToken.Should().NotBe(login.RefreshToken, "tokens rotate on use");
        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_refresh_token_cannot_be_used_twice()
    {
        var login = await LoginAsync();

        await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));
        var replay = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await replay.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Refresh_with_a_fabricated_token_returns_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest("this-was-never-issued"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var login = await LoginAsync();

        var logout = await _client.PostAsJsonAsync("/api/v1/auth/logout",
            new RefreshRequest(login.RefreshToken));
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterLogout = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(login.RefreshToken));
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_with_an_unknown_token_still_returns_204()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/logout",
            new RefreshRequest("never-issued"));

        // Logout is idempotent and must not become an oracle for which tokens exist.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~AuthEndpointsTests`
Expected: failure — `/api/v1/auth/login` does not exist, so responses are 404.

- [ ] **Step 4: Write the auth service contract and implementation**

Create `src/FamilyTree.Application/Auth/IAuthService.cs`:

```csharp
using FamilyTree.Contracts.Auth;

namespace FamilyTree.Application.Auth;

public sealed record AuthResult(LoginResponse? Response, string? ErrorCode)
{
    public bool Succeeded => ErrorCode is null;

    public static AuthResult Success(LoginResponse response) => new(response, null);
    public static AuthResult Failure(string errorCode) => new(null, errorCode);
}

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(string rawRefreshToken, CancellationToken ct = default);
    Task LogoutAsync(string rawRefreshToken, CancellationToken ct = default);
}
```

Create `src/FamilyTree.Infrastructure/Auth/AuthService.cs`:

```csharp
using FamilyTree.Application.Auth;
using FamilyTree.Application.Authorization;
using FamilyTree.Contracts.Auth;
using FamilyTree.Domain.Authentication;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FamilyTree.Infrastructure.Auth;

public sealed class AuthService(
    ApplicationDbContext context,
    IPasswordHasher<ApplicationUser> passwordHasher,
    ITokenService tokenService,
    IPermissionResolver permissionResolver,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.ToUpperInvariant();

        // IgnoreQueryFilters: at login time there is no authenticated principal yet, so the
        // tenant filter would exclude every user. This is the one place that is legitimate.
        var user = await context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (user?.PasswordHash is null)
            return AuthResult.Failure("INVALID_CREDENTIALS");

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
            return AuthResult.Failure("INVALID_CREDENTIALS");

        if (!user.IsActive)
            return AuthResult.Failure("ACCOUNT_INACTIVE");

        var tenantActive = await context.Tenants.AnyAsync(t => t.Id == user.TenantId && t.IsActive, ct);
        if (!tenantActive)
            return AuthResult.Failure("TENANT_INACTIVE");

        var now = timeProvider.GetUtcNow();
        user.LastLoginAt = now;

        var response = await IssueTokensAsync(user, now, ct);
        await context.SaveChangesAsync(ct);

        return AuthResult.Success(response);
    }

    public async Task<AuthResult> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var hash = tokenService.HashRefreshToken(rawRefreshToken);
        var now = timeProvider.GetUtcNow();

        var stored = await context.RefreshTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive(now))
            return AuthResult.Failure("INVALID_REFRESH_TOKEN");

        var user = await context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);

        if (user is null || !user.IsActive)
            return AuthResult.Failure("INVALID_REFRESH_TOKEN");

        var response = await IssueTokensAsync(user, now, ct);

        // Rotation: the old token is revoked and records its successor, so replaying it fails
        // and the chain is auditable.
        stored.Revoke(now, tokenService.HashRefreshToken(response.RefreshToken));

        await context.SaveChangesAsync(ct);
        return AuthResult.Success(response);
    }

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var hash = tokenService.HashRefreshToken(rawRefreshToken);

        var stored = await context.RefreshTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        // Silent no-op when unknown: logout must not reveal which tokens exist.
        if (stored is null) return;

        stored.Revoke(timeProvider.GetUtcNow(), replacedByTokenHash: null);
        await context.SaveChangesAsync(ct);
    }

    private async Task<LoginResponse> IssueTokensAsync(
        ApplicationUser user, DateTimeOffset now, CancellationToken ct)
    {
        // Two-argument overload: login has no authenticated principal, so the tenant must be
        // stated explicitly rather than taken from the ambient context.
        var permissions = await permissionResolver.GetPermissionsAsync(user.Id, user.TenantId, ct);

        var access = tokenService.CreateAccessToken(
            user.Id, user.TenantId, user.Email!, permissions);

        var refresh = tokenService.CreateRefreshToken();

        context.RefreshTokens.Add(RefreshToken.Issue(
            user.Id, user.TenantId, refresh.TokenHash, now,
            TimeSpan.FromDays(_jwt.RefreshTokenLifetimeDays)));

        return new LoginResponse(access.Value, access.ExpiresAt, refresh.RawToken);
    }
}
```

Note the call `permissionResolver.GetPermissionsAsync(user.Id, user.TenantId, ct)` inside `IssueTokensAsync` — the two-argument overload from Task 8. At login there is no authenticated principal yet, so the ambient tenant is empty and the one-argument overload would return nothing.

- [ ] **Step 5: Write the error handler**

Create `src/FamilyTree.Api/Errors/ExceptionHandler.cs`:

```csharp
using FamilyTree.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTree.Api.Errors;

/// <summary>
/// Turns domain rule violations into Problem Details carrying the stable machine-readable
/// code. Message text is never the contract — clients translate from `code` (spec §4.8).
/// </summary>
public sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainException domainException) return false;

        logger.LogWarning("Domain rule violated: {Code}", domainException.Code);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Request violates a business rule",
            Detail = domainException.Message,
            Extensions = { ["code"] = domainException.Code }
        };

        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}

public static class ProblemResults
{
    /// <summary>Problem Details with a stable `code` extension, used by every failing endpoint.</summary>
    public static IResult Coded(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?>
        {
            ["code"] = code
        });
}
```

- [ ] **Step 6: Write the auth endpoints**

Create `src/FamilyTree.Api/Endpoints/Auth/AuthEndpoints.cs`:

```csharp
using FamilyTree.Api.Errors;
using FamilyTree.Application.Auth;
using FamilyTree.Contracts.Auth;

namespace FamilyTree.Api.Endpoints.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").AllowAnonymous().WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService auth, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return ProblemResults.Coded(StatusCodes.Status400BadRequest,
                    "VALIDATION_FAILED", "Email and password are required.");

            var result = await auth.LoginAsync(request, ct);

            return result.Succeeded
                ? Results.Ok(result.Response)
                : ProblemResults.Coded(StatusForCode(result.ErrorCode!), result.ErrorCode!, "Authentication failed.");
        });

        group.MapPost("/refresh", async (RefreshRequest request, IAuthService auth, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return ProblemResults.Coded(StatusCodes.Status400BadRequest,
                    "VALIDATION_FAILED", "Refresh token is required.");

            var result = await auth.RefreshAsync(request.RefreshToken, ct);

            return result.Succeeded
                ? Results.Ok(result.Response)
                : ProblemResults.Coded(StatusCodes.Status401Unauthorized,
                    result.ErrorCode!, "Authentication failed.");
        });

        group.MapPost("/logout", async (RefreshRequest request, IAuthService auth, CancellationToken ct) =>
        {
            await auth.LogoutAsync(request.RefreshToken ?? string.Empty, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static int StatusForCode(string code) => code switch
    {
        "ACCOUNT_INACTIVE" or "TENANT_INACTIVE" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status401Unauthorized
    };
}
```

- [ ] **Step 7: Compose the application host**

Replace `src/FamilyTree.Api/Program.cs`:

```csharp
using System.Text;
using FamilyTree.Api.Endpoints.Auth;
using FamilyTree.Api.Errors;
using FamilyTree.Api.Middleware;
using FamilyTree.Application.Common;
using FamilyTree.Infrastructure;
using FamilyTree.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();

app.Run();

public partial class Program;
```

Extend `src/FamilyTree.Infrastructure/DependencyInjection.cs`:

```csharp
using FamilyTree.Application.Auth;
using FamilyTree.Application.Authorization;
using FamilyTree.Infrastructure.Auth;
using FamilyTree.Infrastructure.Authorization;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence;
using FamilyTree.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
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

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
```

Add the remaining packages:

```bash
dotnet add src/FamilyTree.Api package Serilog.AspNetCore
dotnet add src/FamilyTree.Api package Microsoft.AspNetCore.OpenApi
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests`
Expected: PASS — 26 integration tests (17 from Task 8 plus 9 auth endpoint tests).

- [ ] **Step 9: Commit**

```bash
git add src tests
git commit -m "feat: add authentication service, endpoints, and problem details"
```

---

### Task 10: Permission-based authorization and the `/me` endpoint

**Files:**
- Create: `src/FamilyTree.Api/Authorization/PermissionRequirement.cs`, `PermissionAuthorizationHandler.cs`, `EndpointExtensions.cs`
- Create: `src/FamilyTree.Api/Endpoints/Me/MeEndpoints.cs`
- Modify: `src/FamilyTree.Api/Program.cs` (register policies, map `/me`, seed on startup)
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/MeEndpointsTests.cs`, `Endpoints/AuthorizationTests.cs`

**Interfaces:**
- Consumes: `Permissions` (Task 4), `JwtTokenService.PermissionClaim` (Task 7), `ITenantContext`, `ApplicationDbContext`.
- Produces:
  - `PermissionRequirement(string Permission) : IAuthorizationRequirement`
  - `RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)`
  - `GET /api/v1/me` returning `CurrentUserResponse`, requiring `FamilyTree.View`.

- [ ] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/MeEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class MeEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    [Fact]
    public async Task Me_without_a_token_returns_401()
    {
        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_with_a_malformed_token_returns_401()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_seeded_super_admin_with_the_full_permission_set()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = (await response.Content.ReadFromJsonAsync<CurrentUserResponse>())!;

        me.Email.Should().Be(ApiFactory.AdminEmail);
        me.TenantId.Should().NotBeEmpty();
        me.FamilyTreeName.Should().Be("عائلة السقا");
        me.Permissions.Should().Contain("Member.Create").And.Contain("Role.Delete");
    }

    [Fact]
    public async Task Me_resolves_the_tenant_from_the_token_and_ignores_a_spoofed_header()
    {
        await AuthenticateAsync();
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.CreateVersion7().ToString());

        var response = await _client.GetAsync("/api/v1/me");
        var me = (await response.Content.ReadFromJsonAsync<CurrentUserResponse>())!;

        // The family tree name proves the tenant came from the token, not the header.
        me.FamilyTreeName.Should().Be("عائلة السقا");
    }
}
```

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/AuthorizationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Authorization;
using FamilyTree.Infrastructure.Auth;
using FamilyTree.Application.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class AuthorizationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>Mints a token directly so a permission set can be varied without seeding users.</summary>
    private string TokenWith(params string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        return tokens.CreateAccessToken(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "someone@example.com", permissions).Value;
    }

    [Fact]
    public async Task A_token_without_the_required_permission_returns_403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith(Permissions.Audit.View));

        var response = await _client.GetAsync("/api/v1/me");

        // Authenticated but not permitted — 403, distinct from the 401 of no token at all.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_with_no_permissions_at_all_returns_403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith());

        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_carrying_the_required_permission_is_admitted_by_the_policy()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith(Permissions.FamilyTree.View));

        var response = await _client.GetAsync("/api/v1/me");

        // The policy admits the request. The tenant in this hand-minted token owns no tree,
        // so the endpoint answers 404 — never 403, and never another tenant's data.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~Endpoints`
Expected: failure — `/api/v1/me` returns 404 for every case because it is not mapped.

- [ ] **Step 3: Write the authorization primitives**

Create `src/FamilyTree.Api/Authorization/PermissionRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace FamilyTree.Api.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
```

Create `src/FamilyTree.Api/Authorization/PermissionAuthorizationHandler.cs`:

```csharp
using FamilyTree.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;

namespace FamilyTree.Api.Authorization;

/// <summary>
/// Evaluates permission claims carried by the access token. One handler serves every
/// permission — adding a capability means adding a constant and a seed row, never a new handler.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var granted = context.User.FindAll(JwtTokenService.PermissionClaim)
            .Any(c => c.Value == requirement.Permission);

        if (granted) context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
```

Permissions ride in the token, so evaluation costs no database round trip. The tradeoff is that a permission change takes effect at the next token refresh — at most 15 minutes. Revoking a user outright is immediate, because refresh is rejected once the account is inactive.

Create `src/FamilyTree.Api/Authorization/EndpointExtensions.cs`:

```csharp
using FamilyTree.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace FamilyTree.Api.Authorization;

public static class EndpointExtensions
{
    /// <summary>Registers one policy per permission code, named after the code itself.</summary>
    public static AuthorizationBuilder AddPermissionPolicies(this AuthorizationBuilder builder)
    {
        foreach (var permission in Permissions.All)
            builder.AddPolicy(permission, policy => policy.AddRequirements(new PermissionRequirement(permission)));

        return builder;
    }

    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission) =>
        builder.RequireAuthorization(permission);
}
```

- [ ] **Step 4: Write the `/me` endpoint**

Create `src/FamilyTree.Api/Endpoints/Me/MeEndpoints.cs`:

```csharp
using FamilyTree.Api.Authorization;
using FamilyTree.Application.Common;
using FamilyTree.Contracts.Auth;
using FamilyTree.Domain.Authorization;
using FamilyTree.Infrastructure.Auth;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.Endpoints.Me;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me", async (
            ITenantContext tenant,
            ApplicationDbContext context,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Filtered query: a tenant with no tree of its own simply finds nothing.
            var tree = await context.FamilyTrees.FirstOrDefaultAsync(ct);
            if (tree is null) return Results.NotFound();

            var email = http.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;

            var permissions = http.User.FindAll(JwtTokenService.PermissionClaim)
                .Select(c => c.Value)
                .ToArray();

            return Results.Ok(new CurrentUserResponse(
                tenant.UserId, email, tenant.TenantId, tree.Name, permissions));
        })
        .RequirePermission(Permissions.FamilyTree.View)
        .WithTags("Me");

        return app;
    }
}
```

Returning 404 rather than 403 when the tree is absent is the uniform non-disclosure rule from spec §4.4.

- [ ] **Step 5: Wire policies, the handler, `/me`, and startup seeding into Program.cs**

In `src/FamilyTree.Api/Program.cs`, replace `builder.Services.AddAuthorization();` with:

```csharp
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorizationBuilder().AddPermissionPolicies();
```

Add the matching usings:

```csharp
using FamilyTree.Api.Authorization;
using FamilyTree.Api.Endpoints.Me;
using FamilyTree.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authorization;
```

After `app.MapAuthEndpoints();` add:

```csharp
app.MapMeEndpoints();

// Seeding is idempotent and runs on startup. Schema migration is NOT run here — per the
// technical specification §48, production schema changes belong to CI/CD, never to app startup.
//
// The Testing guard is load-bearing, not tidiness: WebApplicationFactory builds and starts the
// host the moment a test touches .Services, which happens BEFORE the test's own MigrateAsync().
// Without the guard, SeedAsync would query tables that do not exist yet on a fresh Testcontainers
// database and every ApiFactory-based test class would die at host startup. ApiFactory sets
// UseEnvironment("Testing") and seeds explicitly after migrating. Do not remove this.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}
```

- [ ] **Step 6: Run the full backend test suite**

Run: `dotnet test`
Expected: PASS — 69 tests (30 domain, 6 application, 33 integration).

- [ ] **Step 7: Verify the running API end to end by hand**

```bash
docker compose up -d postgres
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=familytree;Username=familytree;Password=devpassword"
export Jwt__Issuer="https://localhost:5001" Jwt__Audience="familytree-api"
export Jwt__SigningKey="local-dev-signing-key-at-least-32-bytes-long!!"
export Seed__TenantName="Al-Saqqa Family" Seed__TenantSlug="al-saqqa"
export Seed__FamilyTreeName="عائلة السقا"
export Seed__AdminEmail="admin@example.com" Seed__AdminPassword="Str0ng!Local#Password"

dotnet ef database update --project src/FamilyTree.Infrastructure --startup-project src/FamilyTree.Api
dotnet run --project src/FamilyTree.Api &

curl -s -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"Str0ng!Local#Password"}'
```

Expected: a JSON body containing `accessToken`, `expiresAt`, and `refreshToken`. Then call `/api/v1/me` with `Authorization: Bearer <accessToken>` and expect the Arabic family tree name in the response.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat: add permission policies, /me endpoint, and startup seeding"
```

---

### Task 11: React application shell with bilingual i18n and RTL

**Files:**
- Create: `frontend/` (Vite scaffold), `vite.config.ts`, `vitest.setup.ts`
- Create: `frontend/src/i18n/index.ts`, `i18n/locales/en.json`, `i18n/locales/ar.json`, `i18n/useDirection.ts`
- Create: `frontend/src/components/LanguageSwitcher.tsx`
- Create: `frontend/src/app/App.tsx`, `app/providers.tsx`, `src/main.tsx`
- Test: `frontend/src/i18n/useDirection.test.ts`, `src/components/LanguageSwitcher.test.tsx`, `src/i18n/locales.test.ts`

**Interfaces:**
- Consumes: nothing from the backend yet.
- Produces:
  - `i18n` — configured `i18next` instance, default language `ar`, fallback `en`.
  - `useDirection(): 'rtl' | 'ltr'` — derives direction from the active language and applies `dir`/`lang` to `<html>`.
  - `<LanguageSwitcher />` — toggles between `ar` and `en`.
  - `<Providers>` — wraps children in `QueryClientProvider`, `I18nextProvider`, and `BrowserRouter`.

- [ ] **Step 1: Scaffold the frontend**

```bash
npm create vite@latest frontend -- --template react-ts
cd frontend
npm install
npm install react-router-dom @tanstack/react-query i18next react-i18next i18next-browser-languagedetector
npm install -D vitest @testing-library/react @testing-library/user-event @testing-library/jest-dom jsdom
cd ..
```

- [ ] **Step 2: Configure Vitest**

Replace `frontend/vite.config.ts`:

```typescript
/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': { target: 'http://localhost:5000', changeOrigin: true },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
  },
})
```

Create `frontend/vitest.setup.ts`:

```typescript
import '@testing-library/jest-dom/vitest'
```

Add to `frontend/package.json` scripts: `"test": "vitest run"`, `"test:watch": "vitest"`.

- [ ] **Step 3: Write the failing i18n tests**

Create `frontend/src/i18n/locales.test.ts`:

```typescript
import { describe, expect, it } from 'vitest'
import ar from './locales/ar.json'
import en from './locales/en.json'

const flatten = (obj: Record<string, unknown>, prefix = ''): string[] =>
  Object.entries(obj).flatMap(([key, value]) =>
    typeof value === 'object' && value !== null
      ? flatten(value as Record<string, unknown>, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  )

describe('locale resources', () => {
  it('define exactly the same keys in both languages', () => {
    // A key present in one language and missing in the other renders as a raw key
    // in production. Catching it here is far cheaper than catching it in review.
    expect(flatten(ar).sort()).toEqual(flatten(en).sort())
  })

  it('leave no value blank', () => {
    const blanks = [
      ...Object.entries(ar).filter(([, v]) => v === ''),
      ...Object.entries(en).filter(([, v]) => v === ''),
    ]
    expect(blanks).toHaveLength(0)
  })
})
```

Create `frontend/src/i18n/useDirection.test.ts`:

```typescript
import { renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import i18n from './index'
import { useDirection } from './useDirection'

describe('useDirection', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('ar')
  })

  it('reports rtl for Arabic and stamps the document', () => {
    const { result } = renderHook(() => useDirection())

    expect(result.current).toBe('rtl')
    expect(document.documentElement.dir).toBe('rtl')
    expect(document.documentElement.lang).toBe('ar')
  })

  it('reports ltr for English and restamps the document', async () => {
    await i18n.changeLanguage('en')
    const { result } = renderHook(() => useDirection())

    expect(result.current).toBe('ltr')
    expect(document.documentElement.dir).toBe('ltr')
    expect(document.documentElement.lang).toBe('en')
  })
})
```

Create `frontend/src/components/LanguageSwitcher.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { I18nextProvider } from 'react-i18next'
import i18n from '../i18n'
import { LanguageSwitcher } from './LanguageSwitcher'

const renderSwitcher = () =>
  render(
    <I18nextProvider i18n={i18n}>
      <LanguageSwitcher />
    </I18nextProvider>,
  )

describe('LanguageSwitcher', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('ar')
  })

  it('offers the other language while Arabic is active', () => {
    renderSwitcher()
    expect(screen.getByRole('button', { name: 'English' })).toBeInTheDocument()
  })

  it('switches the active language and the document direction', async () => {
    renderSwitcher()

    await userEvent.click(screen.getByRole('button', { name: 'English' }))

    expect(i18n.language).toBe('en')
    expect(document.documentElement.dir).toBe('ltr')
    expect(screen.getByRole('button', { name: 'العربية' })).toBeInTheDocument()
  })
})
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `cd frontend && npm test`
Expected: failure — the `i18n`, `useDirection`, and `LanguageSwitcher` modules do not exist.

- [ ] **Step 5: Write the locale resources**

Create `frontend/src/i18n/locales/en.json`:

```json
{
  "app": { "title": "Family Tree" },
  "language": { "ar": "العربية", "en": "English" },
  "auth": {
    "signIn": "Sign in",
    "email": "Email",
    "password": "Password",
    "signOut": "Sign out",
    "signingIn": "Signing in…"
  },
  "errors": {
    "INVALID_CREDENTIALS": "The email or password is incorrect.",
    "ACCOUNT_INACTIVE": "This account has been deactivated.",
    "TENANT_INACTIVE": "This account's subscription is inactive.",
    "VALIDATION_FAILED": "Please check the highlighted fields.",
    "NETWORK": "Could not reach the server. Please try again.",
    "UNKNOWN": "Something went wrong. Please try again."
  }
}
```

Create `frontend/src/i18n/locales/ar.json`:

```json
{
  "app": { "title": "شجرة العائلة" },
  "language": { "ar": "العربية", "en": "English" },
  "auth": {
    "signIn": "تسجيل الدخول",
    "email": "البريد الإلكتروني",
    "password": "كلمة المرور",
    "signOut": "تسجيل الخروج",
    "signingIn": "جارٍ تسجيل الدخول…"
  },
  "errors": {
    "INVALID_CREDENTIALS": "البريد الإلكتروني أو كلمة المرور غير صحيحة.",
    "ACCOUNT_INACTIVE": "تم تعطيل هذا الحساب.",
    "TENANT_INACTIVE": "اشتراك هذا الحساب غير مفعّل.",
    "VALIDATION_FAILED": "يرجى مراجعة الحقول المحددة.",
    "NETWORK": "تعذّر الوصول إلى الخادم. يرجى المحاولة مرة أخرى.",
    "UNKNOWN": "حدث خطأ ما. يرجى المحاولة مرة أخرى."
  }
}
```

The `errors` keys are the backend's stable `code` values — this is the translation layer spec §4.8 requires, and the reason message text is never part of the API contract.

- [ ] **Step 6: Write the i18n instance and direction hook**

Create `frontend/src/i18n/index.ts`:

```typescript
import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import ar from './locales/ar.json'
import en from './locales/en.json'

export const SUPPORTED_LANGUAGES = ['ar', 'en'] as const
export type Language = (typeof SUPPORTED_LANGUAGES)[number]

void i18n.use(initReactI18next).init({
  resources: {
    ar: { translation: ar },
    en: { translation: en },
  },
  lng: 'ar',
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
})

export default i18n
```

Arabic is the default because the family this is built for is Arabic-speaking. English is the fallback so a missing key degrades to readable text rather than a raw key.

Create `frontend/src/i18n/useDirection.ts`:

```typescript
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import type { Language } from './index'

export type Direction = 'rtl' | 'ltr'

export const directionFor = (language: string): Direction =>
  language.startsWith('ar') ? 'rtl' : 'ltr'

/**
 * Keeps <html dir> and <html lang> in step with the active language.
 * Layout follows the document direction, so no component needs its own RTL branch.
 */
export const useDirection = (): Direction => {
  const { i18n } = useTranslation()
  const direction = directionFor(i18n.language as Language)

  useEffect(() => {
    document.documentElement.dir = direction
    document.documentElement.lang = i18n.language
  }, [direction, i18n.language])

  return direction
}
```

- [ ] **Step 7: Write the language switcher and app shell**

Create `frontend/src/components/LanguageSwitcher.tsx`:

```tsx
import { useTranslation } from 'react-i18next'
import { useDirection } from '../i18n/useDirection'

export const LanguageSwitcher = () => {
  const { i18n, t } = useTranslation()
  useDirection()

  const next = i18n.language.startsWith('ar') ? 'en' : 'ar'

  return (
    <button type="button" onClick={() => void i18n.changeLanguage(next)}>
      {t(`language.${next}`)}
    </button>
  )
}
```

Create `frontend/src/app/providers.tsx`:

```tsx
import type { PropsWithChildren } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { I18nextProvider } from 'react-i18next'
import { BrowserRouter } from 'react-router-dom'
import i18n from '../i18n'

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
})

export const Providers = ({ children }: PropsWithChildren) => (
  <I18nextProvider i18n={i18n}>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{children}</BrowserRouter>
    </QueryClientProvider>
  </I18nextProvider>
)
```

Create `frontend/src/app/App.tsx`:

```tsx
import { useTranslation } from 'react-i18next'
import { LanguageSwitcher } from '../components/LanguageSwitcher'
import { useDirection } from '../i18n/useDirection'

export const App = () => {
  const { t } = useTranslation()
  useDirection()

  return (
    <>
      <header>
        <h1>{t('app.title')}</h1>
        <LanguageSwitcher />
      </header>
      <main />
    </>
  )
}
```

Replace `frontend/src/main.tsx`:

```tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './app/App'
import { Providers } from './app/providers'
import './index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Providers>
      <App />
    </Providers>
  </StrictMode>,
)
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `cd frontend && npm test`
Expected: PASS — 6 tests.

- [ ] **Step 9: Verify the app renders in Arabic and flips to English**

```bash
cd frontend && npm run dev
```

Open the printed URL. Expected: the heading reads شجرة العائلة, the page is right-aligned, and clicking `English` flips the layout to left-aligned with the heading `Family Tree`.

- [ ] **Step 10: Commit**

```bash
git add frontend
git commit -m "feat: add React shell with bilingual i18n and RTL support"
```

---

### Task 12: Frontend authentication

**Files:**
- Create: `frontend/src/services/apiClient.ts`, `services/tokenStorage.ts`
- Create: `frontend/src/features/auth/AuthContext.tsx`, `auth/LoginPage.tsx`, `auth/useLogin.ts`
- Create: `frontend/src/routes/ProtectedRoute.tsx`, `routes/AppRoutes.tsx`
- Modify: `frontend/src/app/App.tsx`, `app/providers.tsx`
- Test: `frontend/src/services/apiClient.test.ts`, `features/auth/LoginPage.test.tsx`, `routes/ProtectedRoute.test.tsx`

**Interfaces:**
- Consumes: `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh`, `POST /api/v1/auth/logout`, `GET /api/v1/me`.
- Produces:
  - `tokenStorage` — `read(): Tokens | null`, `write(tokens: Tokens): void`, `clear(): void`.
  - `apiFetch<T>(path: string, init?: RequestInit): Promise<T>` — attaches the bearer token, retries once through refresh on 401, throws `ApiError` carrying `code` and `status`.
  - `useAuth(): { user: CurrentUser | null; isLoading: boolean; login(email, password): Promise<void>; logout(): Promise<void> }`
  - `<ProtectedRoute>` — renders children when authenticated, otherwise redirects to `/login`.

- [ ] **Step 1: Write the failing API client tests**

Create `frontend/src/services/apiClient.test.ts`:

```typescript
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiFetch } from './apiClient'
import { tokenStorage } from './tokenStorage'

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

describe('apiFetch', () => {
  beforeEach(() => {
    tokenStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('attaches the bearer token when one is stored', async () => {
    tokenStorage.write({ accessToken: 'token-abc', refreshToken: 'refresh-abc' })
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await apiFetch('/api/v1/me')

    const headers = new Headers(fetchMock.mock.calls[0][1].headers)
    expect(headers.get('Authorization')).toBe('Bearer token-abc')
  })

  it('sends no Authorization header when no token is stored', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await apiFetch('/api/v1/me')

    const headers = new Headers(fetchMock.mock.calls[0][1].headers)
    expect(headers.get('Authorization')).toBeNull()
  })

  it('throws an ApiError carrying the backend code', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ code: 'INVALID_CREDENTIALS', title: 'Authentication failed.' }, 401),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch('/api/v1/auth/login', { method: 'POST' })).rejects.toMatchObject({
      code: 'INVALID_CREDENTIALS',
      status: 401,
    })
  })

  it('refreshes once on 401 and replays the original request', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'refresh-abc' })

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ code: 'UNAUTHORIZED' }, 401))
      .mockResolvedValueOnce(
        jsonResponse({ accessToken: 'fresh', expiresAt: new Date().toISOString(), refreshToken: 'refresh-def' }),
      )
      .mockResolvedValueOnce(jsonResponse({ email: 'admin@example.com' }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await apiFetch<{ email: string }>('/api/v1/me')

    expect(result.email).toBe('admin@example.com')
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(tokenStorage.read()?.accessToken).toBe('fresh')
  })

  it('clears the stored tokens and gives up when the refresh itself fails', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'stale' })

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ code: 'UNAUTHORIZED' }, 401))
      .mockResolvedValueOnce(jsonResponse({ code: 'INVALID_REFRESH_TOKEN' }, 401))
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch('/api/v1/me')).rejects.toBeInstanceOf(ApiError)
    expect(tokenStorage.read()).toBeNull()
  })

  it('does not attempt a refresh loop on the refresh endpoint itself', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'stale' })
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ code: 'INVALID_REFRESH_TOKEN' }, 401))
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch('/api/v1/auth/refresh', { method: 'POST' })).rejects.toBeInstanceOf(ApiError)
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npm test -- apiClient`
Expected: failure — `apiClient` and `tokenStorage` do not exist.

- [ ] **Step 3: Write token storage and the API client**

Create `frontend/src/services/tokenStorage.ts`:

```typescript
export interface Tokens {
  accessToken: string
  refreshToken: string
}

const STORAGE_KEY = 'familytree.tokens'

/**
 * sessionStorage rather than localStorage: tokens die with the tab, which limits the
 * blast radius on a shared machine. Hardening this further (httpOnly cookies) is a
 * Phase 7 decision that would change the API surface, so it is not made here.
 */
export const tokenStorage = {
  read(): Tokens | null {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    try {
      return JSON.parse(raw) as Tokens
    } catch {
      return null
    }
  },

  write(tokens: Tokens): void {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tokens))
  },

  clear(): void {
    sessionStorage.removeItem(STORAGE_KEY)
  },
}
```

Create `frontend/src/services/apiClient.ts`:

```typescript
import { tokenStorage } from './tokenStorage'

export class ApiError extends Error {
  constructor(
    readonly code: string,
    readonly status: number,
  ) {
    super(code)
    this.name = 'ApiError'
  }
}

const REFRESH_PATH = '/api/v1/auth/refresh'

const withAuth = (init: RequestInit, accessToken?: string): RequestInit => {
  const headers = new Headers(init.headers)
  headers.set('Content-Type', 'application/json')
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  return { ...init, headers }
}

const errorFrom = async (response: Response): Promise<ApiError> => {
  try {
    const body = (await response.json()) as { code?: string }
    return new ApiError(body.code ?? 'UNKNOWN', response.status)
  } catch {
    return new ApiError('UNKNOWN', response.status)
  }
}

const tryRefresh = async (): Promise<boolean> => {
  const tokens = tokenStorage.read()
  if (!tokens?.refreshToken) return false

  const response = await fetch(REFRESH_PATH, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: tokens.refreshToken }),
  })

  if (!response.ok) {
    tokenStorage.clear()
    return false
  }

  const body = (await response.json()) as { accessToken: string; refreshToken: string }
  tokenStorage.write({ accessToken: body.accessToken, refreshToken: body.refreshToken })
  return true
}

/**
 * Single entry point for every API call. On 401 it refreshes once and replays the request;
 * a second failure surfaces to the caller. The refresh endpoint is excluded so a stale
 * refresh token cannot start a loop.
 */
export const apiFetch = async <T>(path: string, init: RequestInit = {}): Promise<T> => {
  const attempt = async (): Promise<Response> =>
    fetch(path, withAuth(init, tokenStorage.read()?.accessToken))

  let response = await attempt()

  if (response.status === 401 && path !== REFRESH_PATH) {
    if (await tryRefresh()) {
      response = await attempt()
    }
  }

  if (!response.ok) throw await errorFrom(response)

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}
```

- [ ] **Step 4: Write the failing auth UI tests**

Create `frontend/src/features/auth/LoginPage.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Providers } from '../../app/providers'
import i18n from '../../i18n'
import { tokenStorage } from '../../services/tokenStorage'
import { LoginPage } from './LoginPage'

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const renderPage = () => render(<Providers><LoginPage /></Providers>)

describe('LoginPage', () => {
  beforeEach(async () => {
    tokenStorage.clear()
    await i18n.changeLanguage('en')
  })

  afterEach(() => vi.restoreAllMocks())

  it('stores the tokens returned by a successful sign-in', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({ accessToken: 'abc', expiresAt: new Date().toISOString(), refreshToken: 'def' }),
    ))

    renderPage()
    await userEvent.type(screen.getByLabelText('Email'), 'admin@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'Str0ng!Password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(tokenStorage.read()?.accessToken).toBe('abc'))
  })

  it('translates the backend error code rather than showing it raw', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({ code: 'INVALID_CREDENTIALS' }, 401),
    ))

    renderPage()
    await userEvent.type(screen.getByLabelText('Email'), 'admin@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('The email or password is incorrect.')
    expect(tokenStorage.read()).toBeNull()
  })

  it('shows the Arabic message for the same code when Arabic is active', async () => {
    await i18n.changeLanguage('ar')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({ code: 'INVALID_CREDENTIALS' }, 401),
    ))

    renderPage()
    await userEvent.type(screen.getByLabelText('البريد الإلكتروني'), 'admin@example.com')
    await userEvent.type(screen.getByLabelText('كلمة المرور'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'تسجيل الدخول' }))

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('البريد الإلكتروني أو كلمة المرور غير صحيحة.')
  })
})
```

Create `frontend/src/routes/ProtectedRoute.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router-dom'
import { Providers } from '../app/providers'
import { tokenStorage } from '../services/tokenStorage'
import { ProtectedRoute } from './ProtectedRoute'

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const renderRoutes = () =>
  render(
    <Providers>
      <Routes>
        <Route path="/login" element={<p>login screen</p>} />
        <Route path="/" element={<ProtectedRoute><p>protected content</p></ProtectedRoute>} />
      </Routes>
    </Providers>,
  )

describe('ProtectedRoute', () => {
  beforeEach(() => tokenStorage.clear())
  afterEach(() => vi.restoreAllMocks())

  it('redirects to the login screen when no token is stored', async () => {
    renderRoutes()

    expect(await screen.findByText('login screen')).toBeInTheDocument()
  })

  it('renders the protected content once the session resolves', async () => {
    tokenStorage.write({ accessToken: 'abc', refreshToken: 'def' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({
        id: '0193...', email: 'admin@example.com', tenantId: '0193...',
        familyTreeName: 'عائلة السقا', permissions: ['FamilyTree.View'],
      }),
    ))

    renderRoutes()

    await waitFor(() => expect(screen.getByText('protected content')).toBeInTheDocument())
  })

  it('redirects to login when the stored token is rejected', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'stale' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'UNAUTHORIZED' }, 401)))

    renderRoutes()

    expect(await screen.findByText('login screen')).toBeInTheDocument()
  })
})
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `cd frontend && npm test`
Expected: failure — `LoginPage`, `ProtectedRoute`, and `AuthContext` do not exist.

- [ ] **Step 6: Write the auth context**

Create `frontend/src/features/auth/AuthContext.tsx`:

```tsx
import { createContext, useCallback, useContext, useMemo, type PropsWithChildren } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '../../services/apiClient'
import { tokenStorage } from '../../services/tokenStorage'

export interface CurrentUser {
  id: string
  email: string
  tenantId: string
  familyTreeName: string
  permissions: string[]
}

interface AuthContextValue {
  user: CurrentUser | null
  isLoading: boolean
  hasPermission: (permission: string) => boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export const AuthProvider = ({ children }: PropsWithChildren) => {
  const queryClient = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['me'],
    queryFn: () => apiFetch<CurrentUser>('/api/v1/me'),
    enabled: tokenStorage.read() !== null,
    retry: false,
  })

  const loginMutation = useMutation({
    mutationFn: async ({ email, password }: { email: string; password: string }) => {
      const tokens = await apiFetch<{ accessToken: string; refreshToken: string }>(
        '/api/v1/auth/login',
        { method: 'POST', body: JSON.stringify({ email, password }) },
      )
      tokenStorage.write(tokens)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['me'] }),
  })

  const login = useCallback(
    async (email: string, password: string) => {
      await loginMutation.mutateAsync({ email, password })
    },
    [loginMutation],
  )

  const logout = useCallback(async () => {
    const tokens = tokenStorage.read()
    if (tokens) {
      // Best-effort revocation; the local session ends either way.
      await apiFetch('/api/v1/auth/logout', {
        method: 'POST',
        body: JSON.stringify({ refreshToken: tokens.refreshToken }),
      }).catch(() => undefined)
    }
    tokenStorage.clear()
    queryClient.clear()
  }, [queryClient])

  const value = useMemo<AuthContextValue>(
    () => ({
      user: data ?? null,
      isLoading: tokenStorage.read() !== null && isLoading,
      hasPermission: (permission) => data?.permissions.includes(permission) ?? false,
      login,
      logout,
    }),
    [data, isLoading, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export const useAuth = (): AuthContextValue => {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
```

`hasPermission` drives which actions the UI offers. The server enforces the same permissions independently — the client copy is convenience, never the control.

- [ ] **Step 7: Write the login page and protected route**

Create `frontend/src/features/auth/LoginPage.tsx`:

```tsx
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../services/apiClient'
import { useAuth } from './AuthContext'

export const LoginPage = () => {
  const { t } = useTranslation()
  const { login } = useAuth()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setErrorCode(null)
    setSubmitting(true)
    try {
      await login(email, password)
    } catch (error) {
      // Translate the stable code; never render a server-supplied string.
      setErrorCode(error instanceof ApiError ? error.code : 'NETWORK')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={onSubmit}>
      <h1>{t('auth.signIn')}</h1>

      <label htmlFor="email">{t('auth.email')}</label>
      <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />

      <label htmlFor="password">{t('auth.password')}</label>
      <input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />

      {errorCode && <p role="alert">{t(`errors.${errorCode}`, { defaultValue: t('errors.UNKNOWN') })}</p>}

      <button type="submit" disabled={submitting}>
        {submitting ? t('auth.signingIn') : t('auth.signIn')}
      </button>
    </form>
  )
}
```

Create `frontend/src/routes/ProtectedRoute.tsx`:

```tsx
import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../features/auth/AuthContext'

export const ProtectedRoute = ({ children }: PropsWithChildren) => {
  const { user, isLoading } = useAuth()

  if (isLoading) return null
  if (!user) return <Navigate to="/login" replace />

  return <>{children}</>
}
```

Create `frontend/src/routes/AppRoutes.tsx`:

```tsx
import { Navigate, Route, Routes } from 'react-router-dom'
import { LoginPage } from '../features/auth/LoginPage'
import { ProtectedRoute } from './ProtectedRoute'

/** Phase 2 replaces the placeholder dashboard with the family tree page. */
const Dashboard = () => <p>Dashboard</p>

export const AppRoutes = () => (
  <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route path="/" element={<ProtectedRoute><Dashboard /></ProtectedRoute>} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes>
)
```

- [ ] **Step 8: Wire the provider and routes into the shell**

In `frontend/src/app/providers.tsx`, wrap `children` with `AuthProvider` **inside** `BrowserRouter` (it uses query hooks and must sit under `QueryClientProvider`):

```tsx
<BrowserRouter>
  <AuthProvider>{children}</AuthProvider>
</BrowserRouter>
```

Add the import: `import { AuthProvider } from '../features/auth/AuthContext'`.

In `frontend/src/app/App.tsx`, replace `<main />` with `<main><AppRoutes /></main>` and import `AppRoutes` from `../routes/AppRoutes`.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `cd frontend && npm test`
Expected: PASS — 18 tests.

- [ ] **Step 10: Commit**

```bash
git add frontend
git commit -m "feat: add frontend authentication with token refresh and protected routes"
```

---

### Task 13: Containerization and continuous integration

**Files:**
- Create: `src/FamilyTree.Api/Dockerfile`, `frontend/Dockerfile`, `frontend/nginx.conf`
- Modify: `docker-compose.yml`
- Create: `.github/workflows/ci.yml`
- Create: `README.md`

**Interfaces:**
- Consumes: everything built in Tasks 1–12.
- Produces: `docker compose up` serving the SPA on `http://localhost:8080` against the API on `http://localhost:5000`, and a CI pipeline that builds, tests, and fails on any failure.

- [ ] **Step 1: Write the API Dockerfile**

Create `src/FamilyTree.Api/Dockerfile` (build context is the repository root):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/ ./src/
RUN dotnet publish src/FamilyTree.Api/FamilyTree.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Runs unprivileged: a container compromise should not also be a root compromise.
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "FamilyTree.Api.dll"]
```

- [ ] **Step 2: Write the frontend Dockerfile**

Create `frontend/Dockerfile`:

```dockerfile
FROM node:24-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

Create `frontend/nginx.conf`:

```nginx
server {
    listen 80;
    server_name _;
    root /usr/share/nginx/html;

    # SPA history fallback: every unknown path serves index.html so client routing works.
    location / {
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://api:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

- [ ] **Step 3: Extend docker-compose with the api and frontend services**

Replace `docker-compose.yml`:

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

  api:
    build:
      context: .
      dockerfile: src/FamilyTree.Api/Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__DefaultConnection: "Host=postgres;Port=5432;Database=familytree;Username=familytree;Password=${POSTGRES_PASSWORD:-devpassword}"
      Jwt__Issuer: ${JWT_ISSUER:-http://localhost:5000}
      Jwt__Audience: ${JWT_AUDIENCE:-familytree-api}
      Jwt__SigningKey: ${JWT_SIGNING_KEY:?JWT_SIGNING_KEY must be set}
      Seed__TenantName: ${SEED_TENANT_NAME:-Al-Saqqa Family}
      Seed__TenantSlug: ${SEED_TENANT_SLUG:-al-saqqa}
      Seed__FamilyTreeName: ${SEED_FAMILY_TREE_NAME:-عائلة السقا}
      Seed__AdminEmail: ${SEED_ADMIN_EMAIL:?SEED_ADMIN_EMAIL must be set}
      Seed__AdminPassword: ${SEED_ADMIN_PASSWORD:?SEED_ADMIN_PASSWORD must be set}
    ports:
      - "5000:8080"
    depends_on:
      postgres:
        condition: service_healthy

  frontend:
    build:
      context: ./frontend
    ports:
      - "8080:80"
    depends_on:
      - api

volumes:
  pgdata:
```

`${VAR:?message}` makes compose refuse to start when a secret is missing, instead of quietly booting with an empty signing key.

Update `.env.example` to list the compose variables:

```bash
POSTGRES_PASSWORD=change-me-locally
JWT_ISSUER=http://localhost:5000
JWT_AUDIENCE=familytree-api
JWT_SIGNING_KEY=generate-a-32-byte-random-value-do-not-commit-a-real-one
SEED_TENANT_NAME=Al-Saqqa Family
SEED_TENANT_SLUG=al-saqqa
SEED_FAMILY_TREE_NAME=عائلة السقا
SEED_ADMIN_EMAIL=admin@example.com
SEED_ADMIN_PASSWORD=choose-a-strong-password
```

- [ ] **Step 4: Add the schema migration step to compose startup**

The API does not migrate on startup (technical spec §48). For local use, apply migrations before bringing the stack up:

```bash
docker compose up -d postgres
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=familytree;Username=familytree;Password=devpassword"
dotnet ef database update --project src/FamilyTree.Infrastructure --startup-project src/FamilyTree.Api
docker compose up -d
```

Document this in the README as the required order. Automating migration deployment belongs to Phase 7.

- [ ] **Step 5: Write the CI workflow**

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - run: dotnet restore
      - run: dotnet build --no-restore --configuration Release

      # Integration tests start their own PostgreSQL via Testcontainers, which needs
      # the Docker daemon the ubuntu-latest runner already provides.
      - run: dotnet test --no-build --configuration Release --verbosity normal

  frontend:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: frontend
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-node@v4
        with:
          node-version: '24'
          cache: npm
          cache-dependency-path: frontend/package-lock.json

      - run: npm ci
      - run: npx tsc --noEmit
      - run: npm test
      - run: npm run build

  docker:
    runs-on: ubuntu-latest
    needs: [backend, frontend]
    steps:
      - uses: actions/checkout@v4
      - run: docker build -f src/FamilyTree.Api/Dockerfile -t familytree-api .
      - run: docker build -f frontend/Dockerfile -t familytree-frontend ./frontend
```

- [ ] **Step 6: Write the README**

Create `README.md`:

````markdown
# Family Tree SaaS

Multi-tenant family tree platform. Arabic/English, RTL-first.

- **Design spec:** `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md`
- **Current phase:** Phase 1 — Foundation

## Requirements

.NET 10 SDK, Node 24, Docker.

## Running locally

```bash
cp .env.example .env    # then edit: set JWT_SIGNING_KEY and SEED_ADMIN_PASSWORD
docker compose up -d postgres

export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=familytree;Username=familytree;Password=devpassword"
dotnet ef database update --project src/FamilyTree.Infrastructure --startup-project src/FamilyTree.Api

docker compose up -d
```

The SPA is on http://localhost:8080 and the API on http://localhost:5000.

Migrations are applied deliberately, never on application startup — production schema
changes belong to the deployment pipeline.

## Tests

```bash
dotnet test                    # unit + integration (integration needs Docker running)
cd frontend && npm test        # component tests
```

## Architecture

Modular monolith. Dependencies point inward: `Domain` → nothing, `Application` → `Domain`,
`Infrastructure` → `Application`, `Api` → all. Tenant isolation is enforced in three layers —
EF global query filters, service-level ownership assertions, and database constraints.
````

- [ ] **Step 7: Verify the full stack runs**

```bash
docker compose build
docker compose up -d
curl -s http://localhost:5000/health
```

Expected: `{"status":"ok"}`. Then open http://localhost:8080, sign in with the seeded
credentials, and confirm the dashboard renders and the language switcher flips direction.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "chore: add containerization, CI pipeline, and README"
```

---

## Phase 1 exit criteria

Phase 1 is complete when all of the following hold:

1. `dotnet test` passes — 69 backend tests, integration tests running against real PostgreSQL.
2. `cd frontend && npm test` passes — 18 component tests.
3. `docker compose up` serves the SPA and API, and the seeded administrator can sign in.
4. A cross-tenant query returns nothing, proven by `TenantIsolationTests`, not by inspection.
5. `GET /api/v1/me` returns 401 without a token, 403 with an insufficient token, and 200 with the seeded administrator's token.
6. The UI renders correctly in Arabic RTL and switches to English LTR.
7. `dotnet build` produces zero warnings, with `TreatWarningsAsErrors` on.
8. No secret appears anywhere in git history.

## What Phase 1 deliberately does not deliver

No family members, no tree visualization, no user or role management screens, no audit log,
no public links. Those are Phases 2 through 6 and get their own plans. Phase 1's job is to
make every one of them safe to build: tenant isolation proven, permissions enforced, and the
test harness that will verify all of it already running against real PostgreSQL.

## Test-tooling note

The `dotnet new xunit` template on .NET 10 scaffolds **xUnit v3**, whose `IAsyncLifetime`
returns `ValueTask` — which is what the fixtures in this plan use. If the scaffolded project
turns out to be xUnit v2, change the four `ValueTask` lifetime signatures to `Task` and
`ValueTask.CompletedTask` to `Task.CompletedTask`. Nothing else in the plan depends on the
version.
