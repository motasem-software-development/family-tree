# Phase 4 — Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the user and role management surface on top of the permission model Phase 1 already delivered, plus a server-enforced forced-password-change flow and a guard that prevents a tenant from removing its own last administrator.

**Architecture:** Nothing in the permission model changes. `Permissions`, `PermissionResolver`, `PermissionAuthorizationHandler`, `RequirePermission`, and the four seeded system roles all stay exactly as they are. Phase 4 adds two application services (`IUserService`, `IRoleService`), eleven endpoints, one middleware, one guard, and two frontend feature folders — each following the shape already established by `IFamilyMemberService` / `FamilyMemberEndpoints` / `features/members/`.

**Tech Stack:** .NET 10, EF Core + Npgsql, ASP.NET Core Identity (password hashing only — Identity roles are unused), minimal APIs, xUnit + FluentAssertions + Testcontainers, React 19 + TypeScript, TanStack Query 5, vitest 4 + Testing Library, i18next.

**Spec:** `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md` — especially §4.2 (authentication), §4.3 (authorization), §4.4 (cross-tenant 404), §4.8 (errors), and **§4.9 (user and role management)**, written for this phase and binding on every decision below.

---

## Global Constraints

Copied from the spec. Every task's requirements implicitly include this section.

- **Authorization is permission-based, never role-name-based** (§4.3). No code may branch on a role's `Name`. This includes the lockout guard.
- **Cross-tenant ids return 404, not 403** (§4.4) — uniformly across members, trees, **users, roles**, audit records, and public links. A caller must not be able to distinguish "no such id here" from "no such id anywhere".
- **Errors are RFC 7807 Problem Details with a stable `code` extension** (§4.8). Message text is never the contract. Every new code needs an `ar` and an `en` entry in `frontend/src/i18n/locales/`.
- **No business rule may be enforced only in the frontend** (§9). The password gate and the lockout guard are server-side rules; the UI reflects them, it does not implement them.
- **Tenant isolation is not optional.** Queries run through the EF global query filter. `IgnoreQueryFilters()` is permitted only where the tenant is stated explicitly in the predicate, with a comment saying why (see `PermissionResolver` and `AuthService` for the two existing precedents).
- **Arabic is the default language and the UI is RTL-first.** Never invent an Arabic string: read the existing value out of `frontend/src/i18n/locales/ar.json` before asserting on it.
- **Audit writes are out of scope** — deferred to Phase 5, which owns the audit schema (§4.9).
- **Services take no `tenantId` parameter** (§2.3). The tenant comes from injected `ITenantContext`.
- **Folder organization is feature-first within every layer** (§2.1).

### Known hazards

- **A running dev API locks the build output.** If `dotnet build` fails with MSB3021/MSB3027, a `FamilyTree.Api` process is holding the DLLs. Find and stop it, then rebuild. This has bitten every prior phase.
- **`ApiFactory.ResetAndSeedAsync()` seeds the imported 349-member family before every integration test.** For users and roles the baseline is one seeded user and four seeded roles — **not zero**.
- **The seeded admin is `admin@example.com` holding Super Admin (all 18 permissions).** It is the tenant's only user, which makes it the last administrator — several tasks depend on that.

---

## File Structure

**Backend — create:**

| File | Responsibility |
|---|---|
| `src/FamilyTree.Contracts/Users/*.cs` | User request/response DTOs |
| `src/FamilyTree.Contracts/Roles/*.cs` | Role and permission-catalog DTOs |
| `src/FamilyTree.Application/Users/IUserService.cs` | User use-case interface |
| `src/FamilyTree.Application/Users/IAdministratorGuard.cs` | Lockout-guard interface |
| `src/FamilyTree.Application/Roles/IRoleService.cs` | Role use-case interface |
| `src/FamilyTree.Infrastructure/Users/UserService.cs` | User implementation |
| `src/FamilyTree.Infrastructure/Users/AdministratorGuard.cs` | The last-administrator rule, in one place |
| `src/FamilyTree.Infrastructure/Roles/RoleService.cs` | Role implementation |
| `src/FamilyTree.Api/Endpoints/Users/UserEndpoints.cs` | User routes |
| `src/FamilyTree.Api/Endpoints/Roles/RoleEndpoints.cs` | Role and permission-catalog routes |
| `src/FamilyTree.Api/Authorization/PasswordChangeGateMiddleware.cs` | Blocks all but two routes while `MustChangePassword` is set |

**Backend — modify:** `ApplicationUser.cs`, `AuthClaims.cs`, `ITokenService.cs`, `JwtTokenService.cs`, `AuthService.cs`, `MeEndpoints.cs`, `CurrentUserResponse.cs`, `Program.cs`, plus one EF migration.

**Frontend — create:** `features/users/` (`types.ts`, `usersApi.ts`, `useUsers.ts`, `useRoleOptions.ts`, `UserForm.tsx`, `UsersPage.tsx` + test), `features/roles/` (same shape), `features/auth/ChangePasswordPage.tsx`.

**Frontend — modify:** `AppRoutes.tsx`, `ProtectedRoute.tsx`, `AppShell.tsx`, `AuthContext.tsx`, both locale files.

---

## Task 1: `MustChangePassword` flag, migration, and claim

**Files:**
- Modify: `src/FamilyTree.Infrastructure/Identity/ApplicationUser.cs`, `src/FamilyTree.Application/Auth/AuthClaims.cs`, `src/FamilyTree.Application/Auth/ITokenService.cs`, `src/FamilyTree.Infrastructure/Auth/JwtTokenService.cs`, `src/FamilyTree.Infrastructure/Auth/AuthService.cs`
- Create: migration `src/FamilyTree.Infrastructure/Persistence/Migrations/*_AddMustChangePassword.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Auth/TokenClaimsTests.cs`

**Interfaces:**
- Produces: `ApplicationUser.MustChangePassword` (`bool`, default `false`); `AuthClaims.MustChangePassword` = `"must_change_password"`; `JwtTokenService.MustChangePasswordClaim`; `ITokenService.CreateAccessToken(Guid userId, Guid tenantId, string email, IReadOnlyCollection<string> permissions, bool mustChangePassword)`.

- [ ] **Step 1: Add the flag to the entity**

In `ApplicationUser.cs`, after `IsActive`:

```csharp
    /// <summary>
    /// Set when an administrator chooses the password (create or reset). While set, the access
    /// token carries a claim that blocks every route but GET /me and POST /me/password
    /// (design spec §4.9). Self-service change clears it.
    /// </summary>
    public bool MustChangePassword { get; set; }
```

- [ ] **Step 2: Add the claim name**

In `AuthClaims.cs`, inside the class:

```csharp
    public const string MustChangePassword = "must_change_password";
```

- [ ] **Step 3: Emit the claim**

In `ITokenService.cs`:

```csharp
    AccessToken CreateAccessToken(
        Guid userId, Guid tenantId, string email,
        IReadOnlyCollection<string> permissions, bool mustChangePassword);
```

In `JwtTokenService.cs`, add the constant beside the other two:

```csharp
    public const string MustChangePasswordClaim = AuthClaims.MustChangePassword;
```

Change the method signature to match, and after `claims.AddRange(...)`:

```csharp
        // Emitted only when true. An absent claim and a "false" claim would both have to be
        // handled by the gate middleware; emitting one shape means the gate has one branch.
        if (mustChangePassword)
            claims.Add(new Claim(MustChangePasswordClaim, "true"));
```

In `AuthService.IssueTokensAsync`:

```csharp
        var access = tokenService.CreateAccessToken(
            user.Id, user.TenantId, user.Email!, permissions, user.MustChangePassword);
```

- [ ] **Step 4: Create the migration**

```bash
dotnet ef migrations add AddMustChangePassword --project src/FamilyTree.Infrastructure --startup-project src/FamilyTree.Api
```

Open the generated file and confirm it contains exactly one `AddColumn<bool>` on `AspNetUsers` with `defaultValue: false` and nothing else. If it contains unrelated changes, the model snapshot had drifted — stop and report rather than editing the migration by hand.

- [ ] **Step 5: Write the test**

Create `tests/FamilyTree.Api.IntegrationTests/Auth/TokenClaimsTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.Auth;
using FamilyTree.Contracts.Auth;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Auth;

[Collection("postgres")]
public sealed class TokenClaimsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<JwtSecurityToken> LoginAndReadTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        return new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
    }

    [Fact]
    public async Task The_seeded_admin_token_carries_no_must_change_password_claim()
    {
        var token = await LoginAndReadTokenAsync();

        // The seeded admin's password came from configuration, not from another administrator,
        // so it is not temporary and must not be forced to change.
        token.Claims.Should().NotContain(c => c.Type == AuthClaims.MustChangePassword);
    }

    [Fact]
    public async Task A_user_flagged_for_password_change_gets_the_claim()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.IgnoreQueryFilters()
                .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
            user.MustChangePassword = true;
            await context.SaveChangesAsync();
        }

        var token = await LoginAndReadTokenAsync();

        token.Claims.Should().ContainSingle(c => c.Type == AuthClaims.MustChangePassword)
            .Which.Value.Should().Be("true");
    }
}
```

- [ ] **Step 6: Run the tests**

```bash
dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~TokenClaimsTests
```

Expected: both PASS. This task is flag-plumbing, so the test proves the plumbing reaches the wire rather than driving a red-then-green cycle. A failure means the claim is either not emitted or not suppressed.

- [ ] **Step 7: Mutation check**

Temporarily delete the `if (mustChangePassword)` guard so the claim is always added. Re-run: `The_seeded_admin_token_carries_no_must_change_password_claim` must FAIL. Restore the guard. If it passed with the guard deleted, the test proves nothing — fix it before continuing.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: carry MustChangePassword as a token claim"
```

---

## Task 2: Password-change gate middleware

**Files:**
- Create: `src/FamilyTree.Api/Authorization/PasswordChangeGateMiddleware.cs`
- Modify: `src/FamilyTree.Api/Program.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Auth/PasswordChangeGateTests.cs`

**Interfaces:**
- Consumes: `AuthClaims.MustChangePassword` (Task 1).
- Produces: error code `PASSWORD_CHANGE_REQUIRED` (403). Allowlist: `GET /api/v1/me` and `POST /api/v1/me/password` — the latter is built in Task 3, and the gate allows it from the start so Task 3 is not blocked.

- [ ] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Auth/PasswordChangeGateTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Auth;

[Collection("postgres")]
public sealed class PasswordChangeGateTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task FlagAdminAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await context.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
        user.MustChangePassword = true;
        await context.SaveChangesAsync();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem is not null && problem.TryGetValue("code", out var code) ? code.ToString() : null;
    }

    [Fact]
    public async Task A_flagged_user_cannot_reach_the_member_list()
    {
        await FlagAdminAsync();
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/family-members");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(response)).Should().Be("PASSWORD_CHANGE_REQUIRED");
    }

    [Fact]
    public async Task A_flagged_user_can_still_reach_me()
    {
        await FlagAdminAsync();
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/me");

        // The gate must leave a door open, otherwise the client has no way to discover why
        // it is being refused.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unflagged_user_is_unaffected()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/family-members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_gate_does_not_apply_to_anonymous_requests()
    {
        await FlagAdminAsync();

        // Login must stay reachable — a flagged user has to authenticate before they can
        // change their password.
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test tests/FamilyTree.Api.IntegrationTests --filter FullyQualifiedName~PasswordChangeGateTests
```

Expected: `A_flagged_user_cannot_reach_the_member_list` FAILS (returns 200, not 403). The other three pass already.

- [ ] **Step 3: Write the middleware**

