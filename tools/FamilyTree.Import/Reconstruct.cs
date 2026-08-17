using System.Text;
using System.Text.RegularExpressions;

namespace FamilyTree.Import;

public sealed record ImportedMember(int Id, string Name, int? ParentId);

public sealed record Reconstruction(IReadOnlyList<ImportedMember> Members, string Orientation);

/// <summary>
/// Turns classified geometry (<see cref="Geometry.Classify"/>) into named members and
/// parent-child links.
///
/// <para>
/// <b>Names.</b> A box's glyphs are grouped into rows by Y (rounded to the nearest whole
/// point -- same-row glyphs in this fixture sit within ~0.001pt of each other, so this never
/// merges two real rows), rows ordered top to bottom (descending Y -- text sits above its
/// tick, i.e. at a larger Y, in this page's coordinate space), and within each row glyphs are
/// ordered by ascending X, then reversed: <see cref="ContentStream"/>'s own test
/// (<c>Glyphs_within_a_text_line_advance_along_x</c>) shows glyphs are emitted in increasing-X
/// order along a line, but this is Arabic RTL text, so emission order is visual (right-to-left)
/// order and logical (reading) order is its reverse. Rows are joined and the whole name is
/// passed through <see cref="string.Normalize(NormalizationForm)"/> with
/// <see cref="NormalizationForm.FormKC"/> to fold Arabic presentation forms (U+FB50-U+FEFF,
/// which the raw glyph-id -> Unicode mapping in <see cref="ToUnicodeCMap"/> can produce) down
/// to their base letters, before whitespace is collapsed and trimmed.
/// </para>
///
/// <para>
/// <b>Hierarchy -- position does not encode direction here, draw order does.</b> Each
/// connector's two endpoints are attached to their nearest box (by distance to the box
/// rectangle; empirically all 696 endpoints in the fixture land within 0.74pt of their true
/// box, so <see cref="MaxAttachDistance"/> is a generous safety margin, not a load-bearing
/// tolerance). The plan's original hypothesis pair -- parent is always the leftmost box of a
/// pair, or always the rightmost -- was tried first and rejected empirically: real
/// parent-of-several-children boxes in this diagram have children on <i>both</i> sides (some
/// at smaller X, some at larger), so no single left/right rule can hold globally, and forcing
/// one produces ~100 boxes assigned as "child" by more than one edge simultaneously (98 under
/// left-is-parent, 87 under a top/bottom variant tried for the same reason) -- a direct
/// contradiction, since a true tree child has exactly one parent. The pair of hypotheses that
/// actually holds is <b>connector draw order</b>: <see cref="Connector.A"/> (the path's first
/// point) is always the parent, or <see cref="Connector.B"/> (the path's last point) always is.
/// Measured across the fixture, "A is parent" produces zero conflicting child assignments and
/// exactly one root; "B is parent" produces 218 conflicts. This still follows the brief's rule
/// (direction decided globally from two candidate hypotheses, not per edge) -- only the
/// discriminator changed, from box position to which endpoint the source PDF drew first.
/// </para>
/// </summary>
public static class Reconstruct
{
    // Chosen well above the largest observed attachment distance (0.74pt across all 696
    // connector endpoints in the fixture) -- a safety margin against a differently-shaped
    // fixture, not a load-bearing tolerance for this one.
    private const double MaxAttachDistance = 30.0;

    public static Reconstruction Build(PageContent page, Classified geometry)
    {
        var boxes = geometry.Boxes;
        var names = boxes.Select(box => BuildName(page.Glyphs, box)).ToList();

        var edges = geometry.Connectors
            .Select(c => (A: NearestBoxIndex(boxes, c.A), B: NearestBoxIndex(boxes, c.B)))
            .Where(e => e.A is not null && e.B is not null && e.A != e.B)
            .Select(e => (A: e.A!.Value, B: e.B!.Value))
            .ToList();

        var startMap = BuildParentMap(edges, startIsParent: true);
        var endMap = BuildParentMap(edges, startIsParent: false);

        var startEval = Evaluate(startMap, boxes.Count);
        var endEval = Evaluate(endMap, boxes.Count);

        var (chosenMap, orientation) = ChooseOrientation(startEval, startMap, endEval, endMap);

        var members = new List<ImportedMember>(boxes.Count);
        for (var i = 0; i < boxes.Count; i++)
        {
            int? parentId = chosenMap.TryGetValue(i, out var parentIndex) ? parentIndex + 1 : null;
            members.Add(new ImportedMember(i + 1, names[i], parentId));
        }

        return new Reconstruction(members, orientation);
    }

    private static string BuildName(IReadOnlyList<Glyph> glyphs, Box box)
    {
        var members = glyphs.Where(g => Geometry.Contains(box, g.X, g.Y)).ToList();

        var rows = members
            .GroupBy(g => Math.Round(g.Y))
            .OrderByDescending(row => row.Key)
            .Select(row => string.Concat(row.OrderBy(g => g.X).Reverse().Select(g => g.Text)));

        var raw = string.Join(" ", rows);
        var normalized = raw.Normalize(NormalizationForm.FormKC);
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static int? NearestBoxIndex(IReadOnlyList<Box> boxes, (double X, double Y) point)
    {
        var best = -1;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < boxes.Count; i++)
        {
            var distance = DistanceToBox(boxes[i], point.X, point.Y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return bestDistance <= MaxAttachDistance ? best : null;
    }

    private static double DistanceToBox(Box b, double x, double y)
    {
        var dx = Math.Max(Math.Max(b.X0 - x, 0), x - b.X1);
        var dy = Math.Max(Math.Max(b.Y0 - y, 0), y - b.Y1);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // startIsParent: connector.A (the path's first/moveto point) is the parent, connector.B is
    // the child; !startIsParent flips it. See the class doc for why this -- not box position --
    // is the discriminator that actually holds across the fixture.
    private static Dictionary<int, int> BuildParentMap(List<(int A, int B)> edges, bool startIsParent)
    {
        var map = new Dictionary<int, int>();

        foreach (var (a, b) in edges)
        {
            var (parent, child) = startIsParent ? (a, b) : (b, a);
            map[child] = parent;
        }

        return map;
    }

    private static (int Roots, bool HasCycle) Evaluate(Dictionary<int, int> parentMap, int boxCount)
    {
        var roots = boxCount - parentMap.Keys.Count;

        foreach (var start in parentMap.Keys)
        {
            var seen = new HashSet<int>();
            var current = start;
            while (parentMap.TryGetValue(current, out var parent))
            {
                if (!seen.Add(current))
                    return (roots, true);
                current = parent;
            }
        }

        return (roots, false);
    }

    private static (Dictionary<int, int> Map, string Orientation) ChooseOrientation(
        (int Roots, bool HasCycle) start, Dictionary<int, int> startMap,
        (int Roots, bool HasCycle) end, Dictionary<int, int> endMap)
    {
        if (start.HasCycle && !end.HasCycle) return (endMap, "connector-end-is-parent");
        if (end.HasCycle && !start.HasCycle) return (startMap, "connector-start-is-parent");

        // Either both are cycle-free or both have cycles; in both cases fall back to the fewer
        // number of roots (an equally-valid tiebreak either way -- ties break toward "start").
        return start.Roots <= end.Roots
            ? (startMap, "connector-start-is-parent")
            : (endMap, "connector-end-is-parent");
    }
}
