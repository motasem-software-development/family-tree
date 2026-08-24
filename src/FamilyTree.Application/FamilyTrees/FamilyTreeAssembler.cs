using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.FamilyTrees;

/// <summary>
/// Turns a flat member list into the nested view DTO. Pure and synchronous on purpose: tree
/// shaping and generation arithmetic are the parts most likely to be wrong, and keeping them
/// free of EF makes them testable in milliseconds (design spec §6).
///
/// The tree filters here rather than in SQL (design spec §4.2): the page already loads every
/// member, and a query returning matches *plus* their ancestor chains is materially harder than
/// filtering a list already in hand.
/// </summary>
public static class FamilyTreeAssembler
{
    public static IReadOnlyList<FamilyTreeNodeResponse> Assemble(
        IReadOnlyList<FamilyMember> members, MemberFilter filter, int? maxDepth)
    {
        var rootId = filter.RootId;

        // With nothing filtered out, take the original path: no derivation, no predicate, and
        // every node matches. This is the overwhelmingly common case and it must not change
        // shape or cost.
        var kept = filter.IsEmpty ? null : Select(members, filter);

        var visible = kept is null ? members : members.Where(m => kept.Contains(m.Id)).ToList();

        // One pass to index children by parent; the build below is then linear in the input.
        // Built from the kept set, so a dropped member takes their whole subtree with them and
        // HasMoreChildren counts only children that survived.
        var childrenByParent = visible
            .Where(m => m.ParentId is not null)
            .GroupBy(m => m.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Name, StringComparer.Ordinal).ToList());

        // A depth of zero or less is meaningless; treat it as "no limit" rather than returning
        // an empty tree, which a client would render as "this family has no members".
        var effectiveDepth = maxDepth is > 0 ? maxDepth : null;

        // Matches is the ancestor rule's flag, not the filter's answer: a member kept only to
        // hold up a matching descendant is present with Matches false. Null means unfiltered,
        // where everything matches.
        var matched = kept?.Matched;

        if (rootId is { } id)
        {
            var subtreeRoot = visible.FirstOrDefault(m => m.Id == id);
            if (subtreeRoot is null) return [];

            // The subtree root keeps its real generation, so the caller still knows how deep
            // this fragment sits in the family. Measured against the FULL member list, not the
            // filtered one — the ancestors above the subtree root were never candidates for the
            // filter and dropping them must not renumber what is below.
            var generation = GenerationOf(subtreeRoot, members);
            return [Build(subtreeRoot, generation, 1, effectiveDepth, childrenByParent, matched)];
        }

        return visible
            .Where(m => m.ParentId is null)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => Build(m, 1, 1, effectiveDepth, childrenByParent, matched))
            .ToList();
    }

    /// <summary>
    /// The members the response keeps, and which of them actually matched.
    ///
    /// Design spec §4.2's ancestor rule: a member who fails the filter but has a matching
    /// descendant stays visible, dimmed and non-selectable. Dropping them would detach the
    /// subtree and render the outline as garbage. The Members list and the export have no such
    /// rule — they show only matches.
    /// </summary>
    private sealed class Selection(HashSet<Guid> keep, HashSet<Guid> matched)
    {
        public HashSet<Guid> Matched { get; } = matched;

        public bool Contains(Guid id) => keep.Contains(id);
    }

    private static Selection Select(IReadOnlyList<FamilyMember> members, MemberFilter filter)
    {
        // Root-relative, per design spec §1.2, and measured from the same root the response is
        // rooted at. Note the asymmetry this creates and does not resolve: the generation
        // FILTER reads these numbers, while FamilyTreeNodeResponse.Generation keeps its absolute
        // 1-based value, because the PDF export and the reports page consume that field too.
        // Plan 3 moves the two display sites to root-relative; the field itself stays absolute.
        var placements = MemberDerivation.Derive(members, filter.RootId);
        var byId = members.ToDictionary(m => m.Id);

        var matched = new HashSet<Guid>();
        foreach (var member in members)
        {
            if (placements.TryGetValue(member.Id, out var placement)
                && MemberFilterPredicate.Matches(member, placement, filter))
            {
                matched.Add(member.Id);
            }
        }

        // Every match, plus every ancestor of a match up to the root the walk started from.
        // Bounded by the placement set, so it cannot climb above the selected root and cannot
        // loop: a member already added ends the climb.
        var keep = new HashSet<Guid>();
        foreach (var id in matched)
        {
            var current = id;
            while (keep.Add(current))
            {
                if (byId[current].ParentId is not { } parentId) break;
                if (!placements.ContainsKey(parentId)) break;
                current = parentId;
            }
        }

        return new Selection(keep, matched);
    }

    private static FamilyTreeNodeResponse Build(
        FamilyMember member,
        int generation,
        int level,
        int? maxDepth,
        IReadOnlyDictionary<Guid, List<FamilyMember>> childrenByParent,
        HashSet<Guid>? matched)
    {
        var hasChildren = childrenByParent.TryGetValue(member.Id, out var children);
        var matches = matched is null || matched.Contains(member.Id);

        if (maxDepth is { } limit && level >= limit)
            return new FamilyTreeNodeResponse(
                member.Id, member.Name, member.ParentId, generation, hasChildren, [], matches);

        var built = hasChildren
            ? children!.Select(c => Build(c, generation + 1, level + 1, maxDepth, childrenByParent, matched)).ToList()
            : [];

        return new FamilyTreeNodeResponse(
            member.Id, member.Name, member.ParentId, generation, false, built, matches);
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