```csharp
using FamilyTree.Api.Errors;
using FamilyTree.Application.Auth;

namespace FamilyTree.Api.Authorization;

/// <summary>
/// While a user holds a temporary password, their token can do exactly two things: read /me
/// and set a new password. Enforced here rather than in the frontend because §9 forbids
/// business rules that live only in the UI — the same token presented by curl is equally
/// restricted (design spec §4.9).
/// </summary>
public sealed class PasswordChangeGateMiddleware(RequestDelegate next)
{
    private static readonly (string Method, string Path)[] Allowed =
    [
        ("GET", "/api/v1/me"),
        ("POST", "/api/v1/me/password")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsBlocked(context))
        {
            await next(context);
            return;
        }

        var result = ProblemResults.Coded(
            StatusCodes.Status403Forbidden,
            "PASSWORD_CHANGE_REQUIRED",
            "A password change is required before continuing.");

        await result.ExecuteAsync(context);
    }

    private static bool IsBlocked(HttpContext context)
    {
        // Anonymous requests are the authentication layer's problem, not this gate's:
        // login and refresh must stay reachable for a flagged user.
        if (context.User.Identity?.IsAuthenticated != true) return false;

        if (!context.User.HasClaim(AuthClaims.MustChangePassword, "true")) return false;

        return !Allowed.Any(a =>
            string.Equals(context.Request.Method, a.Method, StringComparison.OrdinalIgnoreCase) &&
            context.Request.Path.Equals(a.Path, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 4: Register it**

In `Program.cs`, immediately **after** `app.UseAuthorization();` — it needs the authenticated principal, and it must not pre-empt the authentication layer's handling of anonymous requests:

```csharp
app.UseMiddleware<PasswordChangeGateMiddleware>();
```

Add `using FamilyTree.Api.Authorization;` if absent. If `Program.cs` composes the pipeline differently, place the call so it runs after authentication and authorization, and say so in your report.

- [ ] **Step 5: Run to verify it passes**

Expected: all four PASS.

- [ ] **Step 6: Mutation check**

Temporarily add `("GET", "/api/v1/family-members")` to `Allowed`. Re-run: `A_flagged_user_cannot_reach_the_member_list` must FAIL. Remove it.

- [ ] **Step 7: Full backend suite**

```bash
dotnet test
```

Expected: all pass. The gate sits in front of every authenticated route, so a regression shows up as widespread 403s — that is what this run checks for.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: block all but /me routes while a password change is pending"
```

---

## Task 3: Self-service password change

**Files:**
- Create: `src/FamilyTree.Contracts/Auth/ChangePasswordRequest.cs`
- Modify: `src/FamilyTree.Contracts/Auth/CurrentUserResponse.cs`, `src/FamilyTree.Api/Endpoints/Me/MeEndpoints.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Auth/ChangePasswordTests.cs`

**Interfaces:**
- Consumes: the allowlist entry for `POST /api/v1/me/password` (Task 2).
- Produces: `ChangePasswordRequest(string CurrentPassword, string NewPassword)`; `CurrentUserResponse` gains a trailing `bool MustChangePassword`; error codes `PASSWORD_INCORRECT` (400) and `PASSWORD_TOO_SHORT` (400).

- [ ] **Step 1: Add the contract**

```csharp
namespace FamilyTree.Contracts.Auth;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
```

- [ ] **Step 2: Extend `CurrentUserResponse`**

```csharp
namespace FamilyTree.Contracts.Auth;

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    Guid TenantId,
    string FamilyTreeName,
    IReadOnlyCollection<string> Permissions,
    bool MustChangePassword);
```

- [ ] **Step 3: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Auth/ChangePasswordTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Auth;

[Collection("postgres")]
public sealed class ChangePasswordTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string NewPassword = "An0ther!Str0ng#Password";

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
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

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem is not null && problem.TryGetValue("code", out var code) ? code.ToString() : null;
    }

    [Fact]
    public async Task Changing_the_password_lets_the_new_one_log_in_and_retires_the_old_one()
    {
        await AuthenticateAsync();

        var change = await _client.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest(ApiFactory.AdminPassword, NewPassword));
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var fresh = _factory.CreateClient();

        var withNew = await fresh.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, NewPassword));
        withNew.StatusCode.Should().Be(HttpStatusCode.OK);

        // Both halves matter: a change that accepted the new password while still accepting
        // the old one would pass a weaker test and leave the old credential live.
        var withOld = await fresh.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        withOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_current_password_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest("not-the-current-password", NewPassword));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("PASSWORD_INCORRECT");
    }

    [Fact]
    public async Task A_short_new_password_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest(ApiFactory.AdminPassword, "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("PASSWORD_TOO_SHORT");
    }

    [Fact]
    public async Task Changing_the_password_clears_the_must_change_flag_and_revokes_refresh_tokens()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.IgnoreQueryFilters()
                .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
            user.MustChangePassword = true;
            await context.SaveChangesAsync();
        }

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var change = await _client.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest(ApiFactory.AdminPassword, NewPassword));
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.IgnoreQueryFilters()
                .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
            user.MustChangePassword.Should().BeFalse();
        }

        // The refresh token issued against the old password must not survive the change:
        // otherwise a stolen refresh token outlives the rotation meant to kill it.
        using var fresh = _factory.CreateClient();
        var refresh = await fresh.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(tokens.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 4: Run to verify it fails**

Expected: all four FAIL with 404 (the route does not exist).

- [ ] **Step 5: Implement the endpoint**

In `MeEndpoints.cs` add usings: `FamilyTree.Api.Errors`, `FamilyTree.Infrastructure.Identity`, `Microsoft.AspNetCore.Identity`.

Change the `MapGet` handler's tail to read the flag:

```csharp
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == tenant.UserId, ct);

            return Results.Ok(new CurrentUserResponse(
                tenant.UserId, email, tenant.TenantId, tree.Name, permissions,
                user?.MustChangePassword ?? false));
```

Then add the new endpoint before `return app;`:

```csharp
        app.MapPost("/api/v1/me/password", async (
            ChangePasswordRequest request,
            ITenantContext tenant,
            ApplicationDbContext context,
            IPasswordHasher<ApplicationUser> passwordHasher,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            const int minimumPasswordLength = 12;

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == tenant.UserId, ct);
            if (user?.PasswordHash is null)
                return Results.Unauthorized();

            var verification = passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, request.CurrentPassword);

            if (verification == PasswordVerificationResult.Failed)
                return ProblemResults.Coded(StatusCodes.Status400BadRequest,
                    "PASSWORD_INCORRECT", "The current password is incorrect.");

            if (request.NewPassword.Length < minimumPasswordLength)
                return ProblemResults.Coded(StatusCodes.Status400BadRequest,
                    "PASSWORD_TOO_SHORT",
                    $"A password must be at least {minimumPasswordLength} characters.");

            user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
            user.MustChangePassword = false;

            // Every refresh token predates the new password, so each one is a credential the
            // user just chose to rotate away from. Revoking them all is what makes "change my
            // password" also mean "sign my other devices out".
            var now = timeProvider.GetUtcNow();
            var tokens = await context.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync(ct);
            foreach (var token in tokens)
                token.Revoke(now, replacedByTokenHash: null);

            await context.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags("Me");
```

`.RequireAuthorization()` carries no policy deliberately: any authenticated user may change their own password, and requiring a permission would lock out exactly the users this endpoint exists for.

- [ ] **Step 6: Run to verify it passes**

Expected: all four PASS.

- [ ] **Step 7: Mutation check**

Delete `user.MustChangePassword = false;` — the fourth test must FAIL. Restore. Then delete the `foreach (...) token.Revoke(...)` loop — the same test must FAIL on the refresh assertion. Restore.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add self-service password change"
```

---

## Task 4: User contracts, read service, and `GET /api/v1/users`

**Files:**
- Create: `src/FamilyTree.Contracts/Users/UserRoleSummary.cs`, `src/FamilyTree.Contracts/Users/UserResponse.cs`, `src/FamilyTree.Application/Users/IUserService.cs`, `src/FamilyTree.Infrastructure/Users/UserService.cs`, `src/FamilyTree.Api/Endpoints/Users/UserEndpoints.cs`
- Modify: `src/FamilyTree.Api/Program.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/UserEndpointsTests.cs`

**Interfaces:**
- Produces: `UserRoleSummary(Guid Id, string Name)`; `UserResponse(Guid Id, string Email, bool IsActive, bool MustChangePassword, DateTimeOffset? LastLoginAt, IReadOnlyList<UserRoleSummary> Roles)`; `IUserService.ListAsync` and `GetAsync(Guid)` returning `UserResponse?`. Later tasks add `CreateAsync`, `UpdateAsync`, `SetActiveAsync`, `ResetPasswordAsync` to the same interface. The test class also produces the shared helpers `AuthenticateAsync`, `RoleIdAsync`, and `CodeOf`, reused by Tasks 5, 7, 8, and copied by Task 9.

- [ ] **Step 1: Write the contracts**

```csharp
namespace FamilyTree.Contracts.Users;

public sealed record UserRoleSummary(Guid Id, string Name);
```

```csharp
namespace FamilyTree.Contracts.Users;

/// <summary>
/// Deliberately carries no password material of any kind — not the hash, not a placeholder.
/// A field that does not exist cannot be leaked by a future serialization change.
/// </summary>
public sealed record UserResponse(
    Guid Id,
    string Email,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<UserRoleSummary> Roles);
```

- [ ] **Step 2: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/UserEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.Users;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class UserEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
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

    private async Task<Guid> RoleIdAsync(string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == name).Select(r => r.Id).SingleAsync();
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem is not null && problem.TryGetValue("code", out var code) ? code.ToString() : null;
    }

    [Fact]
    public async Task Listing_users_requires_authentication()
    {
        var response = await _client.GetAsync("/api/v1/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Listing_returns_the_seeded_admin_with_its_role()
    {
        await AuthenticateAsync();

        var users = await _client.GetFromJsonAsync<List<UserResponse>>("/api/v1/users");

        users.Should().ContainSingle();
        var admin = users![0];
        admin.Email.Should().Be(ApiFactory.AdminEmail);
        admin.IsActive.Should().BeTrue();
        admin.MustChangePassword.Should().BeFalse();
        admin.Roles.Should().ContainSingle().Which.Name.Should().Be("Super Admin");
    }

    [Fact]
    public async Task Fetching_an_unknown_user_returns_404()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/users/0199a0b1-0000-7000-8000-000000000001");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Expected: the two authenticated tests FAIL with 404. `Listing_users_requires_authentication` passes trivially — an unmapped route is 404, not 401, so **it is vacuous right now**. Step 8 re-verifies it once the route exists.

- [ ] **Step 4: Write the interface**

```csharp
using FamilyTree.Contracts.Users;

namespace FamilyTree.Application.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken ct = default);

    /// <summary>Returns null when no such user is visible to the caller's tenant.</summary>
    Task<UserResponse?> GetAsync(Guid id, CancellationToken ct = default);
}
```

- [ ] **Step 5: Write the implementation**

```csharp
using FamilyTree.Application.Users;
using FamilyTree.Contracts.Users;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Users;

/// <summary>
/// Every query runs through the tenant query filter on ApplicationUser, so "no such user" and
/// "another tenant's user" are the same code path — which makes the uniform 404 in design
/// spec §4.4 true by construction rather than by discipline.
/// </summary>
public sealed class UserService(ApplicationDbContext context) : IUserService
{
    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken ct = default)
    {
        var users = await context.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        var roles = await RolesByUserAsync(users.Select(u => u.Id).ToList(), ct);

        return users.Select(u => Map(u, roles)).ToList();
    }

    public async Task<UserResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return null;

        return Map(user, await RolesByUserAsync([user.Id], ct));
    }

    /// <summary>
    /// One query for all users rather than one per user. UserRole has no tenant column of its
    /// own, so the tenant guarantee comes from joining Roles, which is filtered.
    /// </summary>
    private async Task<ILookup<Guid, UserRoleSummary>> RolesByUserAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var rows = await (
            from userRole in context.UserRoles
            join role in context.Roles on userRole.RoleId equals role.Id
            where userIds.Contains(userRole.UserId)
            select new { userRole.UserId, role.Id, role.Name })
            .ToListAsync(ct);

        return rows.ToLookup(r => r.UserId, r => new UserRoleSummary(r.Id, r.Name));
    }

    private static UserResponse Map(ApplicationUser user, ILookup<Guid, UserRoleSummary> roles) =>
        new(user.Id,
            user.Email ?? string.Empty,
            user.IsActive,
            user.MustChangePassword,
            user.LastLoginAt,
            roles[user.Id].OrderBy(r => r.Name).ToList());
}
```

- [ ] **Step 6: Write the endpoints**

```csharp
using FamilyTree.Api.Authorization;
using FamilyTree.Application.Users;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users");

        group.MapGet("/", async (IUserService users, CancellationToken ct) =>
            Results.Ok(await users.ListAsync(ct)))
            .RequirePermission(Permissions.User.View);

        group.MapGet("/{id:guid}", async (Guid id, IUserService users, CancellationToken ct) =>
            await users.GetAsync(id, ct) is { } user ? Results.Ok(user) : Results.NotFound())
            .RequirePermission(Permissions.User.View);

        return app;
    }
}
```

- [ ] **Step 7: Register in `Program.cs`**

Beside the existing `IFamilyMemberService` registration and `MapFamilyTreeEndpoints()` call:

```csharp
builder.Services.AddScoped<IUserService, UserService>();
```

```csharp
app.MapUserEndpoints();
```

Add usings `FamilyTree.Application.Users`, `FamilyTree.Infrastructure.Users`, `FamilyTree.Api.Endpoints.Users`.

- [ ] **Step 8: Run to verify it passes**

Expected: all three PASS — and `Listing_users_requires_authentication` is now non-vacuous, because the route exists and returns 401 rather than 404. Confirm that explicitly.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: list and fetch tenant users"
```

