namespace FamilyTree.Import.Tests;

public sealed class ToUnicodeCMapTests
{
    private static ToUnicodeCMap Map() =>
        ToUnicodeCMap.Parse(PdfStreams.Inflate(File.ReadAllBytes(TestPaths.FamilyTreePdf)));

    [Theory]
    [InlineData(0x03A3, "ﺣ")] // inside a bfrange — the letter ح
    [InlineData(0x03CB, "ﻋ")] // inside a bfrange — the letter ع
    [InlineData(0x03DF, "ﻟ")] // inside a bfrange — the letter ل
    [InlineData(0x038D, "ا")] // a plain bfchar entry — alef
    public void Resolves_both_bfchar_and_bfrange_entries(int glyphId, string expected)
    {
        Assert.Equal(expected, Map().Lookup(glyphId));
    }

    [Fact]
    public void Range_endpoints_both_resolve()
    {
        // <03A2> <03A3> <FEA2>: lo and hi must both map, offset by their distance from lo.
        Assert.Equal("ﺢ", Map().Lookup(0x03A2));
        Assert.Equal("ﺣ", Map().Lookup(0x03A3));
    }
}
