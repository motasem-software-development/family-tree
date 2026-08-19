using System.Text;
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class SkiaTreeRendererTests
{
    private static readonly string[] Names =
        ["سليمان", "أحمد", "داوود", "فارس", "خليل", "عمر", "إبراهيم"];

    private static FamilyTreeNodeResponse Tree()
    {
        FamilyTreeNodeResponse Leaf(string name) => new(Guid.NewGuid(), name, null, 3, false, []);

        return new FamilyTreeNodeResponse(
            Guid.NewGuid(), Names[0], null, 1, false,
            [
                new FamilyTreeNodeResponse(Guid.NewGuid(), Names[1], null, 2, false,
                    [Leaf(Names[4]), Leaf(Names[5])]),
                new FamilyTreeNodeResponse(Guid.NewGuid(), Names[2], null, 2, false, [Leaf(Names[6])]),
                new FamilyTreeNodeResponse(Guid.NewGuid(), Names[3], null, 2, false, [])
            ]);
    }

    private static TreeScene Scene() =>
        SceneScaler.FitToSheet(
            new XmindLayoutStrategy().Build([Tree()], LayoutOptions.Default, SkiaTextMeasurer.Delegate),
            LayoutOptions.Default.Metrics);

    private static byte[] Rendered() =>
        new SkiaTreeRenderer().Render(Scene(), ExportPageFormat.Sheet);

    [Fact]
    public void The_output_is_a_pdf()
    {
        var pdf = Rendered();

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void The_document_declares_a_page()
    {
        Encoding.Latin1.GetString(Rendered()).Should().Contain("/MediaBox");
    }

    /// <summary>
    /// The unconditional searchability gate (design §7.2). The reference carried a /ToUnicode
    /// CMap and the import tool relied on it; an export whose names cannot be recovered is a
    /// regression, however good it looks.
    /// </summary>
    [Fact]
    public void Every_name_is_recoverable_from_the_rendered_pdf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ft-export-{Guid.NewGuid():N}.pdf");

        try
        {
            File.WriteAllBytes(path, Rendered());
            var extracted = PdfText.Extract(path);

            foreach (var name in Names)
                extracted.Should().Contain(name, "'{0}' must survive into the PDF text layer", name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
