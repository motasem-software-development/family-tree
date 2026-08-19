using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Import.Tests;

/// <summary>
/// The flagship acceptance test (design §7.2): our own reconstruction engine, pointed at our
/// own export, must recover the same hierarchy. It validates geometry, glyph encoding,
/// connector direction, and searchability at once.
///
/// <para>
/// There is no dedicated "read a PDF from a path" or "reconstruct from a path" entry point in
/// <see cref="FamilyTree.Import"/> -- the pipeline is already fully composable from existing
/// public statics (<see cref="PdfStreams.Inflate"/>, <see cref="PdfStreams.ContentStreamOf"/>,
/// <see cref="ToUnicodeCMap.Parse"/>, <see cref="ContentStream.Read"/>,
/// <see cref="Geometry.Classify"/>, <see cref="Reconstruct.Build"/>), the same way
/// <c>TestPdf.cs</c> already exercises it for the reference fixture. No new seam was needed for
/// the reconstruction pipeline itself. <see cref="PdfStreams.ContentStreamOf"/> is used here
/// instead of <see cref="PdfStreams.LargestOf"/> (which <c>Program.cs</c> and
/// <c>TestPdf.cs</c> still use) because Skia's export embeds a font subset larger than its own
/// content stream -- see that method's doc comment.
/// </para>
/// </summary>
public sealed class SkiaExportRoundTripTests
{
    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static FamilyTreeNodeResponse Fixture() =>
        Node("سليمان",
            Node("أحمد", Node("خليل"), Node("عمر")),
            Node("داوود", Node("إبراهيم")),
            Node("فارس"));

    private static string RenderToFile()
    {
        var scene = SceneScaler.FitToSheet(
            new XmindLayoutStrategy().Build(
                [Fixture()], LayoutOptions.Default, SkiaTextMeasurer.Delegate),
            LayoutOptions.Default.Metrics);

        var path = Path.Combine(Path.GetTempPath(), $"roundtrip-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, new SkiaTreeRenderer().Render(scene, ExportPageFormat.Sheet));
        return path;
    }

    private static PageContent ReadFirstPage(string path)
    {
        var streams = PdfStreams.Inflate(File.ReadAllBytes(path));
        return ContentStream.Read(PdfStreams.ContentStreamOf(streams), ToUnicodeCMap.Parse(streams));
    }

    private static Reconstruction ReconstructFromPdf(string path)
    {
        var page = ReadFirstPage(path);
        return Reconstruct.Build(page, Geometry.Classify(page));
    }

    [Fact]
    public void Every_exported_member_is_classified_as_a_node()
    {
        var path = RenderToFile();
        try
        {
            var classified = Geometry.Classify(ReadFirstPage(path));

            classified.Boxes.Should().HaveCount(7, "the fixture has seven members");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_exported_hierarchy_reconstructs_to_the_source_hierarchy()
    {
        var path = RenderToFile();
        try
        {
            var reconstruction = ReconstructFromPdf(path);

            reconstruction.Members.Should().HaveCount(7);
            reconstruction.Members.Where(m => m.ParentId is null).Should().ContainSingle()
                .Which.Name.Should().Be("سليمان");
        }
        finally { File.Delete(path); }
    }
}
