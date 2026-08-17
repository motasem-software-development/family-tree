using System.Data.Common;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace FamilyTree.Infrastructure.FamilyMembers;

/// <summary>
/// The single raw-SQL surface in the codebase, isolated here so the tenant-safety argument
/// lives in one reviewable file.
///
/// Raw SQL rather than LINQ because EF Core cannot express WITH RECURSIVE, and the ancestor
/// path is the whole point of the endpoint (design spec §5.4). The cost of that choice is
/// that the EF global query filter — layer 1 of the three-layer tenant isolation in design
/// spec §3.2 — does not apply. Every table reference below therefore carries an explicit
/// tenant_id predicate, including inside the recursive term: without it, a walk that starts
/// on a permitted row could climb into another tenant's ancestry.
/// </summary>
internal static class FamilyMemberSearchQuery
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    /// <summary>
    /// LIKE metacharacters survive parameterisation — a parameter binds the pattern, not its
    /// meaning — so a user typing "%" would otherwise match every member. Backslash first, or
    /// it would escape the escapes added after it.
    /// </summary>
    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    private const string CountSql = """
        SELECT count(*)
        FROM family_members
        WHERE tenant_id = @tenant_id
          AND name ILIKE @pattern ESCAPE '\';
        """;

    /// <summary>
    /// `page` selects the requested slice and stamps each hit with its position, so the
    /// ordering survives the join that follows. `chain` starts at each hit (up = 0) and walks
    /// parent_id upward one generation per iteration; the walk terminates naturally at a root,
    /// whose parent_id is null and joins to nothing.
    ///
    /// The final ORDER BY replays the page order, and `up DESC` puts each chain root-first —
    /// so the reader can consume rows in a single forward pass with no sorting in C#.
    /// </summary>
    private const string PageSql = """
        WITH RECURSIVE page AS (
            SELECT id, row_number() OVER (ORDER BY name, id) AS ord
            FROM family_members
            WHERE tenant_id = @tenant_id
              AND name ILIKE @pattern ESCAPE '\'
            ORDER BY name, id
            LIMIT @limit OFFSET @offset
        ),
        chain AS (
            SELECT p.ord, p.id AS hit_id, m.id AS node_id, m.name AS node_name,
                   m.parent_id, 0 AS up
            FROM page p
            JOIN family_members m ON m.id = p.id AND m.tenant_id = @tenant_id
            UNION ALL
            SELECT c.ord, c.hit_id, m.id, m.name, m.parent_id, c.up + 1
            FROM chain c
            JOIN family_members m ON m.id = c.parent_id AND m.tenant_id = @tenant_id
        )
        SELECT ord, hit_id, node_id, node_name, up
        FROM chain
        ORDER BY ord, up DESC;
        """;

    public static async Task<FamilyMemberSearchResponse> ExecuteAsync(
        ApplicationDbContext context,
        Guid tenantId,
        string query,
        int limit,
        int offset,
        CancellationToken ct)
    {
        var term = query.Trim();

        // An empty tenant id is an unauthenticated caller; an empty term would otherwise
        // become '%%' and match the entire tree. Both fail closed, before any SQL runs.
        if (tenantId == Guid.Empty || term.Length == 0)
            return new FamilyMemberSearchResponse(0, []);

        var pattern = $"%{EscapeLikePattern(term)}%";
        var safeLimit = Math.Clamp(limit, 1, MaxLimit);
        var safeOffset = Math.Max(offset, 0);

        await context.Database.OpenConnectionAsync(ct);
        try
        {
            var total = await CountAsync(context, tenantId, pattern, ct);
            if (total == 0) return new FamilyMemberSearchResponse(0, []);

            var items = await ReadPageAsync(context, tenantId, pattern, safeLimit, safeOffset, ct);
            return new FamilyMemberSearchResponse(total, items);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<int> CountAsync(
        ApplicationDbContext context, Guid tenantId, string pattern, CancellationToken ct)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = CountSql;
        AddParameter(command, "tenant_id", NpgsqlDbType.Uuid, tenantId);
        AddParameter(command, "pattern", NpgsqlDbType.Text, pattern);

        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<IReadOnlyList<FamilyMemberSearchHit>> ReadPageAsync(
        ApplicationDbContext context,
        Guid tenantId,
        string pattern,
        int limit,
        int offset,
        CancellationToken ct)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = PageSql;
        AddParameter(command, "tenant_id", NpgsqlDbType.Uuid, tenantId);
        AddParameter(command, "pattern", NpgsqlDbType.Text, pattern);
        AddParameter(command, "limit", NpgsqlDbType.Integer, limit);
        AddParameter(command, "offset", NpgsqlDbType.Integer, offset);

        await using var reader = await command.ExecuteReaderAsync(ct);

        var hits = new List<FamilyMemberSearchHit>();
        var ancestors = new List<FamilyMemberAncestor>();
        Guid? currentHitId = null;
        var currentName = string.Empty;

        while (await reader.ReadAsync(ct))
        {
            var hitId = reader.GetGuid(1);
            var nodeId = reader.GetGuid(2);
            var nodeName = reader.GetString(3);
            var up = reader.GetInt32(4);

            if (currentHitId is { } previous && previous != hitId)
            {
                hits.Add(Build(previous, currentName, ancestors));
                ancestors = [];
            }

            currentHitId = hitId;

            // Rows arrive root-first (up DESC), so up = 0 is the hit itself and closes the
            // chain; everything before it is an ancestor, already in the right order.
            if (up == 0) currentName = nodeName;
            else ancestors.Add(new FamilyMemberAncestor(nodeId, nodeName));
        }

        if (currentHitId is { } last) hits.Add(Build(last, currentName, ancestors));

        return hits;
    }

    private static FamilyMemberSearchHit Build(
        Guid id, string name, IReadOnlyList<FamilyMemberAncestor> ancestors) =>
        // Generation is derived, not stored: the walk's depth IS the generation, so this
        // cannot drift from FamilyTreeAssembler's independently computed value.
        new(id, name, ancestors.Count + 1, ancestors);

    private static void AddParameter(DbCommand command, string name, NpgsqlDbType type, object value)
    {
        var parameter = new NpgsqlParameter(name, type) { Value = value };
        command.Parameters.Add(parameter);
    }
}