---

## Task 5: Create a user

**Files:**
- Create: `src/FamilyTree.Contracts/Users/CreateUserRequest.cs`
- Modify: `src/FamilyTree.Application/Users/IUserService.cs`, `src/FamilyTree.Infrastructure/Users/UserService.cs`, `src/FamilyTree.Api/Endpoints/Users/UserEndpoints.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/UserEndpointsTests.cs` (add to the existing class)

**Interfaces:**
- Consumes: `UserResponse`, `IUserService`, and the `AuthenticateAsync` / `RoleIdAsync` / `CodeOf` test helpers (Task 4).
- Produces: `CreateUserRequest(string Email, string Password, IReadOnlyList<Guid> RoleIds)`; `IUserService.CreateAsync` returning `UserResponse`; `UserService.MinimumPasswordLength` (`public const int` = 12); private `ValidateEmail` and `ValidateRoleIdsAsync` reused by Task 7; error codes `USER_EMAIL_REQUIRED`, `USER_EMAIL_INVALID`, `USER_EMAIL_TAKEN` (409), `PASSWORD_TOO_SHORT`, `ROLE_NOT_FOUND`.

- [ ] **Step 1: Write the contract**

```csharp
namespace FamilyTree.Contracts.Users;

public sealed record CreateUserRequest(
    string Email, string Password, IReadOnlyList<Guid> RoleIds);
```

- [ ] **Step 2: Write the failing tests**

Add to `UserEndpointsTests`:

```csharp
    [Fact]
    public async Task Creating_a_user_assigns_roles_and_forces_a_password_change()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        created.Email.Should().Be("cousin@example.com");
        created.IsActive.Should().BeTrue();
        // The administrator chose this password, so the new user must replace it (spec §4.9).
        created.MustChangePassword.Should().BeTrue();
        created.Roles.Should().ContainSingle().Which.Name.Should().Be("Viewer");
    }

    [Fact]
    public async Task A_created_user_can_log_in_and_is_immediately_gated()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));

        using var fresh = _factory.CreateClient();
        var login = await fresh.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        fresh.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var blocked = await fresh.GetAsync("/api/v1/family-members");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(blocked)).Should().Be("PASSWORD_CHANGE_REQUIRED");
    }

    [Fact]
    public async Task A_duplicate_email_is_rejected()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest(ApiFactory.AdminEmail, "Temp0rary!Password", [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("USER_EMAIL_TAKEN");
    }

    [Fact]
    public async Task An_unknown_role_id_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password",
                [Guid.Parse("0199a0b1-0000-7000-8000-000000000001")]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("ROLE_NOT_FOUND");
    }

    [Fact]
    public async Task A_short_password_is_rejected()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "short", [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("PASSWORD_TOO_SHORT");
    }
```

- [ ] **Step 3: Run to verify they fail**

Expected: the five new tests FAIL with 404 (no POST route).

- [ ] **Step 4: Extend the interface**

```csharp
    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
```

- [ ] **Step 5: Implement**

Change `UserService`'s declaration to:

```csharp
public sealed class UserService(
    ApplicationDbContext context,
    ITenantContext tenant,
    IPasswordHasher<ApplicationUser> passwordHasher,
    TimeProvider timeProvider) : IUserService
```

Add usings: `FamilyTree.Application.Common`, `FamilyTree.Domain.Authorization`, `FamilyTree.Domain.Common`, `Microsoft.AspNetCore.Identity`, `Npgsql`.

```csharp
    public const int MinimumPasswordLength = 12;

    /// <summary>PostgreSQL SQLSTATE for a unique violation.</summary>
    private const string UniqueViolation = "23505";

    public async Task<UserResponse> CreateAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        var email = ValidateEmail(request.Email);

        if ((request.Password ?? string.Empty).Length < MinimumPasswordLength)
            throw new DomainException("PASSWORD_TOO_SHORT",
                $"A password must be at least {MinimumPasswordLength} characters.");

        var roleIds = await ValidateRoleIdsAsync(request.RoleIds, ct);
        var normalized = email.ToUpperInvariant();

        // Filtered check: a duplicate in another tenant is not a duplicate here. The unique
        // index is global, though, so the catch below is what actually holds the line.
        if (await context.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw new ConflictException("USER_EMAIL_TAKEN", "That email address is already in use.");

        var now = timeProvider.GetUtcNow();

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Email = email,
            NormalizedEmail = normalized,
            UserName = email,
            NormalizedUserName = normalized,
            SecurityStamp = Guid.CreateVersion7().ToString(),
            IsActive = true,
            CreatedAt = now,
            // An administrator chose this password, so it is temporary by definition (§4.9).
            MustChangePassword = true
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);

        context.Users.Add(user);
        foreach (var roleId in roleIds)
            context.UserRoles.Add(UserRole.Create(user.Id, roleId));

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // The check above is check-then-act and can lose a race. Map the raw violation to
            // the same code so two concurrent creates give one caller a clean conflict.
            throw new ConflictException("USER_EMAIL_TAKEN", "That email address is already in use.");
        }

        return (await GetAsync(user.Id, ct))!;
    }

    /// <summary>
    /// Deliberately minimal: one '@' with text on both sides and no whitespace. There is no
    /// email delivery in V1 (spec §4.2), so this is a typo guard, not an address validator.
    /// </summary>
    private static string ValidateEmail(string? value)
    {
        var email = (value ?? string.Empty).Trim();

        if (email.Length == 0)
            throw new DomainException("USER_EMAIL_REQUIRED", "An email address is required.");

        if (email.Count(c => c == '@') != 1 || email.StartsWith('@') || email.EndsWith('@')
            || email.Any(char.IsWhiteSpace))
            throw new DomainException("USER_EMAIL_INVALID", "That email address is not valid.");

        return email;
    }

    /// <summary>
    /// Filtered lookup, so a role id belonging to another tenant is indistinguishable from one
    /// that does not exist — the same rule §4.4 applies to every other id.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ValidateRoleIdsAsync(
        IReadOnlyList<Guid>? roleIds, CancellationToken ct)
    {
        var requested = (roleIds ?? []).Distinct().ToList();
        if (requested.Count == 0) return [];

        var found = await context.Roles
            .Where(r => requested.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (found.Count != requested.Count)
            throw new DomainException("ROLE_NOT_FOUND", "One or more roles do not exist.");

        return found;
    }
```

- [ ] **Step 6: Add the endpoint**

```csharp
        group.MapPost("/", async (
            CreateUserRequest request, IUserService users, CancellationToken ct) =>
        {
            var created = await users.CreateAsync(request, ct);
            return Results.Created($"/api/v1/users/{created.Id}", created);
        })
            .RequirePermission(Permissions.User.Create);
```

Add `using FamilyTree.Contracts.Users;`.

- [ ] **Step 7: Run to verify they pass**

Expected: all eight PASS.

- [ ] **Step 8: Mutation check**

Change `MustChangePassword = true` to `false`. Re-run: **two** tests must FAIL. Restore it.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: create users with roles and a temporary password"
```

---

## Task 6: The last-administrator guard

**Files:**
- Create: `src/FamilyTree.Application/Users/IAdministratorGuard.cs`, `src/FamilyTree.Infrastructure/Users/AdministratorGuard.cs`
- Modify: `src/FamilyTree.Api/Program.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Authorization/AdministratorGuardTests.cs`

**Interfaces:**
- Produces: `IAdministratorGuard.EnsureAdministratorRemainsAsync(CancellationToken ct = default)` — throws `ConflictException("LAST_ADMINISTRATOR", …)` when no active user in the tenant holds both `User.Edit` and `Role.Edit`. Called **after** a change is staged in the `DbContext` but **before** `SaveChangesAsync`, so it evaluates the post-change state.

**This is the highest-risk task in the plan.** The obvious implementation is wrong in two ways — it must not look at role names, and it must not evaluate pre-change state.

- [ ] **Step 1: Write the interface**

```csharp
namespace FamilyTree.Application.Users;

/// <summary>
/// Prevents a tenant from removing its own ability to administer itself. Call after staging a
/// change and before saving it — the guard reads the state the save is about to produce.
/// </summary>
public interface IAdministratorGuard
{
    /// <summary>
    /// Throws LAST_ADMINISTRATOR when no active user in the tenant would still hold both
    /// User.Edit and Role.Edit.
    /// </summary>
    Task EnsureAdministratorRemainsAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Verify one signature before writing the test**

Read `src/FamilyTree.Domain/Authorization/RolePermission.cs` and confirm the factory is `RolePermission.Create(Guid roleId, Guid permissionId)`. If it differs, use the real one throughout this task and note the difference in your report.

- [ ] **Step 3: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Authorization/AdministratorGuardTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.Users;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Common;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Authorization;

[Collection("postgres")]
public sealed class AdministratorGuardTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task The_guard_passes_while_an_active_administrator_remains()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();

        var act = () => guard.EnsureAdministratorRemainsAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_guard_rejects_deactivating_the_only_administrator()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();

        var admin = await context.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
        admin.IsActive = false;

        // Staged but not saved: the guard must see the pending change, which is what makes it
        // usable as a pre-save gate rather than an after-the-fact audit.
        var act = () => guard.EnsureAdministratorRemainsAsync();

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("LAST_ADMINISTRATOR");
    }

    [Fact]
    public async Task The_guard_rejects_stripping_the_only_administrators_roles()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();

        context.UserRoles.RemoveRange(await context.UserRoles.ToListAsync());

        var act = () => guard.EnsureAdministratorRemainsAsync();

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("LAST_ADMINISTRATOR");
    }

    [Fact]
    public async Task A_role_named_something_else_still_counts_as_administration()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IAdministratorGuard>();
        var now = TimeProvider.System.GetUtcNow();

        var admin = await context.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());

        // A custom role holding the two recovery permissions, with a name matching no system
        // role. If the guard ever regresses to a name check, this test fails.
        var custom = Role.Create(admin.TenantId, "أمناء العائلة", null, now);
        context.Roles.Add(custom);

        var permissionIds = await context.Permissions
            .Where(p => p.Code == Permissions.User.Edit || p.Code == Permissions.Role.Edit)
            .Select(p => p.Id)
            .ToListAsync();

        permissionIds.Should().HaveCount(2);

        foreach (var permissionId in permissionIds)
            context.RolePermissions.Add(RolePermission.Create(custom.Id, permissionId));

        context.UserRoles.RemoveRange(await context.UserRoles.ToListAsync());
        context.UserRoles.Add(UserRole.Create(admin.Id, custom.Id));

        var act = () => guard.EnsureAdministratorRemainsAsync();

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 4: Run to verify it fails**

Expected: all four FAIL — `IAdministratorGuard` is not registered, so resolution throws.

