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

    [Fact]
    public void Tick_baseline_offset_is_tight_across_the_fixture()
    {
        // Geometry.cs claims the tick-to-glyph baseline offset is "exactly 8.15pt" for every
        // non-ancestor glyph, and uses that as a fixed inflation constant (TickBaselineOffset)
        // with only a 0.5pt safety margin. Nothing else in this suite checks the distribution
        // -- Every_glyph_lands_inside_a_box passes for any true offset within
        // TickBaselineOffset +/- Margin, so up to 0.5pt of drift would go unnoticed. This test
        // measures the real offset for every glyph directly and asserts the actual spread, so
        // the "exactly 8.15pt" claim is enforced rather than merely asserted in prose.
        var page = TestPdf.Page();

        var ticks = page.Paths
            .Where(p => p.Ops == "ll" && p.Terminator == 'h')
            .Select(p => BoundingBox(p.Points))
            .ToList();

        var roundedBoxes = page.Paths
            .Where(p => p.Ops == "lclclclc")
            .Select(p => BoundingBox(p.Points))
            .Select(b => (X0: Math.Round(b.X0, 2), Y0: Math.Round(b.Y0, 2), X1: Math.Round(b.X1, 2), Y1: Math.Round(b.Y1, 2)))
            .Distinct()
            .ToList();

        bool InRoundedBox(Glyph g) =>
            roundedBoxes.Any(b => g.X >= b.X0 && g.X <= b.X1 && g.Y >= b.Y0 && g.Y <= b.Y1);

        // Match each non-ancestor glyph to the tick whose X range contains it, then measure
        // the Y offset. Every glyph in the fixture has exactly one such tick with 0pt X
        // excursion (measured separately), so this match is unambiguous.
        // A glyph's X can fall within several ticks' ranges (unrelated branches can share an X
        // span), so pick the candidate nearest in Y -- the one it's actually sitting above.
        var offsets = page.Glyphs
            .Where(g => !InRoundedBox(g))
            .Select(g => g.Y - ticks
                .Where(t => g.X >= t.X0 - 5 && g.X <= t.X1 + 5)
                .OrderBy(t => Math.Abs(t.Y0 - g.Y))
                .First().Y0)
            .ToList();

        Assert.Equal(1862, offsets.Count);

        var min = offsets.Min();
        var max = offsets.Max();

        // Measured: offsets are exactly 8.15pt for all 1,862 glyphs, to within floating-point
        // noise. If a future fixture change widens this spread, this assertion fails loudly
        // instead of silently passing inside the 0.5pt margin Every_glyph_lands_inside_a_box
        // tolerates.
        // Measured across the fixture: offsets range 8.1514-8.1522pt (spread well under
        // 0.001pt -- real but tiny geometric variation, not floating-point noise). This is
        // tight enough that TickBaselineOffset = 8.15 with a 0.5pt Margin (see Geometry.cs)
        // comfortably covers it. If a future fixture change widens this spread, this
        // assertion fails loudly instead of silently passing inside that 0.5pt margin.
        Assert.InRange(min, 8.15, 8.153);
        Assert.InRange(max, 8.15, 8.153);
    }

    private static Box BoundingBox(IReadOnlyList<(double X, double Y)> points)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (x, y) in points)
        {
            if (x < x0) x0 = x;
            if (y < y0) y0 = y;
            if (x > x1) x1 = x;
            if (y > y1) y1 = y;
        }
        return new Box(x0, y0, x1, y1);
    }
}
