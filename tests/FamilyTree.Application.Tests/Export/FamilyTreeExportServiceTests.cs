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

    /// <summary>
    /// Records the STYLE and PAGE FORMAT as well as the caption. Recording only the caption meant
    /// a service that hardcoded "sheet", or ignored the style argument entirely, passed every
    /// Application test (final review, Minor 6) -- the selectors' whole job is to reach the
    /// renderer, and nothing asserted that they did.
    /// </summary>
    private sealed class CapturingRenderer : ITreeRendererAdapter
    {
        public PdfCaption? LastCaption { get; private set; }
        public ExportStyle? LastStyle { get; private set; }
        public string? LastPageFormat { get; private set; }

        public byte[] Render(
            IReadOnlyList<FamilyTreeNodeResponse> roots,
            ExportStyle style,
            string pageFormat,
            PdfCaption? caption = null,
            CancellationToken ct = default)
        {
            LastCaption = caption;
            LastStyle = style;
            LastPageFormat = pageFormat;
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

    /// <summary>
    /// Both selectors must arrive at the renderer exactly as chosen. Asserted for a non-default
    /// value of each, so a service that ignored its arguments and passed its own constants could
    /// not pass by coincidence.
    /// </summary>
    [Theory]
    [InlineData(ExportStyle.Clean, "a4")]
    [InlineData(ExportStyle.Xmind, "a4")]
    [InlineData(ExportStyle.Clean, "sheet")]
    public async Task The_chosen_style_and_page_format_reach_the_renderer(
        ExportStyle style, string pageFormat)
    {
        var view = new FamilyTreeViewResponse(Guid.NewGuid(), "آل سالم", [Leaf("root", 1)]);

        var renderer = new CapturingRenderer();
        var service = new FamilyTreeExportService(
            new FakeTreeService(view), renderer, new FixedTimeProvider(DateTimeOffset.UtcNow));

        await service.ExportAsync(null, null, style, pageFormat, CaptionLanguage.Ar, CancellationToken.None);

        renderer.LastStyle.Should().Be(style);
        renderer.LastPageFormat.Should().Be(pageFormat);
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
