using System.Data;
using System.Data.Common;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace FamilyTree.Infrastructure.FamilyMembers;

/// <summary>
/// Answers one question: would attaching <c>member_id</c> under <c>parent_id</c> close a loop?
///
/// Raw SQL because EF Core cannot express WITH RECURSIVE, and the same caveat as
/// <see cref="FamilyMemberSearchQuery"/> applies — the EF global query filter (layer 1 of the
/// three-layer tenant isolation in design spec §3.2) does not reach raw SQL, so every table
/// reference carries an explicit tenant_id predicate, the recursive term included. Without it
/// a walk starting on a permitted row could climb into another tenant's ancestry.
/// </summary>
internal static class CycleCheckQuery
{
    /// <summary>
    /// Far past any real genealogy. The walk already terminates on acyclic data — which is the
    /// invariant this check exists to preserve — so the bound is not part of the correctness
    /// argument. It exists to cut off the walk on data that is already corrupted, but the cutoff
    /// is fail-OPEN, not fail-closed: past this depth the CTE simply stops climbing and EXISTS
    /// reports false, so the check degrades to "no cycle found within 100 generations" rather
    /// than surfacing an error. A corrupted or deeper-than-100 ancestry lets the move through
    /// silently instead of hanging the connection.
    /// </summary>
    private const int MaxDepth = 100;

    /// <summary>
    /// The walk starts at the PROPOSED PARENT and climbs. If it reaches the member being
    /// moved, that member is an ancestor of its own proposed parent, so the move would close a
    /// loop. Starting at the parent rather than the member is also what makes the self-move
    /// case fall out for free: the first row is then the member itself.
    /// </summary>
    private const string Sql = """
        WITH RECURSIVE chain AS (
            SELECT id, parent_id, 1 AS depth
            FROM family_members
            WHERE id = @parent_id AND tenant_id = @tenant_id
            UNION ALL
            SELECT m.id, m.parent_id, c.depth + 1
            FROM chain c
            JOIN family_members m ON m.id = c.parent_id AND m.tenant_id = @tenant_id
            WHERE c.depth < @max_depth
        )
        SELECT EXISTS (SELECT 1 FROM chain WHERE id = @member_id);
        """;

    public static async Task<bool> WouldCreateCycleAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid memberId,
        Guid proposedParentId,
        CancellationToken ct)
    {
        // An empty tenant id is an unauthenticated caller. Fail closed — report a cycle — so a
        // caller who may see nothing cannot move anything either.
        if (tenantId == Guid.Empty) return true;

        var connection = context.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;

            // The service runs this inside its move transaction. A DbCommand built from the
            // connection is not enlisted automatically, and without this it would read outside
            // the transaction — a different snapshot from the one the update writes.
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();

            AddParameter(command, "tenant_id", NpgsqlDbType.Uuid, tenantId);
            AddParameter(command, "member_id", NpgsqlDbType.Uuid, memberId);
            AddParameter(command, "parent_id", NpgsqlDbType.Uuid, proposedParentId);
            AddParameter(command, "max_depth", NpgsqlDbType.Integer, MaxDepth);

            return Convert.ToBoolean(await command.ExecuteScalarAsync(ct));
        }
        finally
        {
            // Close only what this method opened: inside the move transaction the connection
            // belongs to the caller, and closing it there would abort the transaction.
            if (opened) await context.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, NpgsqlDbType type, object value)
    {
        var parameter = new NpgsqlParameter(name, type) { Value = value };
        command.Parameters.Add(parameter);
    }
}
