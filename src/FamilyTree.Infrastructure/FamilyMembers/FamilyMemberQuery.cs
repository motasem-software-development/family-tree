using System.Data.Common;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace FamilyTree.Infrastructure.FamilyMembers;

/// <summary>
/// The second raw-SQL surface in the codebase, and the one design spec §9 singles out as
/// carrying the tenant-isolation risk. It follows <see cref="FamilyMemberSearchQuery"/>
/// deliberately closely, so the two can be reviewed against each other.
///
/// Raw SQL rather than LINQ because EF Core cannot express WITH RECURSIVE, and the downward walk
/// IS the feature: it produces branch and generation together in one pass (design spec §3). The
/// cost of that choice is that the EF global query filter — layer 1 of the three-layer tenant
/// isolation in the original design spec §3.2 — does not apply. Every <c>family_members</c>
/// reference below therefore carries an explicit tenant_id predicate, <b>including inside the
/// recursive term</b>: without it, a walk that starts on a permitted row could descend into
/// another tenant's members. That case has its own integration test, because it is the one
/// omission every other test in the file would still pass with.
///
/// <c>countries</c> is the single join with no tenant predicate, and that is correct: it is
/// system-level reference data with no global query filter of its own (design spec §2.1).
///
/// One copy of the walk. The three public methods concatenate their own tail onto
/// <see cref="TreeCte"/> rather than restating it, so there is exactly one place where the
/// tenant predicates can be got wrong.
/// </summary>
internal static class FamilyMemberQuery
{
    /// <summary>
    /// Design spec §3, verbatim.
    ///
    /// <c>COALESCE(t.branch_id, c.id)</c> is the entire branch rule: a direct child of the root
    /// has a null parent branch, so it becomes its own branch, and every descendant inherits it
    /// unchanged at any depth. The root keeps <c>branch_id IS NULL</c>, which renders as "Root"
    /// (specification §21). Generation falls out of the same walk, 0 at the root.
    ///
    /// The CASE in the anchor is what lets one query serve both "the whole tree" and "this
    /// subtree" — a null root_id anchors on every parentless member.
    /// </summary>
    private const string TreeCte = """
        WITH RECURSIVE tree AS (
            SELECT m.id, NULL::uuid AS branch_id, 0 AS generation
            FROM family_members m
            WHERE m.tenant_id = @tenant_id
              AND (CASE WHEN @root_id IS NULL THEN m.parent_id IS NULL ELSE m.id = @root_id END)
          UNION ALL
            SELECT c.id, COALESCE(t.branch_id, c.id), t.generation + 1
            FROM tree t
            JOIN family_members c ON c.parent_id = t.id AND c.tenant_id = @tenant_id
        )
        """;

    /// <summary>
    /// The `b` self-join resolves the branch's name in the same pass. LEFT, because the root's
    /// branch_id is null and the root must still come back — an inner join would silently drop
    /// the one member specification §21 renders as "Root".
    ///
    /// Every predicate is "@parameter IS NULL OR ...", so an absent filter is a no-op and
    /// specification §15's combinability is a plain AND across whatever was supplied.
    /// </summary>
    private const string ListTail = """

        SELECT m.id, m.name, m.parent_id, m.version, m.created_at, m.updated_at,
               m.date_of_birth, m.date_of_death, m.is_deceased,
               m.national_id, m.mobile_number, m.whats_app_number, m.country_id,
               co.code AS country_code,
               t.branch_id, b.name AS branch_name, t.generation
        FROM tree t
        JOIN family_members m ON m.id = t.id AND m.tenant_id = @tenant_id
        LEFT JOIN countries co ON co.id = m.country_id
        LEFT JOIN family_members b ON b.id = t.branch_id AND b.tenant_id = @tenant_id
        WHERE (@search      IS NULL OR m.name ILIKE @search ESCAPE '\')
          AND (@is_deceased IS NULL OR m.is_deceased = @is_deceased)
          AND (@branch_id   IS NULL OR t.branch_id   = @branch_id)
          AND (@generation  IS NULL OR t.generation  = @generation)
          AND (@country_id  IS NULL OR m.country_id  = @country_id)
        ORDER BY m.name, m.id
        LIMIT @limit OFFSET @offset;
        """;

    /// <summary>
    /// The branches are exactly the root's direct children (design spec §1.3), which is the set
    /// of values branch_id can take. Deliberately unfiltered by anything but the root: this
    /// answers "what is available to filter by", and narrowing it by the current filter would
    /// build a dropdown that erases its own options as soon as one is used.
    /// </summary>
    private const string BranchTail = """

        SELECT b.id, b.name
        FROM tree t
        JOIN family_members b ON b.id = t.id AND b.tenant_id = @tenant_id
        WHERE t.generation = 1
        ORDER BY b.name, b.id;
        """;

    private const string GenerationTail = """

        SELECT DISTINCT generation FROM tree ORDER BY generation;
        """;

