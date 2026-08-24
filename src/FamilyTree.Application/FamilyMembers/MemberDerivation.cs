using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.FamilyMembers;

/// <summary>
/// Where a member sits relative to the selected root: which branch they belong to, and how deep.
///
/// <paramref name="BranchId"/> is null for the root itself, which renders as "Root"
/// (specification §21) — the absence of a branch, not a branch that can be selected.
/// <paramref name="Generation"/> is root-relative, 0 at the root (design spec §1.2). Both are
/// derived on every read and never stored, so a moved subtree renumbers itself with no backfill
/// (design spec §2.5).
/// </summary>
public readonly record struct MemberPlacement(Guid? BranchId, int Generation);

/// <summary>
/// The in-memory twin of the recursive CTE in <c>FamilyMemberQuery</c>. Both implement design
/// spec §3's single downward walk; this one exists because the tree page already loads every
/// member and shapes it in process (design spec §4.2), and a second query returning matches
/// *plus* their ancestor chains is materially harder than filtering a list already in hand.
///
/// The duplication is deliberate but not unwatched: an integration test walks the seeded family
/// through both and asserts they agree, so a change to one that is not made to the other fails
/// the suite rather than shipping.
///
/// Pure and synchronous, for the same reason <c>FamilyTreeAssembler</c> is: branch and generation
/// arithmetic is the part most likely to be wrong, and keeping it free of EF makes it testable in
/// milliseconds.
/// </summary>
public static class MemberDerivation
{
    /// <summary>
    /// Places every member reachable from the root. A member outside the selected subtree is
    /// absent from the result rather than present with a null placement — absence is what the
    /// tree filter prunes on, and a sentinel would have to be checked for at every use.
    /// </summary>
    /// <param name="rootId">
    /// The root to measure from. Null means every parentless member is a root, which for this
    /// data is the single member داوود (design spec §1.3).
    /// </param>
    public static IReadOnlyDictionary<Guid, MemberPlacement> Derive(
        IReadOnlyList<FamilyMember> members, Guid? rootId)
    {
        var placements = new Dictionary<Guid, MemberPlacement>();
        if (members.Count == 0) return placements;

        // One pass to index children by parent; the walk below is then linear in the input.
        var childrenByParent = members
            .Where(m => m.ParentId is not null)
            .GroupBy(m => m.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // The anchor term of the CTE: the named root, or every parentless member.
        var frontier = rootId is { } id
            ? members.Where(m => m.Id == id).ToList()
            : members.Where(m => m.ParentId is null).ToList();

        foreach (var anchor in frontier) placements[anchor.Id] = new MemberPlacement(null, 0);

        var generation = 0;

        // Iterative breadth-first rather than recursive: a deep chain must not depend on stack
        // depth, and each level maps exactly onto one iteration of the CTE's recursive term.
        while (frontier.Count > 0)
        {
            generation++;
            var next = new List<FamilyMember>();

            foreach (var parent in frontier)
            {
                if (!childrenByParent.TryGetValue(parent.Id, out var children)) continue;

                var parentBranch = placements[parent.Id].BranchId;

                foreach (var child in children)
                {
                    // Already placed means the parent chain loops back on itself. Skipping is
                    // what makes a corrupt import an answer rather than a hang; cycles cannot
                    // be created through the move command, which validates with a recursive CTE.
                    if (placements.ContainsKey(child.Id)) continue;

                    // COALESCE(t.branch_id, c.id) is the entire branch rule (design spec §3): a
                    // direct child of the root has no parent branch, so it becomes its own
                    // branch, and every descendant inherits it unchanged at any depth.
                    placements[child.Id] = new MemberPlacement(parentBranch ?? child.Id, generation);
                    next.Add(child);
                }
            }

            frontier = next;
        }

        return placements;
    }
}
