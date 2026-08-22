using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

public static class StructureCalculator
{
    public static StructureReport Calculate(
        IReadOnlyList<FamilyMember> members, IReadOnlyDictionary<Guid, int> generations)
    {
        var childrenByParent = members
            .Where(m => m.ParentId is not null)
            .GroupBy(m => m.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var generationCounts = generations.Values
            .GroupBy(g => g)
            .OrderBy(g => g.Key)
            .Select(g => new GenerationCount(g.Key, g.Count()))
            .ToList();

        var branches = members
            .Where(m => m.ParentId is null)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(root => BranchOf(root, childrenByParent))
            .ToList();

        // Counted over members rather than over the dictionary's keys: a key naming a member
        // outside the list would otherwise inflate the parent count and break the partition.
        var membersWithChildren = members.Count(m => childrenByParent.ContainsKey(m.Id));
        var childCount = members.Count(m => m.ParentId is not null);

        return new StructureReport(
            TotalMembers: members.Count,
            Depth: generations.Values.DefaultIfEmpty(0).Max(),
            Generations: generationCounts,
            Branches: branches,
            MembersWithChildren: membersWithChildren,
            LeafMembers: members.Count - membersWithChildren,
            AverageChildrenPerParent: membersWithChildren == 0
                ? 0m
                : Math.Round((decimal)childCount / membersWithChildren, 2));
    }

    /// <summary>
    /// Iterative depth-first walk rather than recursion: a deep imported lineage should not be
    /// able to overflow the stack in a report.
    /// </summary>
    private static BranchSummary BranchOf(
        FamilyMember root, IReadOnlyDictionary<Guid, List<FamilyMember>> childrenByParent)
    {
        var descendants = 0;
        var depth = 1;
        var stack = new Stack<(FamilyMember Member, int Level)>();
        stack.Push((root, 1));

        while (stack.Count > 0)
        {
            var (member, level) = stack.Pop();
            depth = Math.Max(depth, level);

            if (!childrenByParent.TryGetValue(member.Id, out var children)) continue;

            foreach (var child in children)
            {
                descendants++;
                stack.Push((child, level + 1));
            }
        }

        return new BranchSummary(root.Id, root.Name, descendants, depth);
    }
}
