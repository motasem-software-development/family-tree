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
