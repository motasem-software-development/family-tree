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

    [Fact]
    public void Handles_multi_codeunit_bfchar_destination_ligature()
    {
        // Create a synthetic CMap with a ligature: glyph 0x1000 -> "fl" (U+0066 U+006C)
        var cmapText = """
            /CIDInit /ProcSet findresource begin
            12 dict begin
            begincmap
            /CMapType 2 def
            /CMapName /Test def
            1 begincodespacerange
            <0000> <FFFF>
            endcodespacerange
            1 beginbfchar
            <1000> <0066006C>
            endbfchar
            endcmap
            end
            end
            """;

        var cmapBytes = System.Text.Encoding.Latin1.GetBytes(cmapText);
        var parsed = ToUnicodeCMap.Parse(new[] { cmapBytes });

        Assert.Equal("fl", parsed.Lookup(0x1000));
    }

    [Fact]
    public void Rejects_bfrange_array_form_with_loud_error()
    {
        // Create a synthetic CMap with bfrange array form: glyph range 0x1000-0x1001 with [<0041> <0042>]
        // This is valid PDF but not supported by our implementation — must fail loudly, not silently mis-parse
        var cmapText = """
            /CIDInit /ProcSet findresource begin
            12 dict begin
            begincmap
            /CMapType 2 def
            /CMapName /Test def
            1 begincodespacerange
            <0000> <FFFF>
            endcodespacerange
            1 beginbfrange
            <1000> <1001> [<0041> <0042>]
            endbfrange
            endcmap
            end
            end
            """;

        var cmapBytes = System.Text.Encoding.Latin1.GetBytes(cmapText);

        var ex = Assert.Throws<NotSupportedException>(() =>
            ToUnicodeCMap.Parse(new[] { cmapBytes }));

        Assert.Contains("array form", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_bfrange_multi_codeunit_destination_in_offset_form()
    {
        // Create a synthetic CMap with invalid bfrange: multi-codepoint destination in offset form
        // (offset only works with single UTF-16 code unit destinations)
        var cmapText = """
            /CIDInit /ProcSet findresource begin
            12 dict begin
            begincmap
            /CMapType 2 def
            /CMapName /Test def
            1 begincodespacerange
            <0000> <FFFF>
            endcodespacerange
            1 beginbfrange
            <1000> <1001> <0066006C>
            endbfrange
            endcmap
            end
            end
            """;

        var cmapBytes = System.Text.Encoding.Latin1.GetBytes(cmapText);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ToUnicodeCMap.Parse(new[] { cmapBytes }));

        Assert.Contains("single UTF-16 code unit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
