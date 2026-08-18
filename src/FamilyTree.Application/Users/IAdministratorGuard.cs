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
    /// <remarks>
    /// The caller must stage its change, call this, and save — all within <b>one transaction</b>
    /// on the <b>same scoped DbContext</b>:
    ///
    /// <code>
    /// await using var tx = await context.Database.BeginTransactionAsync(ct);
    /// user.IsActive = false;                        // stage
    /// await guard.EnsureAdministratorRemainsAsync(ct);
    /// await context.SaveChangesAsync(ct);
    /// await tx.CommitAsync(ct);
    /// </code>
    ///
    /// Same context, because the guard reads pending changes from that context's change tracker;
    /// one transaction, because it serializes concurrent callers on a transaction-scoped
    /// per-tenant lock, and without the transaction two racing requests could each remove a
    /// different last administrator. Calling with no ambient transaction throws
    /// <see cref="InvalidOperationException"/> rather than proceeding unguarded.
    ///
    /// The staged change must be made through tracked entities. ExecuteUpdateAsync,
    /// ExecuteDeleteAsync and raw SQL bypass the change tracker and are invisible here.
    /// </remarks>
    Task EnsureAdministratorRemainsAsync(CancellationToken ct = default);
}
