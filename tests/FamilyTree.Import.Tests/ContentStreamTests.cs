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
}