- [ ] **Step 5: Implement the guard**

```csharp
using FamilyTree.Application.Common;
using FamilyTree.Application.Users;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Common;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Users;

/// <summary>
/// A tenant must not be able to strip itself of administration (design spec §4.9).
///
/// Two decisions here are load-bearing:
///
/// 1. The rule is expressed in PERMISSIONS, not role names. §4.3 says authorization is never
///    role-name-based; a name check would also be defeated by renaming Super Admin or by a
///    custom role that is Super Admin in all but name.
///
/// 2. It queries through the tracked DbContext rather than a fresh one, so pending changes are
///    visible. That is the whole point: the caller stages a deactivation or a role removal,
///    asks whether an administrator remains, and only then saves.
/// </summary>
public sealed class AdministratorGuard(
    ApplicationDbContext context, ITenantContext tenant) : IAdministratorGuard
{
    /// <summary>The pair required to undo any change this guard protects against.</summary>
    private static readonly string[] RecoveryPermissions =
        [Permissions.User.Edit, Permissions.Role.Edit];

    public async Task EnsureAdministratorRemainsAsync(CancellationToken ct = default)
    {
        var users = await context.Users.Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct);
        if (users.Count == 0) throw Rejected();

        var grants = await (
            from userRole in context.UserRoles
            join role in context.Roles on userRole.RoleId equals role.Id
            join rolePermission in context.RolePermissions on role.Id equals rolePermission.RoleId
            join permission in context.Permissions
                on rolePermission.PermissionId equals permission.Id
            where role.TenantId == tenant.TenantId
                && RecoveryPermissions.Contains(permission.Code)
            select new { userRole.UserId, permission.Code })
            .ToListAsync(ct);

        var survives = users.Any(userId =>
        {
            var held = grants.Where(g => g.UserId == userId).Select(g => g.Code).ToHashSet();
            return RecoveryPermissions.All(held.Contains);
        });

        if (!survives) throw Rejected();
    }

    private static ConflictException Rejected() => new(
        "LAST_ADMINISTRATOR",
        "This change would leave the account with no one able to manage users and roles.");
}
```

**Implementer note — read before declaring this task done.** EF Core translates these queries to SQL, and SQL does not see un-saved changes tracked in memory. Step 3's second and third tests are precisely the cases where that matters. Run them: if either still fails after this implementation, the guard is reading stale state and must be reworked to merge the database rows with `context.Users.Local` / `context.UserRoles.Local` (honouring `EntityState.Deleted` and `Added`) before evaluating. **Do not weaken the tests to match a guard that reads stale state** — a guard that cannot see the change it is guarding is worse than none, because it reports safety it has not checked. Report which path you took.

- [ ] **Step 6: Register it**

```csharp
builder.Services.AddScoped<IAdministratorGuard, AdministratorGuard>();
```

- [ ] **Step 7: Run to verify it passes**

Expected: all four PASS.

- [ ] **Step 8: Mutation check**

Temporarily rewrite the guard to look for a role named `"Super Admin"` instead of the permission pair. `A_role_named_something_else_still_counts_as_administration` must FAIL. Restore the permission-based version. This test is what keeps the guard honest — confirm it bites.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: guard against removing a tenant's last administrator"
```

---

## Task 7: Update a user's email and roles

**Files:**
- Create: `src/FamilyTree.Contracts/Users/UpdateUserRequest.cs`
- Modify: `src/FamilyTree.Application/Users/IUserService.cs`, `src/FamilyTree.Infrastructure/Users/UserService.cs`, `src/FamilyTree.Api/Endpoints/Users/UserEndpoints.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/UserEndpointsTests.cs` (add)

**Interfaces:**
- Consumes: `IAdministratorGuard` (Task 6); `ValidateEmail`, `ValidateRoleIdsAsync`, `UniqueViolation` (Task 5).
- Produces: `UpdateUserRequest(string Email, IReadOnlyList<Guid> RoleIds)`; `IUserService.UpdateAsync(Guid id, UpdateUserRequest, CancellationToken)` returning `UserResponse`; error code `USER_NOT_FOUND` (404).

- [ ] **Step 1: Write the contract**

```csharp
namespace FamilyTree.Contracts.Users;

