namespace FamilyTree.Application.Export;

public enum Side { Right, Left }

/// <summary>
/// Pass 4 (design §4.3). Greedy heaviest-first packing across two sides. On the seeded tree
/// this reproduces the reference: the dominant branch alone on one side, the rest stacked
/// opposite, with the two masses within a fifth of each other.
/// </summary>
public static class SideAssignment
{
    public static IReadOnlyDictionary<PackedNode, Side> Assign(IReadOnlyList<PackedNode> topLevel)
    {
        var sides = new Dictionary<PackedNode, Side>();
        var loads = new Dictionary<Side, double> { [Side.Right] = 0, [Side.Left] = 0 };

        // Ties break by original index so the assignment is deterministic across runs — a
        // coordinate-level test cannot tolerate an unstable sort.
        var heaviestFirst = topLevel
            .Select((node, index) => (node, index))
            .OrderByDescending(entry => entry.node.Height)
            .ThenBy(entry => entry.index);

        foreach (var (node, _) in heaviestFirst)
        {
            var side = loads[Side.Right] <= loads[Side.Left] ? Side.Right : Side.Left;
            sides[node] = side;
            loads[side] += node.Height;
        }

        return sides;
    }
}
