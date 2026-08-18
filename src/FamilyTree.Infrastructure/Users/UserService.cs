using FamilyTree.Application.Auth;
using FamilyTree.Application.Common;
using FamilyTree.Application.Users;
using FamilyTree.Contracts.Users;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Common;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyTree.Infrastructure.Users;

/// <summary>
/// Every query runs through the tenant query filter on ApplicationUser, so "no such user" and
/// "another tenant's user" are the same code path — which makes the uniform 404 in design
/// spec §4.4 true by construction rather than by discipline.
/// </summary>
public sealed class UserService(
    ApplicationDbContext context,
    ITenantContext tenant,
    IPasswordHasher<ApplicationUser> passwordHasher,
    TimeProvider timeProvider,
    IAdministratorGuard guard) : IUserService
{
    /// <summary>PostgreSQL SQLSTATE for a unique violation.</summary>
    private const string UniqueViolation = "23505";

    public async Task<UserResponse> CreateAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        var email = ValidateEmail(request.Email);

        if ((request.Password ?? string.Empty).Length < PasswordPolicy.MinimumLength)
            throw new DomainException("PASSWORD_TOO_SHORT",
                $"A password must be at least {PasswordPolicy.MinimumLength} characters.");

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

    public async Task<UserResponse> UpdateAsync(
        Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        // IAdministratorGuard needs an ambient transaction on this same DbContext: it takes a
        // per-tenant advisory lock held for the transaction's lifetime to close the TOCTOU
        // window where two concurrent requests each remove a different administrator.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

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

        // Roles are replaced wholesale through tracked entities, not ExecuteDeleteAsync /
        // ExecuteUpdateAsync: those bypass the change tracker and would make the guard below
        // fail open, reporting an administrator remains when the change tracker (and thus the
        // guard) never saw the removal.
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
            // The check above is check-then-act and can lose a race. The `await using`
            // transaction above is never committed on this path, so it rolls back on dispose
            // and leaves no partial change behind.
            throw new ConflictException("USER_EMAIL_TAKEN", "That email address is already in use.");
        }

        await transaction.CommitAsync(ct);

        return (await GetAsync(user.Id, ct))!;
    }

    public async Task<UserResponse> SetActiveAsync(
        Guid id, bool isActive, CancellationToken ct = default)
    {
        // IAdministratorGuard needs an ambient transaction on this same DbContext: it takes a
        // per-tenant advisory lock held for the transaction's lifetime to close the TOCTOU
        // window where two concurrent requests each remove a different administrator.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "No such user.");

        user.IsActive = isActive;

        if (!isActive)
        {
            // Staged, not saved: the guard evaluates the state this request is asking for.
            await RevokeRefreshTokensAsync(user.Id, ct);
            await guard.EnsureAdministratorRemainsAsync(ct);
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return (await GetAsync(user.Id, ct))!;
    }

    public async Task<UserResponse> ResetPasswordAsync(
        Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "No such user.");

        if ((request.Password ?? string.Empty).Length < PasswordPolicy.MinimumLength)
            throw new DomainException("PASSWORD_TOO_SHORT",
                $"A password must be at least {PasswordPolicy.MinimumLength} characters.");

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
