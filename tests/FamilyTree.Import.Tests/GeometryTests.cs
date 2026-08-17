namespace FamilyTree.Import.Tests;

public sealed class GeometryTests
{
    private static Classified Classify() => Geometry.Classify(TestPdf.Page());

    [Fact]
    public void Finds_one_box_per_name()
    {
        // 349 text runs were measured in the source PDF: 344 name ticks + 5 deduped rounded
        // ancestor boxes. Tightened from the plan's 345-355 range now that the exact
        // composition is known (see Geometry.cs for the derivation).
        Assert.Equal(349, Classify().Boxes.Count);
    }

    [Fact]
    public void Every_glyph_lands_inside_a_box()
    {
        var boxes = Classify().Boxes;
        var orphans = TestPdf.Page().Glyphs
            .Where(g => !boxes.Any(b => Geometry.Contains(b, g.X, g.Y)))
            .ToArray();

        Assert.Empty(orphans);
    }

    [Fact]
    public void Boxes_do_not_overlap()
    {
        // Overlapping boxes make glyph assignment ambiguous and names interleave.
        var boxes = Classify().Boxes;
        var overlaps =
            from i in Enumerable.Range(0, boxes.Count)
            from j in Enumerable.Range(i + 1, boxes.Count - i - 1)
            where Geometry.Overlaps(boxes[i], boxes[j])
            select (i, j);

        Assert.Empty(overlaps);
    }

    [Fact]
    public void Finds_a_connector_for_all_but_the_root()
    {
        // A tree of 349 nodes has exactly 348 edges: 338 elbow connectors + 6 short stubs +
        // 4 brackets (see Geometry.cs). Tightened from the plan's wide margin now that the
        // exact composition is known.
        Assert.Equal(348, Classify().Connectors.Count);
    }
}
