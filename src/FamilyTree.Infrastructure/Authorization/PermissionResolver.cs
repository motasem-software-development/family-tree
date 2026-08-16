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