/// <summary>
/// Roles are replaced wholesale rather than patched. A partial update would need add/remove
/// lists and a merge rule; sending the intended final set makes the guard in §4.9 a simple
/// question about the state the request asks for.
/// </summary>
public sealed record UpdateUserRequest(string Email, IReadOnlyList<Guid> RoleIds);
```

- [ ] **Step 2: Write the failing tests**

```csharp
    [Fact]
    public async Task Updating_replaces_the_role_set()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");
        var editorRoleId = await RoleIdAsync("Editor");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));
        var user = (await create.Content.ReadFromJsonAsync<UserResponse>())!;

        var response = await _client.PutAsJsonAsync($"/api/v1/users/{user.Id}",
            new UpdateUserRequest("cousin@example.com", [editorRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        updated.Roles.Should().ContainSingle().Which.Name.Should().Be("Editor");
    }

    [Fact]
    public async Task Updating_an_unknown_user_returns_404()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var response = await _client.PutAsJsonAsync(
            "/api/v1/users/0199a0b1-0000-7000-8000-000000000001",
            new UpdateUserRequest("nobody@example.com", [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stripping_the_last_administrators_roles_is_rejected()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var users = await _client.GetFromJsonAsync<List<UserResponse>>("/api/v1/users");
        var admin = users!.Single(u => u.Email == ApiFactory.AdminEmail);

        // Viewer holds neither User.Edit nor Role.Edit, so this would leave the tenant unable
        // to manage itself.
        var response = await _client.PutAsJsonAsync($"/api/v1/users/{admin.Id}",
            new UpdateUserRequest(ApiFactory.AdminEmail, [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("LAST_ADMINISTRATOR");
    }

    [Fact]
    public async Task Demoting_an_administrator_is_allowed_when_another_remains()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");
        var superAdminRoleId = await RoleIdAsync("Super Admin");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("second@example.com", "Temp0rary!Password", [superAdminRoleId]));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var users = await _client.GetFromJsonAsync<List<UserResponse>>("/api/v1/users");
        var admin = users!.Single(u => u.Email == ApiFactory.AdminEmail);

        var response = await _client.PutAsJsonAsync($"/api/v1/users/{admin.Id}",
            new UpdateUserRequest(ApiFactory.AdminEmail, [viewerRoleId]));

        // The mirror of the previous test: the guard must permit exactly the case that is safe,
        // otherwise it is indistinguishable from "administrators can never be demoted".
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
```

- [ ] **Step 3: Run to verify they fail**

Expected: all four FAIL with 404 (no PUT route).

- [ ] **Step 4: Extend the interface**

```csharp
    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
```

- [ ] **Step 5: Implement**

Add `IAdministratorGuard guard` to `UserService`'s constructor parameter list, then:

```csharp
    public async Task<UserResponse> UpdateAsync(
        Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "No such user.");

        var email = ValidateEmail(request.Email);
        var roleIds = await ValidateRoleIdsAsync(request.RoleIds, ct);
        var normalized = email.ToUpperInvariant();

        if (normalized != user.NormalizedEmail
            && await context.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw new ConflictException("USER_EMAIL_TAKEN", "That email address is already in use.");

        user.Email = email;
        user.NormalizedEmail = normalized;
        user.UserName = email;
        user.NormalizedUserName = normalized;

        var existing = await context.UserRoles.Where(ur => ur.UserId == user.Id).ToListAsync(ct);
        context.UserRoles.RemoveRange(existing);
        foreach (var roleId in roleIds)
            context.UserRoles.Add(UserRole.Create(user.Id, roleId));

        // Staged, not saved: the guard evaluates the state this request is asking for.
        await guard.EnsureAdministratorRemainsAsync(ct);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            throw new ConflictException("USER_EMAIL_TAKEN", "That email address is already in use.");
        }

        return (await GetAsync(user.Id, ct))!;
    }
```

- [ ] **Step 6: Add the endpoint**

```csharp
        group.MapPut("/{id:guid}", async (
            Guid id, UpdateUserRequest request, IUserService users, CancellationToken ct) =>
            Results.Ok(await users.UpdateAsync(id, request, ct)))
            .RequirePermission(Permissions.User.Edit);
```

- [ ] **Step 7: Run to verify they pass**

Expected: all twelve `UserEndpointsTests` PASS.

- [ ] **Step 8: Mutation check**

Delete `await guard.EnsureAdministratorRemainsAsync(ct);`. `Stripping_the_last_administrators_roles_is_rejected` must FAIL while `Demoting_an_administrator_is_allowed_when_another_remains` still passes. Restore it.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: update a user's email and role assignments"
```

---

## Task 8: Activate, deactivate, and administrator password reset

**Files:**
- Create: `src/FamilyTree.Contracts/Users/ResetPasswordRequest.cs`
- Modify: `src/FamilyTree.Application/Users/IUserService.cs`, `src/FamilyTree.Infrastructure/Users/UserService.cs`, `src/FamilyTree.Api/Endpoints/Users/UserEndpoints.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/UserEndpointsTests.cs` (add)

**Interfaces:**
- Produces: `ResetPasswordRequest(string Password)`; `IUserService.SetActiveAsync(Guid id, bool isActive, CancellationToken)` and `ResetPasswordAsync(Guid id, ResetPasswordRequest, CancellationToken)`, both returning `UserResponse`.

- [ ] **Step 1: Write the contract**

```csharp
namespace FamilyTree.Contracts.Users;

public sealed record ResetPasswordRequest(string Password);
```

- [ ] **Step 2: Write the failing tests**

```csharp
    [Fact]
    public async Task Deactivating_a_user_blocks_their_login_and_kills_their_refresh_token()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));
        var user = (await create.Content.ReadFromJsonAsync<UserResponse>())!;

        using var theirs = _factory.CreateClient();
        var login = await theirs.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        var deactivate = await _client.PostAsync($"/api/v1/users/{user.Id}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deactivate.Content.ReadFromJsonAsync<UserResponse>())!.IsActive.Should().BeFalse();

        var again = await theirs.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        again.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var refresh = await theirs.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(tokens.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reactivating_restores_login()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));
        var user = (await create.Content.ReadFromJsonAsync<UserResponse>())!;

        await _client.PostAsync($"/api/v1/users/{user.Id}/deactivate", null);
        var activate = await _client.PostAsync($"/api/v1/users/{user.Id}/activate", null);

        activate.StatusCode.Should().Be(HttpStatusCode.OK);

        using var theirs = _factory.CreateClient();
        var login = await theirs.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deactivating_the_last_administrator_is_rejected()
    {
        await AuthenticateAsync();

        var users = await _client.GetFromJsonAsync<List<UserResponse>>("/api/v1/users");
        var admin = users!.Single(u => u.Email == ApiFactory.AdminEmail);

        var response = await _client.PostAsync($"/api/v1/users/{admin.Id}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("LAST_ADMINISTRATOR");
    }

    [Fact]
    public async Task An_administrator_reset_forces_the_user_to_change_it_again()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));
        var user = (await create.Content.ReadFromJsonAsync<UserResponse>())!;

        // Clear the flag first, so the assertion below proves the reset SET it rather than
        // merely observing the value creation left behind.
        using var theirs = _factory.CreateClient();
        var firstLogin = await theirs.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        var tokens = (await firstLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        theirs.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var selfChange = await theirs.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest("Temp0rary!Password", "Ch0sen!ByThe#User"));
        selfChange.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reset = await _client.PostAsJsonAsync($"/api/v1/users/{user.Id}/password",
            new ResetPasswordRequest("R3set!ByAdmin#Pass"));

        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reset.Content.ReadFromJsonAsync<UserResponse>())!.MustChangePassword.Should().BeTrue();

        using var after = _factory.CreateClient();
        var login = await after.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "R3set!ByAdmin#Pass"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }
```

- [ ] **Step 3: Run to verify they fail**

Expected: all four FAIL with 404.

- [ ] **Step 4: Extend the interface**

```csharp
    Task<UserResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);

    Task<UserResponse> ResetPasswordAsync(
        Guid id, ResetPasswordRequest request, CancellationToken ct = default);
```

- [ ] **Step 5: Implement**

```csharp
    public async Task<UserResponse> SetActiveAsync(
        Guid id, bool isActive, CancellationToken ct = default)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "No such user.");

        user.IsActive = isActive;

        if (!isActive)
        {
            await guard.EnsureAdministratorRemainsAsync(ct);
            await RevokeRefreshTokensAsync(user.Id, ct);
        }

        await context.SaveChangesAsync(ct);
        return (await GetAsync(user.Id, ct))!;
    }

    public async Task<UserResponse> ResetPasswordAsync(
        Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "No such user.");

        if ((request.Password ?? string.Empty).Length < MinimumPasswordLength)
            throw new DomainException("PASSWORD_TOO_SHORT",
                $"A password must be at least {MinimumPasswordLength} characters.");

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
        // An administrator chose it, so it is temporary — the same rule as creation (§4.9).
        user.MustChangePassword = true;

        await RevokeRefreshTokensAsync(user.Id, ct);
        await context.SaveChangesAsync(ct);

        return (await GetAsync(user.Id, ct))!;
    }

    /// <summary>
    /// Deactivation and password reset both invalidate the credential a refresh token was
    /// issued against. AuthService already refuses to refresh an inactive user, so this is
    /// defence in depth for deactivation — and the primary mechanism for a reset.
    /// </summary>
    private async Task RevokeRefreshTokensAsync(Guid userId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var tokens = await context.RefreshTokens.Where(t => t.UserId == userId).ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke(now, replacedByTokenHash: null);
    }
```

- [ ] **Step 6: Add the endpoints**

```csharp
        group.MapPost("/{id:guid}/activate", async (
            Guid id, IUserService users, CancellationToken ct) =>
            Results.Ok(await users.SetActiveAsync(id, isActive: true, ct)))
            .RequirePermission(Permissions.User.Deactivate);

        group.MapPost("/{id:guid}/deactivate", async (
            Guid id, IUserService users, CancellationToken ct) =>
            Results.Ok(await users.SetActiveAsync(id, isActive: false, ct)))
            .RequirePermission(Permissions.User.Deactivate);

        group.MapPost("/{id:guid}/password", async (
            Guid id, ResetPasswordRequest request, IUserService users, CancellationToken ct) =>
            Results.Ok(await users.ResetPasswordAsync(id, request, ct)))
            .RequirePermission(Permissions.User.Edit);
```

- [ ] **Step 7: Run to verify they pass**

Expected: all sixteen `UserEndpointsTests` PASS.

- [ ] **Step 8: Mutation check**

Delete `await guard.EnsureAdministratorRemainsAsync(ct);` from `SetActiveAsync`. `Deactivating_the_last_administrator_is_rejected` must FAIL. Restore it.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: activate, deactivate, and reset user passwords"
```

---

## Task 9: Role read endpoints and the permission catalog

**Files:**
- Create: `src/FamilyTree.Contracts/Roles/PermissionResponse.cs`, `src/FamilyTree.Contracts/Roles/RoleResponse.cs`, `src/FamilyTree.Application/Roles/IRoleService.cs`, `src/FamilyTree.Infrastructure/Roles/RoleService.cs`, `src/FamilyTree.Api/Endpoints/Roles/RoleEndpoints.cs`
- Modify: `src/FamilyTree.Api/Program.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/RoleEndpointsTests.cs`

**Interfaces:**
- Produces: `PermissionResponse(string Code, string Description)`; `RoleResponse(Guid Id, string Name, string? Description, bool IsSystem, int UserCount, IReadOnlyList<string> Permissions)`; `IRoleService.ListAsync`, `GetAsync(Guid)`, `ListPermissionsAsync`.

- [ ] **Step 1: Verify one property name**

Read `src/FamilyTree.Domain/Authorization/Permission.cs` and confirm the human-readable text property is named `Description`. If it differs, use the real name throughout and note it in your report.

- [ ] **Step 2: Write the contracts**

```csharp
namespace FamilyTree.Contracts.Roles;

public sealed record PermissionResponse(string Code, string Description);
```

```csharp
namespace FamilyTree.Contracts.Roles;

/// <summary>
/// Permissions are codes, not ids: the catalog is global and stable, and codes are what the
/// frontend already reasons about (it holds them as claims). UserCount lets the UI warn before
/// a change that affects people.
/// </summary>
public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    int UserCount,
    IReadOnlyList<string> Permissions);
```

- [ ] **Step 3: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/RoleEndpointsTests.cs`. Copy the fixture boilerplate (`InitializeAsync`, `DisposeAsync`, `AuthenticateAsync`, `CodeOf`) from `UserEndpointsTests` verbatim, then add:

```csharp
    [Fact]
    public async Task Listing_roles_requires_authentication()
    {
        var response = await _client.GetAsync("/api/v1/roles");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Listing_returns_the_four_seeded_system_roles()
    {
        await AuthenticateAsync();

        var roles = await _client.GetFromJsonAsync<List<RoleResponse>>("/api/v1/roles");

        roles.Should().HaveCount(4);
        roles.Should().OnlyContain(r => r.IsSystem);
        roles!.Select(r => r.Name).Should()
            .BeEquivalentTo("Super Admin", "Administrator", "Editor", "Viewer");
    }

    [Fact]
    public async Task Super_admin_carries_every_permission_and_one_user()
    {
        await AuthenticateAsync();

        var roles = await _client.GetFromJsonAsync<List<RoleResponse>>("/api/v1/roles");
        var superAdmin = roles!.Single(r => r.Name == "Super Admin");

        superAdmin.Permissions.Should().HaveCount(18);
        superAdmin.UserCount.Should().Be(1);
    }

    [Fact]
    public async Task The_permission_catalog_lists_every_code()
    {
        await AuthenticateAsync();

        var permissions =
            await _client.GetFromJsonAsync<List<PermissionResponse>>("/api/v1/permissions");

        permissions.Should().HaveCount(18);
        permissions!.Select(p => p.Code).Should().Contain("Member.Move");
    }

    [Fact]
    public async Task Fetching_an_unknown_role_returns_404()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/roles/0199a0b1-0000-7000-8000-000000000001");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
```

The count `18` comes from `Permissions.All`. Verify it against `src/FamilyTree.Domain/Authorization/Permissions.cs` before running; if the catalog has grown, use the real number.

- [ ] **Step 4: Run to verify it fails**

Expected: the authenticated tests FAIL with 404.

- [ ] **Step 5: Write the interface**

```csharp
using FamilyTree.Contracts.Roles;

namespace FamilyTree.Application.Roles;

public interface IRoleService
{
    Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken ct = default);

    /// <summary>Returns null when no such role is visible to the caller's tenant.</summary>
    Task<RoleResponse?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>The global permission catalog. Not tenant-scoped — every tenant sees all codes.</summary>
    Task<IReadOnlyList<PermissionResponse>> ListPermissionsAsync(CancellationToken ct = default);
}
```

- [ ] **Step 6: Implement**

```csharp
using FamilyTree.Application.Roles;
using FamilyTree.Contracts.Roles;
using FamilyTree.Domain.Authorization;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Roles;

/// <summary>
/// Roles are tenant-owned and read through the query filter, so another tenant's role id is
/// indistinguishable from a nonexistent one (design spec §4.4). Permissions are not
/// tenant-owned — the catalog is global by design.
/// </summary>
public sealed class RoleService(ApplicationDbContext context) : IRoleService
{
    public async Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken ct = default)
    {
        var roles = await context.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var ids = roles.Select(r => r.Id).ToList();

        var permissions = await PermissionsByRoleAsync(ids, ct);
        var counts = await UserCountsByRoleAsync(ids, ct);

        return roles.Select(r => Map(r, permissions, counts)).ToList();
    }

    public async Task<RoleResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var role = await context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return null;

        return Map(role,
            await PermissionsByRoleAsync([role.Id], ct),
            await UserCountsByRoleAsync([role.Id], ct));
    }

    public async Task<IReadOnlyList<PermissionResponse>> ListPermissionsAsync(
        CancellationToken ct = default) =>
        await context.Permissions.AsNoTracking()
            .OrderBy(p => p.Code)
            .Select(p => new PermissionResponse(p.Code, p.Description))
            .ToListAsync(ct);

    private async Task<ILookup<Guid, string>> PermissionsByRoleAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        var rows = await (
            from rolePermission in context.RolePermissions
            join permission in context.Permissions
                on rolePermission.PermissionId equals permission.Id
            where roleIds.Contains(rolePermission.RoleId)
            select new { rolePermission.RoleId, permission.Code })
            .ToListAsync(ct);

        return rows.ToLookup(r => r.RoleId, r => r.Code);
    }

    private async Task<Dictionary<Guid, int>> UserCountsByRoleAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        var rows = await context.UserRoles
            .Where(ur => roleIds.Contains(ur.RoleId))
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.RoleId, r => r.Count);
    }

    private static RoleResponse Map(
        Role role, ILookup<Guid, string> permissions, Dictionary<Guid, int> counts) =>
        new(role.Id, role.Name, role.Description, role.IsSystem,
            counts.GetValueOrDefault(role.Id),
            permissions[role.Id].OrderBy(c => c).ToList());
}
```

- [ ] **Step 7: Write the endpoints**

```csharp
using FamilyTree.Api.Authorization;
using FamilyTree.Application.Roles;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.Roles;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/roles").WithTags("Roles");

        group.MapGet("/", async (IRoleService roles, CancellationToken ct) =>
            Results.Ok(await roles.ListAsync(ct)))
            .RequirePermission(Permissions.Role.View);

        group.MapGet("/{id:guid}", async (Guid id, IRoleService roles, CancellationToken ct) =>
            await roles.GetAsync(id, ct) is { } role ? Results.Ok(role) : Results.NotFound())
            .RequirePermission(Permissions.Role.View);

        // Outside the /roles group: it is the catalog the role editor reads, not a role.
        app.MapGet("/api/v1/permissions", async (IRoleService roles, CancellationToken ct) =>
            Results.Ok(await roles.ListPermissionsAsync(ct)))
            .RequirePermission(Permissions.Role.View)
            .WithTags("Roles");

        return app;
    }
}
```

- [ ] **Step 8: Register in `Program.cs`**

```csharp
builder.Services.AddScoped<IRoleService, RoleService>();
```

```csharp
app.MapRoleEndpoints();
```

- [ ] **Step 9: Run to verify it passes**

Expected: all five PASS.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: list roles and the permission catalog"
```

---

## Task 10: Create, update, and delete custom roles

**Files:**
- Create: `src/FamilyTree.Contracts/Roles/SaveRoleRequest.cs`
- Modify: `src/FamilyTree.Application/Roles/IRoleService.cs`, `src/FamilyTree.Infrastructure/Roles/RoleService.cs`, `src/FamilyTree.Api/Endpoints/Roles/RoleEndpoints.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/RoleEndpointsTests.cs` (add)

**Interfaces:**
- Consumes: `IAdministratorGuard` (Task 6), `RoleResponse` (Task 9), `CreateUserRequest` (Task 5).
- Produces: `SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions)`; `IRoleService.CreateAsync`, `UpdateAsync`, `DeleteAsync`; error codes `ROLE_NOT_FOUND` (404), `ROLE_IS_SYSTEM` (400, thrown by the domain), `ROLE_NAME_TAKEN` (409), `PERMISSION_NOT_FOUND` (400), `ROLE_IN_USE` (409).

- [ ] **Step 1: Write the contract**

```csharp
namespace FamilyTree.Contracts.Roles;

/// <summary>
/// Permissions are sent as codes and replaced wholesale, matching how the role editor presents
/// them: a set of checkboxes whose final state is the request.
/// </summary>
public sealed record SaveRoleRequest(
    string Name, string? Description, IReadOnlyList<string> Permissions);
```

- [ ] **Step 2: Write the failing tests**

```csharp
    [Fact]
    public async Task Creating_a_custom_role_stores_its_permissions()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("أمناء العائلة", "يديرون الأفراد", ["Member.View", "Member.Edit"]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<RoleResponse>())!;
        created.IsSystem.Should().BeFalse();
        created.UserCount.Should().Be(0);
        created.Permissions.Should().BeEquivalentTo("Member.View", "Member.Edit");
    }

    [Fact]
    public async Task An_unknown_permission_code_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("أمناء العائلة", null, ["Member.Teleport"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("PERMISSION_NOT_FOUND");
    }

    [Fact]
    public async Task A_duplicate_role_name_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("Viewer", null, ["Member.View"]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("ROLE_NAME_TAKEN");
    }

    [Fact]
    public async Task A_system_role_cannot_be_updated()
    {
        await AuthenticateAsync();
        var roles = await _client.GetFromJsonAsync<List<RoleResponse>>("/api/v1/roles");
        var viewer = roles!.Single(r => r.Name == "Viewer");

        var response = await _client.PutAsJsonAsync($"/api/v1/roles/{viewer.Id}",
            new SaveRoleRequest("Viewer", null, ["Member.View", "Member.Delete"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("ROLE_IS_SYSTEM");
    }

    [Fact]
    public async Task A_system_role_cannot_be_deleted()
    {
        await AuthenticateAsync();
        var roles = await _client.GetFromJsonAsync<List<RoleResponse>>("/api/v1/roles");
        var viewer = roles!.Single(r => r.Name == "Viewer");

        var response = await _client.DeleteAsync($"/api/v1/roles/{viewer.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("ROLE_IS_SYSTEM");
    }

    [Fact]
    public async Task A_custom_role_can_be_updated_and_deleted()
    {
        await AuthenticateAsync();

        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("أمناء العائلة", null, ["Member.View"]));
        var role = (await create.Content.ReadFromJsonAsync<RoleResponse>())!;

        var update = await _client.PutAsJsonAsync($"/api/v1/roles/{role.Id}",
            new SaveRoleRequest("أمناء الشجرة", "وصف محدث", ["Member.View", "Member.Create"]));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await update.Content.ReadFromJsonAsync<RoleResponse>())!;
        updated.Name.Should().Be("أمناء الشجرة");
        updated.Permissions.Should().BeEquivalentTo("Member.View", "Member.Create");

        var delete = await _client.DeleteAsync($"/api/v1/roles/{role.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await _client.GetAsync($"/api/v1/roles/{role.Id}");
        after.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_role_that_still_has_members_cannot_be_deleted()
    {
        await AuthenticateAsync();

        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("أمناء العائلة", null, ["Member.View"]));
        var role = (await create.Content.ReadFromJsonAsync<RoleResponse>())!;

        var user = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [role.Id]));
        user.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.DeleteAsync($"/api/v1/roles/{role.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("ROLE_IN_USE");
    }
```

Add `using FamilyTree.Contracts.Users;` for `CreateUserRequest`.

- [ ] **Step 3: Run to verify they fail**

Expected: all seven FAIL with 404 or 405.

- [ ] **Step 4: Extend the interface**

```csharp
    Task<RoleResponse> CreateAsync(SaveRoleRequest request, CancellationToken ct = default);

    Task<RoleResponse> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
```

- [ ] **Step 5: Implement**

Change the declaration to:

```csharp
public sealed class RoleService(
    ApplicationDbContext context,
    ITenantContext tenant,
    IAdministratorGuard guard,
    TimeProvider timeProvider) : IRoleService
```

Add usings: `FamilyTree.Application.Common`, `FamilyTree.Application.Users`, `FamilyTree.Domain.Common`.

```csharp
    public async Task<RoleResponse> CreateAsync(
        SaveRoleRequest request, CancellationToken ct = default)
    {
        var permissionIds = await ResolvePermissionIdsAsync(request.Permissions, ct);
        await EnsureNameIsFreeAsync(request.Name, excludingRoleId: null, ct);

        // Role.Create validates the name and throws ROLE_NAME_REQUIRED / ROLE_NAME_TOO_LONG.
        var role = Role.Create(
            tenant.TenantId, request.Name, request.Description, timeProvider.GetUtcNow());

        context.Roles.Add(role);
        foreach (var permissionId in permissionIds)
            context.RolePermissions.Add(RolePermission.Create(role.Id, permissionId));

        await context.SaveChangesAsync(ct);
        return (await GetAsync(role.Id, ct))!;
    }

    public async Task<RoleResponse> UpdateAsync(
        Guid id, SaveRoleRequest request, CancellationToken ct = default)
    {
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("ROLE_NOT_FOUND", "No such role.");

        // Throws ROLE_IS_SYSTEM. Checked first so a rejected request leaves no partial effect
        // on the tracked graph.
        role.EnsureNotSystem();

        var permissionIds = await ResolvePermissionIdsAsync(request.Permissions, ct);
        await EnsureNameIsFreeAsync(request.Name, excludingRoleId: role.Id, ct);

        role.Rename(request.Name, timeProvider.GetUtcNow());

        var existing = await context.RolePermissions
            .Where(rp => rp.RoleId == role.Id).ToListAsync(ct);
        context.RolePermissions.RemoveRange(existing);
        foreach (var permissionId in permissionIds)
            context.RolePermissions.Add(RolePermission.Create(role.Id, permissionId));

        // A role edit can remove User.Edit or Role.Edit from everyone who holds it — the same
        // lockout the user-facing paths guard against (spec §4.9).
        await guard.EnsureAdministratorRemainsAsync(ct);

        await context.SaveChangesAsync(ct);
        return (await GetAsync(role.Id, ct))!;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("ROLE_NOT_FOUND", "No such role.");

        role.EnsureNotSystem();

        // Refuse rather than cascade: silently unassigning people would change what they can
        // do without anyone asking for that.
        if (await context.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, ct))
            throw new ConflictException("ROLE_IN_USE",
                "This role is still assigned to one or more users.");

        var permissions = await context.RolePermissions
            .Where(rp => rp.RoleId == role.Id).ToListAsync(ct);
        context.RolePermissions.RemoveRange(permissions);
        context.Roles.Remove(role);

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Maps codes to ids, rejecting any code the catalog does not contain. Sending a code that
    /// does not exist is a client bug, not a permission the tenant simply lacks.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolvePermissionIdsAsync(
        IReadOnlyList<string>? codes, CancellationToken ct)
    {
        var requested = (codes ?? []).Distinct().ToList();
        if (requested.Count == 0) return [];

        var found = await context.Permissions
            .Where(p => requested.Contains(p.Code))
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (found.Count != requested.Count)
            throw new DomainException("PERMISSION_NOT_FOUND", "One or more permissions do not exist.");

        return found;
    }

    private async Task EnsureNameIsFreeAsync(
        string? name, Guid? excludingRoleId, CancellationToken ct)
    {
        var trimmed = (name ?? string.Empty).Trim();
        // Left to Role.Create / Rename, which produce the proper ROLE_NAME_REQUIRED error.
        if (trimmed.Length == 0) return;

        // Filtered: another tenant's role of the same name is not a collision.
        var taken = await context.Roles.AnyAsync(
            r => r.Name == trimmed && (excludingRoleId == null || r.Id != excludingRoleId), ct);

        if (taken)
            throw new ConflictException("ROLE_NAME_TAKEN", "A role with that name already exists.");
    }
```

- [ ] **Step 6: Add the endpoints**

```csharp
        group.MapPost("/", async (
            SaveRoleRequest request, IRoleService roles, CancellationToken ct) =>
        {
            var created = await roles.CreateAsync(request, ct);
            return Results.Created($"/api/v1/roles/{created.Id}", created);
        })
            .RequirePermission(Permissions.Role.Create);

        group.MapPut("/{id:guid}", async (
            Guid id, SaveRoleRequest request, IRoleService roles, CancellationToken ct) =>
            Results.Ok(await roles.UpdateAsync(id, request, ct)))
            .RequirePermission(Permissions.Role.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id, IRoleService roles, CancellationToken ct) =>
        {
            await roles.DeleteAsync(id, ct);
            return Results.NoContent();
        })
            .RequirePermission(Permissions.Role.Delete);
```

Add `using FamilyTree.Contracts.Roles;`.

- [ ] **Step 7: Run the full backend suite**

```bash
dotnet test
```

Expected: everything passes.

- [ ] **Step 8: Mutation check**

Delete `role.EnsureNotSystem();` from `DeleteAsync`. `A_system_role_cannot_be_deleted` must FAIL. Restore it.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: create, update, and delete custom roles"
```

---

## Task 11: Users page

**Files:**
- Create: `frontend/src/features/users/types.ts`, `usersApi.ts`, `useUsers.ts`, `useRoleOptions.ts`, `UserForm.tsx`, `UsersPage.tsx`, `UsersPage.test.tsx`
- Modify: `frontend/src/routes/AppRoutes.tsx`, `frontend/src/app/AppShell.tsx`, `frontend/src/i18n/locales/ar.json`, `frontend/src/i18n/locales/en.json`

**Interfaces:**
- Consumes: the endpoints from Tasks 4–8.
- Produces: route `/users`; `usersApi` with `list`, `create`, `update`, `setActive`, `resetPassword`; `useRoleOptionsQuery` on query key `['roles']` — a temporary hook Task 12 deletes.

- [ ] **Step 1: Add the locale strings**

Add a `users` namespace to **both** locale files, keeping key order identical.

`ar.json`:

```json
  "users": {
    "title": "المستخدمون",
    "loading": "جارٍ تحميل المستخدمين…",
    "empty": "لا يوجد مستخدمون بعد.",
    "add": "إضافة مستخدم",
    "email": "البريد الإلكتروني",
    "password": "كلمة المرور المؤقتة",
    "roles": "الأدوار",
    "status": "الحالة",
    "active": "نشط",
    "inactive": "معطّل",
    "lastLogin": "آخر دخول",
    "neverSignedIn": "لم يسجّل الدخول بعد",
    "pendingPasswordChange": "بانتظار تغيير كلمة المرور",
    "edit": "تعديل",
    "deactivate": "تعطيل",
    "activate": "تفعيل",
    "resetPassword": "إعادة تعيين كلمة المرور",
    "save": "حفظ",
    "saving": "جارٍ الحفظ…",
    "cancel": "إلغاء",
    "confirmDeactivate": "تعطيل {{email}}؟"
  },
```

`en.json`:

```json
  "users": {
    "title": "Users",
    "loading": "Loading users…",
    "empty": "No users yet.",
    "add": "Add user",
    "email": "Email",
    "password": "Temporary password",
    "roles": "Roles",
    "status": "Status",
    "active": "Active",
    "inactive": "Deactivated",
    "lastLogin": "Last sign-in",
    "neverSignedIn": "Has not signed in yet",
    "pendingPasswordChange": "Password change pending",
    "edit": "Edit",
    "deactivate": "Deactivate",
    "activate": "Activate",
    "resetPassword": "Reset password",
    "save": "Save",
    "saving": "Saving…",
    "cancel": "Cancel",
    "confirmDeactivate": "Deactivate {{email}}?"
  },
```

Add to the existing `errors` namespace in `ar.json`:

```json
    "PASSWORD_CHANGE_REQUIRED": "يجب تغيير كلمة المرور قبل المتابعة.",
    "PASSWORD_INCORRECT": "كلمة المرور الحالية غير صحيحة.",
    "PASSWORD_TOO_SHORT": "كلمة المرور قصيرة جدًا (١٢ حرفًا على الأقل).",
    "USER_EMAIL_REQUIRED": "البريد الإلكتروني مطلوب.",
    "USER_EMAIL_INVALID": "البريد الإلكتروني غير صالح.",
    "USER_EMAIL_TAKEN": "هذا البريد الإلكتروني مستخدم بالفعل.",
    "USER_NOT_FOUND": "هذا المستخدم لم يعد موجودًا. أعد التحميل وحاول مجددًا.",
    "LAST_ADMINISTRATOR": "لا يمكن تنفيذ هذا التغيير لأنه لن يبقى أحد قادرًا على إدارة المستخدمين والأدوار.",
    "ROLE_NOT_FOUND": "هذا الدور لم يعد موجودًا. أعد التحميل وحاول مجددًا.",
    "ROLE_IS_SYSTEM": "لا يمكن تعديل الأدوار الأساسية أو حذفها.",
    "ROLE_NAME_REQUIRED": "اسم الدور مطلوب.",
    "ROLE_NAME_TOO_LONG": "اسم الدور طويل جدًا (١٠٠ حرف كحد أقصى).",
    "ROLE_NAME_TAKEN": "يوجد دور بهذا الاسم بالفعل.",
    "ROLE_IN_USE": "لا يمكن حذف هذا الدور لأنه معيّن لمستخدمين.",
    "PERMISSION_NOT_FOUND": "صلاحية غير معروفة. أعد التحميل وحاول مجددًا."
```

And to `en.json`:

```json
    "PASSWORD_CHANGE_REQUIRED": "You must change your password before continuing.",
    "PASSWORD_INCORRECT": "The current password is incorrect.",
    "PASSWORD_TOO_SHORT": "That password is too short (12 characters minimum).",
    "USER_EMAIL_REQUIRED": "An email address is required.",
    "USER_EMAIL_INVALID": "That email address is not valid.",
    "USER_EMAIL_TAKEN": "That email address is already in use.",
    "USER_NOT_FOUND": "That user no longer exists. Reload and try again.",
    "LAST_ADMINISTRATOR": "This change would leave no one able to manage users and roles.",
    "ROLE_NOT_FOUND": "That role no longer exists. Reload and try again.",
    "ROLE_IS_SYSTEM": "Built-in roles cannot be changed or deleted.",
    "ROLE_NAME_REQUIRED": "A role name is required.",
    "ROLE_NAME_TOO_LONG": "That role name is too long (100 characters maximum).",
    "ROLE_NAME_TAKEN": "A role with that name already exists.",
    "ROLE_IN_USE": "This role cannot be deleted because it is assigned to users.",
    "PERMISSION_NOT_FOUND": "Unknown permission. Reload and try again."
```

- [ ] **Step 2: Write the types**

```ts
export type UserRoleSummary = {
  id: string
  name: string
}

export type User = {
  id: string
  email: string
  isActive: boolean
  mustChangePassword: boolean
  lastLoginAt: string | null
  roles: UserRoleSummary[]
}
```

- [ ] **Step 3: Write the API module**

```ts
import { apiFetch } from '../../services/apiClient'
import type { User } from './types'

const USERS = '/api/v1/users'

export const usersApi = {
  list: (): Promise<User[]> => apiFetch<User[]>(USERS),

  create: (email: string, password: string, roleIds: string[]): Promise<User> =>
    apiFetch<User>(USERS, {
      method: 'POST',
      body: JSON.stringify({ email, password, roleIds }),
    }),

  update: (id: string, email: string, roleIds: string[]): Promise<User> =>
    apiFetch<User>(`${USERS}/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ email, roleIds }),
    }),

  setActive: (id: string, isActive: boolean): Promise<User> =>
    apiFetch<User>(`${USERS}/${id}/${isActive ? 'activate' : 'deactivate'}`, { method: 'POST' }),

  resetPassword: (id: string, password: string): Promise<User> =>
    apiFetch<User>(`${USERS}/${id}/password`, {
      method: 'POST',
      body: JSON.stringify({ password }),
    }),
}
```

- [ ] **Step 4: Write the hooks**

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { usersApi } from './usersApi'
import type { User } from './types'

export const userKeys = {
  all: ['users'] as const,
}

export const useUsersQuery = () =>
  useQuery<User[]>({ queryKey: userKeys.all, queryFn: () => usersApi.list() })

/**
 * Invalidates roles too: a role's userCount changes whenever an assignment changes, so a
 * cached roles list would show a stale count immediately after editing a user.
 */
const useInvalidateUsers = () => {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: userKeys.all })
    void queryClient.invalidateQueries({ queryKey: ['roles'] })
  }
}

export const useCreateUser = () => {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ email, password, roleIds }: {
      email: string; password: string; roleIds: string[]
    }) => usersApi.create(email, password, roleIds),
    onSuccess: invalidate,
  })
}

export const useUpdateUser = () => {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, email, roleIds }: { id: string; email: string; roleIds: string[] }) =>
      usersApi.update(id, email, roleIds),
    onSuccess: invalidate,
  })
}

export const useSetUserActive = () => {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      usersApi.setActive(id, isActive),
    onSuccess: invalidate,
  })
}

export const useResetUserPassword = () => {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, password }: { id: string; password: string }) =>
      usersApi.resetPassword(id, password),
    onSuccess: invalidate,
  })
}
```

