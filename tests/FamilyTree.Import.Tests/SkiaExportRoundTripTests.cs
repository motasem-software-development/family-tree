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

    // Every letter here is one already confirmed to decode correctly through this exact
    // Skia -> ToUnicodeCMap.Parse pipeline elsewhere in this fixture (س ل ي م ا ن ح د ع ر ف و).
    // Two letters found during Task 12 review to decode incorrectly through this project's own
    // ToUnicodeCMap.Parse (a separate, pre-existing bug -- see Reconstruct.FoldFontArtifacts'
    // doc comment on the Noon Ghunna fold for the mechanism) are deliberately avoided here: خ
    // (KHAH), which this pipeline's bfrange decoding turned into plain ح (HAH) -- a codepoint
    // that is *also* a legitimate letter elsewhere in this very fixture (أحمد), so no unambiguous
    // string-level fold could compensate the way the Noon fold does; and إ (ALEF WITH HAMZA
    // BELOW), which decoded as bare ا. This test's job is discriminating connector direction and
    // hierarchy reconstruction (see the class doc), not re-litigating ToUnicodeCMap.Parse's
    // decoding fidelity, which is explicitly out of this task's scope.
    private static FamilyTreeNodeResponse Fixture() =>
        Node("سليمان",
            Node("أحمد", Node("سالم"), Node("عمر")),
            Node("داوود", Node("سعيد")),
            Node("فارس"));

    /// <summary>
    /// Every member's name mapped to its parent's name (null for the root), walked directly off
    /// <see cref="Fixture"/> so the expectation can never drift from the tree actually rendered.
    /// </summary>
    private static Dictionary<string, string?> ExpectedParentByName()
    {
        var result = new Dictionary<string, string?>();

        void Walk(FamilyTreeNodeResponse node, string? parentName)
        {
            result[node.Name] = parentName;
            foreach (var child in node.Children) Walk(child, node.Name);
        }

        Walk(Fixture(), null);
        return result;
    }

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

    /// <summary>
    /// Asserts the complete parent map, not just the root: a node reattached under the wrong
    /// (but still valid, still singly-rooted) parent -- e.g. سالم under داوود instead of أحمد --
    /// changes no count and does not disturb the root, so member-count-plus-root-name alone
    /// cannot catch it. Comparing every member's own parent name against
    /// <see cref="ExpectedParentByName"/> is what actually exercises connector direction for
    /// every edge, which is what this test's own docstring claims to validate. Verified this
    /// discriminates (Task 12 review): temporarily forcing one edge in
    /// <see cref="ExpectedParentByName"/> to the wrong (but still valid, still singly-rooted)
    /// parent made this test fail with a one-line diff on exactly that entry; reverting made it
    /// pass again.
    /// </summary>
    [Fact]
    public void The_exported_hierarchy_reconstructs_to_the_source_hierarchy()
    {
        var path = RenderToFile();
        try
        {
            var reconstruction = ReconstructFromPdf(path);
            var byId = reconstruction.Members.ToDictionary(m => m.Id);

            var actualParentByName = reconstruction.Members.ToDictionary(
                m => m.Name,
                m => m.ParentId is { } parentId ? byId[parentId].Name : null);

            actualParentByName.Should().BeEquivalentTo(ExpectedParentByName());
        }
        finally { File.Delete(path); }
    }
}
