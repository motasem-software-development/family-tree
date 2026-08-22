using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

/// <summary>
/// Maps every member to its generation, where a parentless member is generation 1 — BR-003:
/// the root family is the family_trees row, not a member.
/// </summary>
public static class GenerationIndex
{
    public static IReadOnlyDictionary<Guid, int> Build(IReadOnlyList<FamilyMember> members)
    {
        var byId = members.ToDictionary(m => m.Id);
        var generations = new Dictionary<Guid, int>(members.Count);

        foreach (var member in members)
            generations[member.Id] = GenerationOf(member, byId, members.Count);

        return generations;
    }

    /// <summary>
    /// Walks upward, bounded by the member count exactly as FamilyTreeAssembler.GenerationOf
    /// bounds it, so a malformed chain terminates instead of looping. Stops on an unresolvable
    /// parent rather than throwing: the composite self-FK makes that unrepresentable in the
    /// database, but this is a pure function over whatever list it is handed (design §6).
    /// </summary>
    private static int GenerationOf(
        FamilyMember member, IReadOnlyDictionary<Guid, FamilyMember> byId, int bound)
    {
        var generation = 1;
        var current = member;

        while (current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
        {
            generation++;
            current = parent;
            if (generation > bound) break;
        }

        return generation;
    }
}
