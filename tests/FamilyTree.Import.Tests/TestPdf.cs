namespace FamilyTree.Import.Tests;

public static class TestPdf
{
    private static readonly Lazy<PageContent> _page = new(() =>
    {
        var streams = PdfStreams.Inflate(File.ReadAllBytes(TestPaths.FamilyTreePdf));
        return ContentStream.Read(PdfStreams.LargestOf(streams), ToUnicodeCMap.Parse(streams));
    });

    public static PageContent Page() => _page.Value;

    private static readonly Lazy<Reconstruction> _reconstruction = new(() =>
        Reconstruct.Build(Page(), Geometry.Classify(Page())));

    public static Reconstruction Reconstruction() => _reconstruction.Value;
}
