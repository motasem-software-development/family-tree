using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.FamilyTrees;

/// <summary>
/// Turns a flat member list into the nested view DTO. Pure and synchronous on purpose: tree
/// shaping and generation arithmetic are the parts most likely to be wrong, and keeping them
/// free of EF makes them testable in milliseconds (design spec §6).
/// </summary>
public static class FamilyTreeAssembler
{
    public static IReadOnlyList<FamilyTreeNodeResponse> Assemble(
        IReadOnlyList<FamilyMember> members, Guid? rootId, int? maxDepth)
    {
        // One pass to index children by parent; the build below is then linear in the input.
        var childrenByParent = members
            .Where(m => m.ParentId is not null)
            .GroupBy(m => m.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Name, StringComparer.Ordinal).ToList());

        // A depth of zero or less is meaningless; treat it as "no limit" rather than returning
        // an empty tree, which a client would render as "this family has no members".
        var effectiveDepth = maxDepth is > 0 ? maxDepth : null;

        if (rootId is { } id)
        {
            var subtreeRoot = members.FirstOrDefault(m => m.Id == id);
            if (subtreeRoot is null) return [];

            // The subtree root keeps its real generation, so the caller still knows how deep
            // this fragment sits in the family.
            var generation = GenerationOf(subtreeRoot, members);
            return [Build(subtreeRoot, generation, 1, effectiveDepth, childrenByParent)];
        }

        return members
            .Where(m => m.ParentId is null)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => Build(m, 1, 1, effectiveDepth, childrenByParent))
            .ToList();
    }

    private static FamilyTreeNodeResponse Build(
        FamilyMember member,
        int generation,
        int level,
        int? maxDepth,
        IReadOnlyDictionary<Guid, List<FamilyMember>> childrenByParent)
    {
        var hasChildren = childrenByParent.TryGetValue(member.Id, out var children);

        if (maxDepth is { } limit && level >= limit)
            return new FamilyTreeNodeResponse(
                member.Id, member.Name, member.ParentId, generation, hasChildren, []);

        var built = hasChildren
            ? children!.Select(c => Build(c, generation + 1, level + 1, maxDepth, childrenByParent)).ToList()
            : [];

        return new FamilyTreeNodeResponse(
            member.Id, member.Name, member.ParentId, generation, false, built);
    }

    /// <summary>
    /// Walks upward to find how deep a member sits. Bounded by the input size so a malformed
    /// parent chain cannot loop forever — cycles are impossible by construction until the
    /// Phase 5 move command exists, and that command validates them with a recursive CTE.
    /// </summary>
    private static int GenerationOf(FamilyMember member, IReadOnlyList<FamilyMember> members)
    {
        var byId = members.ToDictionary(m => m.Id);
        var generation = 1;
        var current = member;

        while (current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
        {
            generation++;
            current = parent;
            if (generation > members.Count) break;
        }

        return generation;
    }
}
