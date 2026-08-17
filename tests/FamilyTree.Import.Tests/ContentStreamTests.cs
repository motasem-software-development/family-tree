namespace FamilyTree.Import.Tests;

public sealed class ContentStreamTests
{
    [Fact]
    public void Reads_every_glyph() => Assert.Equal(1887, TestPdf.Page().Glyphs.Count);

    [Fact]
    public void Leaves_no_glyph_unresolved()
    {
        // One unmapped glyph silently corrupts a name, so this is an exact zero.
        // (Assert.DoesNotContain instead of Assert.Empty(...Where...): the repo builds
        // with TreatWarningsAsErrors, and xUnit analyzer rule xUnit2029 rejects the latter.)
        Assert.DoesNotContain(TestPdf.Page().Glyphs, g => g.Text.Length == 0);
    }

    [Fact]
    public void Reads_every_path() => Assert.Equal(1218, TestPdf.Page().Paths.Count);

    [Fact]
    public void Reports_the_three_font_sizes()
    {
        var sizes = TestPdf.Page().Glyphs.Select(g => Math.Round(g.Size, 2)).Distinct().Order().ToArray();

        Assert.Equal(new[] { 17.78, 23.71, 35.57 }, sizes);
    }

    [Fact]
    public void Places_the_first_glyph_at_a_hand_computed_position()
    {
        // Independent derivation — traced by hand from the raw token stream (largest inflated
        // content stream), NOT by calling into ContentStream. Verified against the fixture at
        // token indices [0..7210]:
        //
        //   [6]    cm  .23999999 0 0 -.23999999 0 3709.9199   top-level, never popped (base transform)
        //   [7]..[46]  a q/Q/cm pair nested two deep that fully unwinds before the target — no effect
        //   [47]   q                                          opens, still unmatched at the target
        //   [55]   q                                          opens, still unmatched at the target
        //   [62]   cm  3.0877743 ...                          paired with the Q at [7179] — no effect
        //   [7179] Q                                          pops back to the q at [55]
        //   [7180] q                                          opens, still unmatched at the target
        //   [7187] cm  3.125 0 0 3.125 2410.0078 9016.6914     still in effect at the target Tj
        //   [7198] BT                                          resets Tm to identity
        //   [7201] Tf  /F6 35.57
        //   [7208] Tm  1 0 0 -1 -40.988853 35.571159
        //   [7210] Tj  <0003>                                  the target glyph (glyph id 3)
        //
        // Every q/Q pair between token 0 and 7210 balances out, so the CTM in effect at the
        // target Tj is just cm[7187] composed onto cm[6] (the only two cm's still "open").
        // Composing with the spec's affine formula [a b c d e f] x [a' b' c' d' e' f'] =
        // [aa'+bc', ab'+bd', ca'+dc', cb'+dd', ea'+fc'+e', eb'+fd'+f']:
        //
        //   CTM   = [3.125, 0, 0, 3.125, 2410.0078, 9016.6914] x [.23999999, 0, 0, -.23999999, 0, 3709.9199]
        //         = [0.74999996875, 0, 0, -0.74999996875, 578.4018479, 1545.9140542]
        //   full  = Tm x CTM, where Tm = [1, 0, 0, -1, -40.988853, 35.571159] (BT reset it, then this Tm set it)
        //   full.e = -40.988853 * 0.74999996875 + 578.4018479 = 547.6602094
        //   full.f =  35.571159 * -0.74999996875 + 1545.9140542 = 1519.2356860
        //
        // If BT stopped resetting Tm, or Tm x CTM composed in the wrong order, or cm premultiplied
        // the wrong way, this value would move — this is the guard the position-blind count/text/
        // size assertions above cannot provide.
        var first = TestPdf.Page().Glyphs[0];

        Assert.Equal(547.6602, first.X, precision: 3);
        Assert.Equal(1519.2357, first.Y, precision: 3);
    }

    [Fact]
    public void Glyphs_within_a_text_line_advance_along_x()
    {
        // A second, cheaper independent guard: within the first BT/ET text block that carries
        // real glyphs (glyphs[1..5], font F7 at 35.57pt — glyphs[0] is a single placeholder
        // glyph from a separate BT block), each glyph after the first is positioned by a Td
        // that carries a strictly positive x offset in the raw stream (19.243912, 8.3367157,
        // 24.419632, 8.3367157 — all > 0). A broken or non-accumulating Td chain (e.g. Td
        // overwriting Tm instead of premultiplying) would collapse these to the same X, and a
        // sign error would make them decrease.
        var line = TestPdf.Page().Glyphs.Skip(1).Take(5).ToArray();

        for (var i = 1; i < line.Length; i++)
            Assert.True(line[i].X > line[i - 1].X, $"glyph {i} at X={line[i].X} did not advance past glyph {i - 1} at X={line[i - 1].X}");

        // Same text line, so the Y coordinate must be identical across the run.
        Assert.All(line, g => Assert.Equal(line[0].Y, g.Y, precision: 6));
    }
}
