namespace FamilyTree.Import.Tests;

public sealed class PdfStreamsTests
{
    private static byte[] Pdf() => File.ReadAllBytes(TestPaths.FamilyTreePdf);

    [Fact]
    public void Inflate_returns_every_flate_stream()
    {
        Assert.Equal(5, PdfStreams.Inflate(Pdf()).Count);
    }

    [Fact]
    public void LargestOf_returns_the_content_stream()
    {
        Assert.Equal(244_206, PdfStreams.LargestOf(PdfStreams.Inflate(Pdf())).Length);
    }
}
