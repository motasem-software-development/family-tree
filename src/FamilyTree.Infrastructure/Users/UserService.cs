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
