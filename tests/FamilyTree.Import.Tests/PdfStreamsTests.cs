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

    [Fact]
    public void ContentStreamOf_agrees_with_LargestOf_on_the_reference_fixture()
    {
        // The reference fixture has no embedded font stream large enough to fool LargestOf,
        // so the two selectors must agree here -- this is what pins ContentStreamOf (added for
        // Skia's export, which does embed a larger font stream) to not disturb the reference.
        var streams = PdfStreams.Inflate(Pdf());
        Assert.Same(PdfStreams.LargestOf(streams), PdfStreams.ContentStreamOf(streams));
    }
}