- [ ] **Step 5: Write the temporary role-options hook**

`frontend/src/features/users/useRoleOptions.ts` — the user form needs role names before Task 12 exists. Task 12 deletes this file.

```ts
import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '../../services/apiClient'

export type RoleOption = { id: string; name: string }

/**
 * Temporary: replaced by useRolesQuery in the roles feature. Same query key, so the cache
 * entry is shared and the swap is invisible to consumers.
 */
export const useRoleOptionsQuery = () =>
  useQuery<RoleOption[]>({
    queryKey: ['roles'],
    queryFn: () => apiFetch<RoleOption[]>('/api/v1/roles'),
  })
```

- [ ] **Step 6: Write the page and form**

**Read `frontend/src/features/members/MembersPage.tsx` and `MemberForm.tsx` first** and match their structure, styling approach, loading/empty/error handling, and RTL conventions. Do not invent a new layout — these pages must look like siblings.

`UsersPage.tsx` must:

- render `t('users.title')` as a heading
- show `t('users.loading')` while pending and `t('users.empty')` when the list is empty
- render one row per user: email, role names, `t('users.active')` / `t('users.inactive')`, and `t('users.pendingPasswordChange')` when `mustChangePassword` is true
- show `lastLoginAt` using whatever date formatting the codebase already uses, or `t('users.neverSignedIn')` when null
- offer Edit (`User.Edit`), Activate/Deactivate (`User.Deactivate`), and Reset password (`User.Edit`) per row, plus Add (`User.Create`) — each gated with `hasPermission(...)` from `AuthContext`
- confirm deactivation with `t('users.confirmDeactivate', { email })`
- surface a failed mutation by mapping `ApiError.code` through `t('errors.' + code)`, exactly as `MembersPage` does

