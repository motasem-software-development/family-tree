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

*Tasks 4–12 are appended in the sections that follow.*
