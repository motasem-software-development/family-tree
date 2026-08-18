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
