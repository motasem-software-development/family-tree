using System.Text;
using System.Text.Json;

namespace FamilyTree.Import;

/// <summary>
/// Renders a <see cref="Reconstruction"/> as the two Task 6 artifacts: an indented plain-text
/// tree for human review (§7.2's gate) and a flat JSON array for Task 7 to seed from.
/// </summary>
public static class Emit
{
    /// <summary>
    /// One name per line, two spaces of indent per generation, siblings ordered by
    /// <see cref="ImportedMember.Y"/> descending (PDF page space: larger Y is higher on the
    /// page, so descending Y reads top-to-bottom the way the source document does). Traversal
    /// is pre-order (a parent's line always precedes its children's), and root order among
    /// multiple roots (should there ever be more than the expected one) is also by Y.
    /// </summary>
    public static string IndentedTree(Reconstruction r)
    {
        var childrenOf = ChildrenByParent(r.Members);
        var roots = r.Members.Where(m => m.ParentId is null).OrderByDescending(m => m.Y).ThenBy(m => m.Id);

        var sb = new StringBuilder();
        foreach (var root in roots)
            AppendSubtree(sb, root, childrenOf, depth: 0);

        return sb.ToString();
    }

    private static void AppendSubtree(
        StringBuilder sb,
        ImportedMember member,
        IReadOnlyDictionary<int, List<ImportedMember>> childrenOf,
        int depth)
    {
        sb.Append(' ', depth * 2).Append(member.Name).Append('\n');

        if (!childrenOf.TryGetValue(member.Id, out var children))
            return;

        foreach (var child in children)
            AppendSubtree(sb, child, childrenOf, depth + 1);
    }

    private static Dictionary<int, List<ImportedMember>> ChildrenByParent(IReadOnlyList<ImportedMember> members)
    {
        var byParent = new Dictionary<int, List<ImportedMember>>();

        foreach (var m in members)
        {
            if (m.ParentId is not { } parentId)
                continue;

            if (!byParent.TryGetValue(parentId, out var list))
                byParent[parentId] = list = new List<ImportedMember>();

            list.Add(m);
        }

        foreach (var list in byParent.Values)
            list.Sort((a, b) => b.Y.CompareTo(a.Y) is var cmp && cmp != 0 ? cmp : a.Id.CompareTo(b.Id));

        return byParent;
    }

    /// <summary>
    /// Flat JSON array (not nested by <c>ParentId</c>) -- Task 7 seeds a relational table, so a
    /// flat member list with parent references is the shape it needs, not a nested tree.
    /// Members are emitted in the same pre-order (siblings by Y) as <see cref="IndentedTree"/>
    /// so document order survives into seeding.
    /// </summary>
    public static string Json(Reconstruction r)
    {
        var childrenOf = ChildrenByParent(r.Members);
        var roots = r.Members.Where(m => m.ParentId is null).OrderByDescending(m => m.Y).ThenBy(m => m.Id);

        var ordered = new List<ImportedMember>(r.Members.Count);
        foreach (var root in roots)
            CollectPreOrder(root, childrenOf, ordered);

        var payload = new
        {
            orientation = r.Orientation,
            members = ordered.Select(m => new { id = m.Id, name = m.Name, parentId = m.ParentId }),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static void CollectPreOrder(
        ImportedMember member,
        IReadOnlyDictionary<int, List<ImportedMember>> childrenOf,
        List<ImportedMember> ordered)
    {
        ordered.Add(member);

        if (!childrenOf.TryGetValue(member.Id, out var children))
            return;

        foreach (var child in children)
            CollectPreOrder(child, childrenOf, ordered);
    }
}