    /// <summary>
    /// The members list is unpaginated today (design spec §5.3). The parameters exist so that
    /// adding pagination later changes this file rather than the contract.
    /// </summary>
    public const int NoLimit = int.MaxValue;

    /// <summary>
    /// LIKE metacharacters survive parameterisation — a parameter binds the pattern, not its
    /// meaning — so a user typing "%" would otherwise match every member. Backslash first, or it
    /// would escape the escapes added after it.
    /// </summary>
    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    public static async Task<IReadOnlyList<FamilyMemberListItem>> ListAsync(
        ApplicationDbContext context,
        Guid tenantId,
        MemberFilter filter,
        int limit,
        int offset,
        CancellationToken ct)
    {
        // An empty tenant id is an unauthenticated caller. Fail closed, before any SQL runs, as
        // FamilyMemberSearchQuery does.
        if (tenantId == Guid.Empty) return [];

        return await ExecuteAsync(context, TreeCte + ListTail, command =>
        {
            AddFilterParameters(command, tenantId, filter);
            AddParameter(command, "limit", NpgsqlDbType.Integer, Math.Max(limit, 0));
            AddParameter(command, "offset", NpgsqlDbType.Integer, Math.Max(offset, 0));
        }, ReadItem, ct);
    }

    public static async Task<IReadOnlyList<BranchResponse>> ListBranchesAsync(
        ApplicationDbContext context, Guid tenantId, Guid? rootId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) return [];

        return await ExecuteAsync(context, TreeCte + BranchTail, command =>
        {
            AddParameter(command, "tenant_id", NpgsqlDbType.Uuid, tenantId);
            AddParameter(command, "root_id", NpgsqlDbType.Uuid, rootId);
        }, reader => new BranchResponse(reader.GetGuid(0), reader.GetString(1)), ct);
    }

    public static async Task<IReadOnlyList<int>> ListGenerationsAsync(
        ApplicationDbContext context, Guid tenantId, Guid? rootId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) return [];

        return await ExecuteAsync(context, TreeCte + GenerationTail, command =>
        {
            AddParameter(command, "tenant_id", NpgsqlDbType.Uuid, tenantId);
            AddParameter(command, "root_id", NpgsqlDbType.Uuid, rootId);
        }, reader => reader.GetInt32(0), ct);
    }

    private static async Task<IReadOnlyList<T>> ExecuteAsync<T>(
        ApplicationDbContext context,
        string sql,
        Action<DbCommand> bind,
        Func<DbDataReader, T> read,
        CancellationToken ct)
    {
        await context.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            bind(command);

            await using var reader = await command.ExecuteReaderAsync(ct);

            var rows = new List<T>();
            while (await reader.ReadAsync(ct)) rows.Add(read(reader));
            return rows;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static void AddFilterParameters(DbCommand command, Guid tenantId, MemberFilter filter)
    {
        AddParameter(command, "tenant_id", NpgsqlDbType.Uuid, tenantId);
        AddParameter(command, "root_id", NpgsqlDbType.Uuid, filter.RootId);
        AddParameter(command, "search", NpgsqlDbType.Text,
            filter.Search is null ? null : $"%{EscapeLikePattern(filter.Search)}%");

        // The three-valued status becomes a nullable bool at the parameter, so the SQL never has
        // to know the enum exists: All binds NULL, which makes the predicate a no-op.
        AddParameter(command, "is_deceased", NpgsqlDbType.Boolean, filter.Status switch
        {
            MemberStatusFilter.Alive => false,
            MemberStatusFilter.Deceased => true,
            _ => (bool?)null
        });

        AddParameter(command, "branch_id", NpgsqlDbType.Uuid, filter.BranchId);
        AddParameter(command, "generation", NpgsqlDbType.Integer, filter.Generation);
        AddParameter(command, "country_id", NpgsqlDbType.Integer, filter.CountryId);
    }

    private static FamilyMemberListItem ReadItem(DbDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetGuid(2),
        reader.GetInt32(3),
        reader.GetFieldValue<DateTimeOffset>(4),
        reader.GetFieldValue<DateTimeOffset>(5),
        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
        reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
        reader.GetBoolean(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetInt32(12),
        reader.IsDBNull(13) ? null : reader.GetString(13),
        reader.IsDBNull(14) ? null : reader.GetGuid(14),
        reader.IsDBNull(15) ? null : reader.GetString(15),
        reader.GetInt32(16));

    /// <summary>
    /// A null value binds DBNull, which is what makes every "@parameter IS NULL OR ..." predicate
    /// a no-op for an absent filter. Passing null through as a CLR null would bind nothing and
    /// leave the parameter unset.
    /// </summary>
    private static void AddParameter(DbCommand command, string name, NpgsqlDbType type, object? value)
    {
        var parameter = new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value };
        command.Parameters.Add(parameter);
    }
}