`UserForm.tsx` handles create and edit: an email field, a temporary-password field **only in create mode**, and a multi-select of roles fed by `useRoleOptionsQuery()`.

- [ ] **Step 7: Write the tests**

`UsersPage.test.tsx`, modeled on `MembersPage.test.tsx` (read it first). Cover these four cases with real assertions:

1. **Lists users with roles and status.** Two mocked users — one active with role "Viewer", one deactivated. Assert both emails, the role name, and both Arabic status strings render.
2. **Marks a user who still owes a password change.** One user flagged, one not. Assert `users.pendingPasswordChange` renders for the flagged row **and not** for the other — without the negative half this passes even if the badge renders unconditionally.
3. **Gates the add button on `User.Create`.** Render without the permission and assert the button is absent; render with it and assert the button is present. A one-sided assertion passes even if the button never renders at all.
4. **Shows a translated refusal.** Mock `setActive` to reject with `new ApiError('LAST_ADMINISTRATOR', 409)`; assert the Arabic `LAST_ADMINISTRATOR` string renders.

Read every Arabic value out of `ar.json`. Do not type Arabic from memory — a wrong string in a negative assertion passes vacuously.

- [ ] **Step 8: Wire the route and nav**

In `AppRoutes.tsx`, before the catch-all:

```tsx
    <Route
      path="/users"
      element={
        <ProtectedRoute>
          <UsersPage />
        </ProtectedRoute>
      }
    />
```

In `AppShell.tsx`, replace the `User.View`-gated `PendingNavItem` with a real `Link` to `/users`, matching the `/members` link exactly — same `navItemStyle(pathname === '/users', true)` shape and the same icon.

- [ ] **Step 9: Run the frontend suite**

```bash
cd frontend && npm test -- --run
```

Expected: all pass. If `AppShell.test.tsx` asserted the users nav item was disabled, update that assertion — the behaviour it described has intentionally changed.

- [ ] **Step 10: Lint**

```bash
cd frontend && npx oxlint
```

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: add the users management page"
```

---

## Task 12: Roles page

**Files:**
- Create: `frontend/src/features/roles/types.ts`, `rolesApi.ts`, `useRoles.ts`, `RoleForm.tsx`, `RolesPage.tsx`, `RolesPage.test.tsx`
- Delete: `frontend/src/features/users/useRoleOptions.ts`
- Modify: `frontend/src/features/users/UserForm.tsx`, `frontend/src/routes/AppRoutes.tsx`, `frontend/src/app/AppShell.tsx`, both locale files

**Interfaces:**
- Consumes: the endpoints from Tasks 9–10.
- Produces: route `/roles`; `useRolesQuery` on query key `['roles']` — the same key Task 11's temporary hook used, so the swap is transparent to the cache.

- [ ] **Step 1: Add the locale strings**

`ar.json`:

```json
  "roles": {
    "title": "الأدوار والصلاحيات",
    "loading": "جارٍ تحميل الأدوار…",
    "empty": "لا توجد أدوار بعد.",
    "add": "إضافة دور",
    "name": "اسم الدور",
    "description": "الوصف",
    "permissions": "الصلاحيات",
    "members": "عدد المستخدمين",
    "systemRole": "دور أساسي",
    "systemRoleHint": "الأدوار الأساسية غير قابلة للتعديل.",
    "edit": "تعديل",
    "delete": "حذف",
    "save": "حفظ",
    "saving": "جارٍ الحفظ…",
    "cancel": "إلغاء",
    "confirmDelete": "حذف الدور {{name}}؟"
  },
```

`en.json`:

```json
  "roles": {
    "title": "Roles and permissions",
    "loading": "Loading roles…",
    "empty": "No roles yet.",
    "add": "Add role",
    "name": "Role name",
    "description": "Description",
    "permissions": "Permissions",
    "members": "Users",
    "systemRole": "Built-in role",
    "systemRoleHint": "Built-in roles cannot be changed.",
    "edit": "Edit",
    "delete": "Delete",
    "save": "Save",
    "saving": "Saving…",
    "cancel": "Cancel",
    "confirmDelete": "Delete the role {{name}}?"
  },
```

- [ ] **Step 2: Write the types and API module**

```ts
export type Role = {
  id: string
  name: string
  description: string | null
  isSystem: boolean
  userCount: number
  permissions: string[]
}

export type Permission = {
  code: string
  description: string
}
```

```ts
import { apiFetch } from '../../services/apiClient'
import type { Permission, Role } from './types'

const ROLES = '/api/v1/roles'

export const rolesApi = {
  list: (): Promise<Role[]> => apiFetch<Role[]>(ROLES),

  permissions: (): Promise<Permission[]> => apiFetch<Permission[]>('/api/v1/permissions'),

  create: (name: string, description: string | null, permissions: string[]): Promise<Role> =>
    apiFetch<Role>(ROLES, {
      method: 'POST',
      body: JSON.stringify({ name, description, permissions }),
    }),

  update: (
    id: string, name: string, description: string | null, permissions: string[],
  ): Promise<Role> =>
    apiFetch<Role>(`${ROLES}/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name, description, permissions }),
    }),

  remove: (id: string): Promise<void> => apiFetch<void>(`${ROLES}/${id}`, { method: 'DELETE' }),
}
```

- [ ] **Step 3: Write the hooks**

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { rolesApi } from './rolesApi'
import type { Permission, Role } from './types'

export const roleKeys = {
  all: ['roles'] as const,
  permissions: ['permissions'] as const,
}

export const useRolesQuery = () =>
  useQuery<Role[]>({ queryKey: roleKeys.all, queryFn: () => rolesApi.list() })

/** The catalog is fixed for the life of a deployment, so it never needs refetching. */
export const usePermissionsQuery = () =>
  useQuery<Permission[]>({
    queryKey: roleKeys.permissions,
    queryFn: () => rolesApi.permissions(),
    staleTime: Infinity,
  })

/**
 * Invalidates users as well: changing a role's permissions changes what its members can do,
 * and renaming or deleting one changes what the users list displays.
 */
const useInvalidateRoles = () => {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: roleKeys.all })
    void queryClient.invalidateQueries({ queryKey: ['users'] })
  }
}

