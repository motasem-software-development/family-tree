using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class TreeRendererAdapterTests
{
    private static FamilyTreeNodeResponse Tree()
    {
        FamilyTreeNodeResponse Leaf(string name) => new(Guid.NewGuid(), name, null, 2, false, []);

        return new FamilyTreeNodeResponse(
            Guid.NewGuid(), "root", null, 1, false,
            [
                new FamilyTreeNodeResponse(Guid.NewGuid(), "alpha", null, 2, false, [Leaf("a1")]),
                new FamilyTreeNodeResponse(Guid.NewGuid(), "beta", null, 2, false, [Leaf("b1")]),
                new FamilyTreeNodeResponse(Guid.NewGuid(), "gamma", null, 2, false, [Leaf("g1")])
            ]);
    }

    // Wiring test: without real selection, both styles fall through to xmind and this fails.
    [Fact]
    public void Clean_and_xmind_styles_render_different_pdfs_for_the_same_tree()
    {
        var adapter = new TreeRendererAdapter();
        var roots = new[] { Tree() };

        var xmind = adapter.Render(roots, ExportStyle.Xmind, "a4");
        var clean = adapter.Render(roots, ExportStyle.Clean, "a4");

        clean.Should().NotEqual(xmind);
    }

    // The wiring test above reads signal out of byte-inequality, which only means anything while
    // the renderer is deterministic. Adding a timestamp or a document /ID to SKDocumentPdfMetadata
    // would make it pass unconditionally and hide a regression that collapsed both styles onto
    // xmind again -- this guard fails first if that ever happens.
    [Fact]
    public void The_same_style_renders_the_same_bytes_twice()
    {
        var adapter = new TreeRendererAdapter();
        var roots = new[] { Tree() };

        adapter.Render(roots, ExportStyle.Xmind, "a4")
            .Should().Equal(adapter.Render(roots, ExportStyle.Xmind, "a4"));
    }
}
