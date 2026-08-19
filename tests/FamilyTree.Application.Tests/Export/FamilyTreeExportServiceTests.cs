using FamilyTree.Application.Export;
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class FamilyTreeExportServiceTests
{
    private sealed class FakeTreeService(FamilyTreeViewResponse view) : IFamilyTreeService
    {
        public Task<FamilyTreeResponse> GetAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FamilyTreeResponse> RenameAsync(
            RenameFamilyTreeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FamilyTreeViewResponse> GetViewAsync(
            Guid? rootId, int? maxDepth, CancellationToken ct = default) => Task.FromResult(view);
    }

    private sealed class CapturingRenderer : ITreeRendererAdapter
    {
        public PdfCaption? LastCaption { get; private set; }

        public byte[] Render(
            IReadOnlyList<FamilyTreeNodeResponse> roots,
            ExportStyle style,
            string pageFormat,
            PdfCaption? caption = null)
        {
            LastCaption = caption;
            return [1, 2, 3];
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static FamilyTreeNodeResponse Leaf(string name, int generation) =>
        new(Guid.NewGuid(), name, null, generation, false, []);

    [Fact]
    public async Task The_caption_carries_the_tree_name_member_count_generation_span_and_date()
    {
        var view = new FamilyTreeViewResponse(Guid.NewGuid(), "آل سالم",
            [
                new FamilyTreeNodeResponse(Guid.NewGuid(), "root", null, 1, false,
                    [Leaf("child", 2), Leaf("grandchild", 3)])
            ]);

        var renderer = new CapturingRenderer();
        var service = new FamilyTreeExportService(
            new FakeTreeService(view), renderer, new FixedTimeProvider(new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero)));

        await service.ExportAsync(null, null, ExportStyle.Xmind, "sheet", CaptionLanguage.En, CancellationToken.None);

        renderer.LastCaption.Should().NotBeNull();
        renderer.LastCaption!.FamilyTreeName.Should().Be("آل سالم");
        renderer.LastCaption.MemberCount.Should().Be(3);
        renderer.LastCaption.GenerationCount.Should().Be(3); // generations 1..3
        renderer.LastCaption.ExportDate.Should().Be(new DateOnly(2026, 8, 18));
        renderer.LastCaption.Language.Should().Be(CaptionLanguage.En);
    }

    [Fact]
    public async Task A_forest_with_roots_at_different_generations_spans_all_of_them()
    {
        var view = new FamilyTreeViewResponse(Guid.NewGuid(), "forest",
            [
                Leaf("root-a", 1),
                new FamilyTreeNodeResponse(Guid.NewGuid(), "root-b", null, 1, false, [Leaf("deep", 4)])
            ]);

        var renderer = new CapturingRenderer();
        var service = new FamilyTreeExportService(
            new FakeTreeService(view), renderer, new FixedTimeProvider(DateTimeOffset.UtcNow));

        await service.ExportAsync(null, null, ExportStyle.Xmind, "sheet", CaptionLanguage.Ar, CancellationToken.None);

        renderer.LastCaption!.GenerationCount.Should().Be(4); // generations 1..4
    }
}