export const useCreateRole = () => {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: ({ name, description, permissions }: {
      name: string; description: string | null; permissions: string[]
    }) => rolesApi.create(name, description, permissions),
    onSuccess: invalidate,
  })
}

export const useUpdateRole = () => {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: ({ id, name, description, permissions }: {
      id: string; name: string; description: string | null; permissions: string[]
    }) => rolesApi.update(id, name, description, permissions),
    onSuccess: invalidate,
  })
}

export const useDeleteRole = () => {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: (id: string) => rolesApi.remove(id),
    onSuccess: invalidate,
  })
}
```

- [ ] **Step 4: Retire the temporary hook**

Delete `frontend/src/features/users/useRoleOptions.ts` and change `UserForm.tsx` to import `useRolesQuery` from `../roles/useRoles`. The query key is unchanged, so nothing else moves. Confirm no other file imported the deleted module.

- [ ] **Step 5: Write the page and form**

`RolesPage.tsx` follows `MembersPage.tsx`'s structure. It must:

- render one row per role: name, description, `userCount`, and a `t('roles.systemRole')` badge when `isSystem`
- gate Add on `Role.Create`, Edit on `Role.Edit`, Delete on `Role.Delete`
- **not offer Edit or Delete on a system role at all** — the server rejects both, and an action that always fails is worse than no action. Show `t('roles.systemRoleHint')` in their place.
- confirm deletion with `t('roles.confirmDelete', { name })`
- map `ApiError.code` through `t('errors.' + code)`

`RoleForm.tsx` renders name, description, and one checkbox per permission from `usePermissionsQuery()`, grouped by the code's prefix (`FamilyTree`, `Member`, `User`, `Role`, `Audit`, `PublicLink`). Group headers may use the raw prefix — permission codes are developer-facing identifiers, and the catalog's `description` carries the human text.

- [ ] **Step 6: Write the tests**

`RolesPage.test.tsx` covering, with real assertions:

1. **Lists roles with user counts.** Two roles — one system with `userCount: 1`, one custom with `userCount: 0`. Assert both names and both counts render.
2. **Offers no edit or delete on a built-in role.** Assert the system role's row has neither action **and** the custom role's row has both. Without the second half this passes even if the actions never render anywhere.
3. **Shows a translated refusal.** Mock `remove` to reject with `new ApiError('ROLE_IN_USE', 409)`; assert the Arabic string renders.
4. **Groups permission checkboxes.** Open the create form with a mocked catalog of `Member.View` and `User.View`; assert both checkboxes render.

Read the Arabic values from `ar.json`.

- [ ] **Step 7: Wire the route and nav**

Add the `/roles` route to `AppRoutes.tsx` inside `ProtectedRoute`, and replace the `Role.View`-gated `PendingNavItem` in `AppShell.tsx` with a `Link` to `/roles`.

- [ ] **Step 8: Run the frontend suite and lint**

```bash
cd frontend && npm test -- --run && npx oxlint
```

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: add the roles and permissions page"
```

---

## Task 13: Forced password-change screen, docs, and end-to-end verification

**Files:**
- Create: `frontend/src/features/auth/ChangePasswordPage.tsx`, `ChangePasswordPage.test.tsx`
- Modify: `frontend/src/features/auth/AuthContext.tsx`, `frontend/src/routes/AppRoutes.tsx`, `frontend/src/routes/ProtectedRoute.tsx`, both locale files
- Modify: `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md` (§8 Phase 4 row), `README.md`, and this plan file (append verification findings)

**Interfaces:**
- Consumes: `CurrentUserResponse.mustChangePassword` and `POST /api/v1/me/password` (Task 3).

- [ ] **Step 1: Add the locale strings**

Add to the existing `auth` namespace.

`ar.json`:

```json
    "changePasswordTitle": "اختر كلمة مرور جديدة",
    "changePasswordSubtitle": "تم تعيين كلمة المرور الحالية من قبل المسؤول. اختر كلمة مرور خاصة بك للمتابعة.",
    "currentPassword": "كلمة المرور الحالية",
    "newPassword": "كلمة المرور الجديدة",
    "confirmPassword": "تأكيد كلمة المرور",
    "passwordMismatch": "كلمتا المرور غير متطابقتين.",
    "changePassword": "تغيير كلمة المرور",
    "changingPassword": "جارٍ التغيير…"
```

`en.json`:

```json
    "changePasswordTitle": "Choose a new password",
    "changePasswordSubtitle": "Your current password was set by an administrator. Choose your own to continue.",
    "currentPassword": "Current password",
    "newPassword": "New password",
    "confirmPassword": "Confirm password",
    "passwordMismatch": "The passwords do not match.",
    "changePassword": "Change password",
    "changingPassword": "Changing…"
```

- [ ] **Step 2: Expose the flag from `AuthContext`**

Read `AuthContext.tsx` first. It already fetches `/api/v1/me` and exposes `hasPermission`. Add `mustChangePassword` to the context value from the same response, defaulting to `false` while the query is pending — a default of `true` would flash the change-password screen on every page load.

- [ ] **Step 3: Write the failing test**

`ChangePasswordPage.test.tsx`, modeled on `LoginPage.test.tsx`, covering with real assertions:

1. **Rejects a mismatched confirmation without calling the API.** Fill current, new, and a different confirmation; submit. Assert `t('auth.passwordMismatch')` renders **and** the API mock was never called — the second assertion is what proves the check runs before the request.
2. **Submits the change.** Fill all three consistently; assert the API mock was called with the current and new password.
3. **Shows a translated failure.** Mock the call to reject with `new ApiError('PASSWORD_INCORRECT', 400)`; assert the Arabic string from `ar.json` renders.

- [ ] **Step 4: Run to verify it fails**

```bash
cd frontend && npm test -- --run ChangePasswordPage
```

Expected: FAIL — the module does not exist.

- [ ] **Step 5: Write the page**

`ChangePasswordPage.tsx`, modeled on `LoginPage.tsx` (read it first — match its layout, styling, and error display). Three password fields, a client-side match check before submitting, and a call to `POST /api/v1/me/password` via `apiFetch`. On success, invalidate the `/me` query so `mustChangePassword` flips and the redirect below releases.

- [ ] **Step 6: Gate the app on the flag**

In `ProtectedRoute.tsx`, after the existing authentication check, redirect to `/change-password` when `mustChangePassword` is true and the current path is not already `/change-password`. Register the route in `AppRoutes.tsx` inside `ProtectedRoute`, so an unauthenticated visitor still lands on login first.

This mirrors the server gate; it does not replace it. The server is the enforcement point (§9) — this exists so the user sees the right screen instead of a wall of 403s.

- [ ] **Step 7: Run both suites**

```bash
cd frontend && npm test -- --run && npx oxlint
cd .. && dotnet test
```

Expected: everything passes.

- [ ] **Step 8: Update the spec's delivery table**

In §8, replace the Phase 4 row, following the pattern the Phase 3 row sets:

```
| 4 — Authorization | User management (list, create, update, activate/deactivate, administrator password reset), role management including custom roles, the permission catalog endpoint, server-enforced first-login password change, and the last-administrator lockout guard (§4.9). Permissions, the resolver, `RequirePermission`, and the four system roles were built in Phase 1 and are unchanged. Audit writes deferred to Phase 5. |
```

- [ ] **Step 9: Update the README**

Set "Current phase" to Phase 4 and add a **User and role management** section covering: how the first administrator is seeded; that an administrator creates users with a temporary password the user must replace at first sign-in; that built-in roles cannot be edited or deleted; and that the system refuses any change leaving no one able to manage users and roles.

- [ ] **Step 10: Verify against the real database**

Start the API and frontend against the seeded tenant and check by hand:

1. Sign in as the seeded administrator — Users and Roles are now links, not disabled items.
2. Create a user with the Viewer role and a temporary password.
3. Sign in as that user in a private window — you land on the change-password screen and cannot navigate away.
4. Change the password — you land on the tree, and Users/Roles are absent (Viewer holds neither `User.View` nor `Role.View`).
5. Back as administrator: create a custom role, assign it, edit its permissions, then try to delete it while assigned — it refuses with the translated `ROLE_IN_USE` message.
6. Try to deactivate the seeded administrator — it refuses with the translated `LAST_ADMINISTRATOR` message.
7. Confirm the Arabic UI reads correctly right-to-left on both new pages.

Append a "Task 13 — verification findings" section to this plan file recording what you actually observed, **including anything that did not work**. Measurements, not assurances.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: force a password change at first sign-in and document Phase 4"
```

---

## Self-Review

**1. Spec coverage.** §4.9's endpoint list maps to Tasks 4, 5, 7, 8 (users), 9, 10 (roles), and 9 (permission catalog). Its password rule is Tasks 1–3 and 13. Its lockout guard is Task 6, applied in Tasks 7, 8, and 10. §4.4's uniform 404 is asserted in Tasks 4, 7, 9, and 10. §4.8's error codes all receive locale entries in Task 11. §4.3's permission-not-name rule is enforced by Task 6's fourth test. The staleness paragraph and the audit deferral are documentation, recorded in Task 13's spec row. No §4.9 requirement is unassigned.

**2. Placeholder scan — one deliberate deviation, flagged.** Tasks 11, 12, and 13 describe three page components and their tests as enumerated behavioural requirements rather than verbatim JSX. This violates the skill's "No Placeholders" rule and I am flagging it rather than hiding it. The reason: these pages must match `MembersPage.tsx`'s house style closely, and transcribing 200 lines of component code I could not check against the real file would produce confident, wrong code — the failure mode that cost three rounds in Phase 3. Every behaviour, permission gate, string key, and test case is enumerated, and each task names the exact file to read and copy from. **If an implementer reports these tasks as under-specified, that is a fair finding; the answer is to read the neighbouring file, not to invent a new layout.**

**3. Type consistency.** `UserResponse` is defined once (Task 4, six members) and returned unchanged by Tasks 5, 7, and 8. `RoleResponse` is defined once (Task 9, six members) and returned by Task 10. `IUserService` accumulates methods across Tasks 4, 5, 7, 8 with no signature redefined; `IRoleService` likewise across Tasks 9 and 10. `ValidateEmail`, `ValidateRoleIdsAsync`, and `UniqueViolation` are defined in Task 5 and reused in Task 7. The test helpers `AuthenticateAsync`, `RoleIdAsync`, and `CodeOf` are defined in Task 4's test class and reused by Tasks 5, 7, 8.

**Two duplications are deliberate; a reviewer should confirm they stay in step:**
- `MinimumPasswordLength = 12` lives on `UserService` (Task 5), but Task 3's `/me/password` endpoint declares its own local `const int minimumPasswordLength = 12`, because `MeEndpoints` predates `UserService` and the Api layer should not reach into Infrastructure for a constant. **These two values must match.**
- `RevokeRefreshTokensAsync` (Task 8) duplicates the revoke loop written inline in Task 3's endpoint. The duplication crosses a layer boundary and is left in place rather than forced into a shared helper.

**4. Risks carried into execution.**
- **Task 6 is the riskiest task in the plan.** Whether EF Core's LINQ observes pending `RemoveRange` on tracked sets is an empirical question I could not settle while writing this. The task states the risk, tells the implementer exactly what to do if the tests fail, and forbids weakening the tests to match a guard that reads stale state.
- **Task 2's middleware ordering** assumes `Program.cs` calls `UseAuthorization()`. The task says to adapt and report if the pipeline is composed differently.
- **`RolePermission.Create` and `Permission.Description`** are used in Tasks 6, 9, and 10 without my having read those two files. Both tasks open with an explicit step to verify the real signatures first.
- **The `18` permission count** in Task 9 is read from `Permissions.All` as of writing; the task says to re-verify before relying on it.
