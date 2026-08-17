namespace FamilyTree.Import.Tests;

public static class TestPdf
{
    private static readonly Lazy<PageContent> _page = new(() =>
    {
        var streams = PdfStreams.Inflate(File.ReadAllBytes(TestPaths.FamilyTreePdf));
        return ContentStream.Read(PdfStreams.LargestOf(streams), ToUnicodeCMap.Parse(streams));
    });

    public static PageContent Page() => _page.Value;
}
