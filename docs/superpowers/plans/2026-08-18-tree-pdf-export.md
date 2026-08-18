# Family Tree PDF Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Export a family tree as a PDF poster in the style of `familytree.pdf`, with correctly shaped right-to-left Arabic names.

**Architecture:** A pure layout engine in `FamilyTree.Application` turns the tree into an immutable `TreeScene` (positioned nodes, connector paths, branch colours). A renderer in `FamilyTree.Infrastructure` draws that scene with SkiaSharp + HarfBuzzSharp into a PDF. One API endpoint streams the bytes. The layout engine has no SkiaSharp reference; text measurement enters as an injected delegate, which is what lets every layout test run without a font or native binary.

**Tech Stack:** .NET 10, SkiaSharp + SkiaSharp.HarfBuzz, xunit + FluentAssertions, React 19 + Vite + vitest, PostgreSQL via Testcontainers.

**Spec:** `docs/superpowers/specs/2026-08-18-tree-pdf-export-design.md`

## Global Constraints

- `TargetFramework` is `net10.0`; `Nullable` is enabled; **`TreatWarningsAsErrors` is true** — a warning fails the build (`Directory.Build.props`).
- **`FamilyTree.Application` must never reference SkiaSharp.** Its only project references are `FamilyTree.Domain` and `FamilyTree.Contracts`. Adding a package reference to Application is a plan violation.
- Dependencies point inward: `Domain` → nothing, `Application` → `Domain`, `Infrastructure` → `Application`, `Api` → all.
- Immutability: types are `sealed record` / `readonly record struct`. Never mutate a caller's object; return new instances. Build-time scratch types may be mutable but must not escape their pass.
- Files stay under 800 lines; functions under 50 lines.
- Tests use **xunit** with **FluentAssertions** (`result.Should().Be(...)`). Test names are `Snake_case_sentences`, matching the existing suite.
- Errors are RFC 7807 Problem Details carrying a stable `code`; message text is never the contract.
- Every task ends with a commit. Conventional commit types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`.
- Run backend tests with `dotnet test`; integration tests need Docker running. Run frontend tests with `cd frontend && npm test`.

### Measured constants (spec §3, from `familytree.pdf` — do not re-derive)

| Constant | Value |
|---|---|
| Leaf row pitch | `15.0` pt |
| Sibling-group separation | `29.5` pt |
| Connector stroke | `1.48` pt |
| Elbow corner radius | `6.0` pt |
| Centre box stroke | `2.22` pt |
| Font size — centre / level 1 / body | `26.68` / `17.78` / `13.34` pt |
| Centre colour (stroke only) | `#8793A5` |
| Branch colours 1–4 | `#518CD8`, `#FD6D5A`, `#6DC354`, `#FEB40B` |
| Max page extent | `14400.0` pt |
| Minimum font size floor | `6.0` pt |
| Member cap | `10000` |
| Render concurrency | `2` |

---

## File Structure

**Created — `FamilyTree.Application/Export/`** (pure, no Skia)

| File | Responsibility |
|---|---|
| `TreeScene.cs` | Immutable scene data: `SceneNode`, `SceneConnector`, `SceneBounds`, `TreeScene`. |
| `LayoutOptions.cs` | `LayoutMetrics`, `BranchPalette`, `MeasureText` delegate, `LayoutOptions`. |
| `PackedNode.cs` | Internal build-time tree used by passes 2–3. Never escapes the layout. |
| `VerticalPacking.cs` | Pass 2: block heights and vertical centres. |
| `ColumnAssignment.cs` | Pass 3: per-branch, per-depth column x. |
| `SideAssignment.cs` | Pass 4: balance top-level branches across the two sides. |
| `ConnectorBuilder.cs` | Ribbons, elbows, ticks. |
| `XmindLayoutStrategy.cs` | Orchestrates passes 1–5 into a `TreeScene`. |
| `CleanLayoutStrategy.cs` | Task 14. The second style. |
| `SceneScaler.cs` | Overflow scaling and the 6 pt floor rejection. |
| `IFamilyTreeExporter.cs` / `FamilyTreeExportService.cs` | Application service: tree → PDF bytes. |

**Created — `FamilyTree.Infrastructure/Export/`** (owns Skia)

| File | Responsibility |
|---|---|
| `EmbeddedFonts.cs` | Loads the two Noto typefaces from embedded resources, cached. |
| `SkiaTextMeasurer.cs` | Supplies the `MeasureText` delegate via HarfBuzz shaping. |
| `SkiaTreeRenderer.cs` | Draws a `TreeScene` onto an `SKDocument` PDF canvas. |
| `SheetPaginator.cs` | Single-page emission. |
| `A4Paginator.cs` | Task 13. Tiling, bleed, continuation. |
| `TreeRendererAdapter.cs` | Application-facing seam that picks strategy and format. |
| `Fonts/NotoSansArabic-Bold.ttf`, `Fonts/NotoSans-Bold.ttf` | Embedded resources. |

**Modified**

| File | Change |
|---|---|
| `src/FamilyTree.Domain/Common/DomainException.cs` | Add `TooLargeException` carrying a `Reason`. |
| `src/FamilyTree.Api/Errors/ExceptionHandler.cs` | Map `TooLargeException` → 413 + `reason` extension. |
| `src/FamilyTree.Api/Endpoints/FamilyTrees/FamilyTreeEndpoints.cs` | Add the export endpoint. |
| `src/FamilyTree.Api/Program.cs` | Register the exporter and adapter. |
| `src/FamilyTree.Api/Dockerfile` | SkiaSharp native dependencies. |
| `frontend/src/services/apiClient.ts` | Extract the fetch/refresh core; add `apiFetchBlob`. |
| `frontend/src/features/tree/TreePage.tsx` | Export control. |
| `tools/FamilyTree.Import/Geometry.cs` | Generalise the path classifier (Task 12). |
| `README.md` | Endpoint and error-code documentation. |

---

## Task 1: Scene and options types

**Files:**
- Create: `src/FamilyTree.Application/Export/TreeScene.cs`
- Create: `src/FamilyTree.Application/Export/LayoutOptions.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/BranchPaletteTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `TreeScene`, `SceneNode`, `SceneConnector`, `SceneBounds`, `ScenePoint`, `NodeShape`, `ConnectorKind`, `LayoutMetrics`, `BranchPalette`, `LayoutOptions`, and `delegate double MeasureText(string text, double fontSize)`. Every later task uses these names exactly.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/BranchPaletteTests.cs`:

```csharp
using FamilyTree.Application.Export;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class BranchPaletteTests
{
    [Fact]
    public void The_first_four_hues_are_the_measured_reference_colours_in_order()
    {
        BranchPalette.Default.ColorAt(0).Should().Be("#518CD8");
        BranchPalette.Default.ColorAt(1).Should().Be("#FD6D5A");
        BranchPalette.Default.ColorAt(2).Should().Be("#6DC354");
        BranchPalette.Default.ColorAt(3).Should().Be("#FEB40B");
    }

    [Fact]
    public void The_palette_cycles_beyond_its_length()
    {
        BranchPalette.Default.ColorAt(12).Should().Be(BranchPalette.Default.ColorAt(0));
        BranchPalette.Default.ColorAt(13).Should().Be(BranchPalette.Default.ColorAt(1));
    }

    [Fact]
    public void Every_hue_is_distinct()
    {
        BranchPalette.Default.Colors.Should().OnlyHaveUniqueItems();
        BranchPalette.Default.Colors.Should().HaveCount(12);
    }

    // Spec §3.1: these documents get printed on mono printers, so neighbouring branches must
    // stay separable without colour.
    [Fact]
    public void Adjacent_hues_stay_separable_in_greyscale()
    {
        var luminances = BranchPalette.Default.Colors.Select(RelativeLuminance).ToList();

        for (var i = 0; i < luminances.Count - 1; i++)
            Math.Abs(luminances[i + 1] - luminances[i]).Should()
                .BeGreaterThanOrEqualTo(0.03, "hues {0} and {1} must differ in print", i, i + 1);
    }

    private static double RelativeLuminance(string hex)
    {
        double Channel(int offset)
        {
            var c = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter BranchPaletteTests`
Expected: FAIL — build error, `BranchPalette` does not exist.

- [ ] **Step 3: Write the scene types**

`src/FamilyTree.Application/Export/TreeScene.cs`:

```csharp
namespace FamilyTree.Application.Export;

public readonly record struct ScenePoint(double X, double Y);

/// <summary>
/// Only the centre and the top-level children are drawn as rounded rectangles; every other
/// node is a label sitting on a horizontal tick (design §2.2).
/// </summary>
public enum NodeShape { Tick, RoundedBox }

/// <summary>
/// Centre-to-level-1 links are filled tapered ribbons; everything deeper is a stroked
/// orthogonal elbow (design §4.3).
/// </summary>
public enum ConnectorKind { Ribbon, Elbow }

/// <param name="X">Left edge of the node's box in scene coordinates.</param>
/// <param name="Y">Vertical centre of the node, not its top — every layout pass reasons
/// about centres, and only the renderer converts to a baseline.</param>
public sealed record SceneNode(
    Guid Id,
    string Label,
    double X,
    double Y,
    double Width,
    double Height,
    double FontSize,
    string Color,
    NodeShape Shape);

/// <param name="Points">
/// For <see cref="ConnectorKind.Elbow"/>: an orthogonal polyline the renderer rounds at each
/// interior vertex. For <see cref="ConnectorKind.Ribbon"/>: exactly eight points forming a
/// closed teardrop — start edge, two controls, tip, tip, two controls, opposite start edge.
/// </param>
public sealed record SceneConnector(
    ConnectorKind Kind,
    IReadOnlyList<ScenePoint> Points,
    string Color,
    double StrokeWidth);

public sealed record SceneBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

/// <param name="Scale">
/// 1.0 unless overflow forced a uniform reduction (design §4.4). The renderer applies it; the
/// layout coordinates stay unscaled so tests read the same numbers either way.
/// </param>
public sealed record TreeScene(
    IReadOnlyList<SceneNode> Nodes,
    IReadOnlyList<SceneConnector> Connectors,
    SceneBounds Bounds,
    double Scale = 1.0);
```

- [ ] **Step 4: Write the options types**

`src/FamilyTree.Application/Export/LayoutOptions.cs`:

```csharp
namespace FamilyTree.Application.Export;

/// <summary>
/// Width of a shaped label at a given size. Injected because shaped-text width is a font fact
/// and Application may not reference SkiaSharp (design §4.2). Tests pass a stub.
/// </summary>
public delegate double MeasureText(string text, double fontSize);

/// <summary>Defaults are measured from <c>familytree.pdf</c> (design §3.2). Do not re-derive.</summary>
public sealed record LayoutMetrics
{
    public double LeafPitch { get; init; } = 15.0;
    public double GroupSeparation { get; init; } = 29.5;
    public double ColumnGap { get; init; } = 14.0;
    public double LabelPadding { get; init; } = 6.0;
    public double ConnectorStroke { get; init; } = 1.48;
    public double CentreStroke { get; init; } = 2.22;
    public double CornerRadius { get; init; } = 6.0;
    public double CentreFontSize { get; init; } = 26.68;
    public double LevelOneFontSize { get; init; } = 17.78;
    public double BodyFontSize { get; init; } = 13.34;
    public double Margin { get; init; } = 24.0;
    public double MinFontSize { get; init; } = 6.0;
    public double MaxPageExtent { get; init; } = 14400.0;
    public double RibbonHalfWidth { get; init; } = 5.0;

    /// <summary>Extra space inserted between two siblings when either has children of its own.
    /// Turns the uniform leaf pitch into the reference's bimodal 15 / 29.5 rhythm.</summary>
    public double SiblingGroupGap => GroupSeparation - LeafPitch;

    public double FontSizeForDepth(int depth) => depth switch
    {
        0 => CentreFontSize,
        1 => LevelOneFontSize,
        _ => BodyFontSize
    };

    public NodeShape ShapeForDepth(int depth) => depth <= 1 ? NodeShape.RoundedBox : NodeShape.Tick;
}

/// <summary>
/// Hue binds to top-level branch index and is inherited by every descendant (design §3.1).
/// The first four are the reference's measured colours; the remaining eight are chosen to stay
/// separable from their neighbours both in colour and in greyscale.
/// </summary>
public sealed record BranchPalette(IReadOnlyList<string> Colors, string CentreColor)
{
    public static BranchPalette Default { get; } = new(
        [
            "#518CD8", "#FD6D5A", "#6DC354", "#FEB40B",
            "#9B6BD6", "#2FB0A5", "#E5568E", "#8A9A2B",
            "#4A6FA5", "#D97A2B", "#5FA8D3", "#A05252"
        ],
        "#8793A5");

    public string ColorAt(int branchIndex) => Colors[branchIndex % Colors.Count];
}

public sealed record LayoutOptions(LayoutMetrics Metrics, BranchPalette Palette)
{
    public static LayoutOptions Default { get; } = new(new LayoutMetrics(), BranchPalette.Default);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter BranchPaletteTests`
Expected: PASS — 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyTree.Application/Export tests/FamilyTree.Application.Tests/Export
git commit -m "feat: add the PDF export scene and layout option types"
```

---

## Task 2: Vertical packing (pass 2)

**Files:**
- Create: `src/FamilyTree.Application/Export/PackedNode.cs`
- Create: `src/FamilyTree.Application/Export/VerticalPacking.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/VerticalPackingTests.cs`

**Interfaces:**
- Consumes: `LayoutMetrics`, `MeasureText` (Task 1); `FamilyTreeNodeResponse` from `FamilyTree.Contracts.FamilyTrees`.
- Produces: `sealed class PackedNode` with mutable `X`, `Y`, `Width`, `Top`, `Bottom`, a derived `Height => Bottom - Top`, readonly `Source`, `Depth`, `BranchIndex`, `Children`, plus `Descend()` and `Shift(double)`; `VerticalPacking.Pack(IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutMetrics metrics, MeasureText measure) -> IReadOnlyList<PackedNode>`.

A leaf occupies one `LeafPitch`. Consecutive siblings are separated by `SiblingGroupGap` **only when either has children** — that is what produces the reference's bimodal spacing. A parent's centre is the **midpoint of its first and last child's centres**, not their mean.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/VerticalPackingTests.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class VerticalPackingTests
{
    private static readonly LayoutMetrics Metrics = new();

    /// <summary>Fixed-width stub: layout must never depend on a real font (design §4.2).</summary>
    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static PackedNode Pack(FamilyTreeNodeResponse root) =>
        VerticalPacking.Pack([root], Metrics, Stub).Single();

    [Fact]
    public void A_leaf_occupies_one_leaf_pitch()
    {
        Pack(Node("a")).Height.Should().Be(Metrics.LeafPitch);
    }

    [Fact]
    public void Adjacent_leaves_are_one_leaf_pitch_apart()
    {
        var packed = Pack(Node("p", Node("a"), Node("b")));

        var gap = packed.Children[1].Y - packed.Children[0].Y;
        gap.Should().BeApproximately(Metrics.LeafPitch, 1e-9);
    }

    // The reference's rhythm is bimodal: ~15pt between leaves, ~29.5pt where a sibling group
    // begins (design §3.2). A single constant pitch does not reproduce it.
    [Fact]
    public void A_sibling_with_children_earns_the_wider_group_separation()
    {
        var packed = Pack(Node("p", Node("a"), Node("b", Node("b1"))));

        var gap = packed.Children[1].Y - packed.Children[0].Y;
        gap.Should().BeApproximately(Metrics.GroupSeparation, 1e-9);
    }

    [Fact]
    public void A_parent_centres_between_its_first_and_last_child()
    {
        var packed = Pack(Node("p", Node("a"), Node("b"), Node("c")));

        var expected = (packed.Children[0].Y + packed.Children[^1].Y) / 2;
        packed.Y.Should().BeApproximately(expected, 1e-9);
    }

    // The distinguishing rule: with a lopsided subtree the parent must straddle first and last,
    // NOT sit at the mean of all child centres. Those differ here, and the reference uses the
    // former (design §4.3 pass 2).
    [Fact]
    public void A_parent_of_a_lopsided_subtree_straddles_rather_than_averages()
    {
        // Three children, not two: for exactly two the mean and the straddle are equal by
        // definition, so a two-child fixture cannot discriminate the rule at all.
        var packed = Pack(Node("p",
            Node("a", Node("a1"), Node("a2"), Node("a3")),
            Node("b"),
            Node("c")));

        var straddle = (packed.Children[0].Y + packed.Children[^1].Y) / 2;
        var mean = packed.Children.Average(c => c.Y);

        mean.Should().NotBeApproximately(straddle, 1e-6, "the fixture must actually discriminate");
        packed.Y.Should().BeApproximately(straddle, 1e-9);
    }

    [Fact]
    public void A_parent_block_is_as_tall_as_its_children_plus_their_gaps()
    {
        var packed = Pack(Node("p", Node("a"), Node("b"), Node("c")));

        packed.Height.Should().BeApproximately(3 * Metrics.LeafPitch, 1e-9);
    }

    [Fact]
    public void Depth_and_branch_index_are_carried_down_the_tree()
    {
        var roots = VerticalPacking.Pack(
            [Node("root", Node("x", Node("x1")), Node("y"))], Metrics, Stub);

        var root = roots.Single();
        root.Depth.Should().Be(0);
        root.Children[0].Depth.Should().Be(1);
        root.Children[0].BranchIndex.Should().Be(0);
        root.Children[0].Children[0].Depth.Should().Be(2);
        root.Children[0].Children[0].BranchIndex.Should().Be(0);
        root.Children[1].BranchIndex.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter VerticalPackingTests`
Expected: FAIL — `VerticalPacking` does not exist.

- [ ] **Step 3: Write PackedNode**

`src/FamilyTree.Application/Export/PackedNode.cs`:

```csharp
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

/// <summary>
/// Build-time scratch node. Mutable by design — the passes fill coordinates in stages — but it
/// never escapes the layout strategy, which freezes it into an immutable
/// <see cref="TreeScene"/>. Nothing outside the Export namespace may depend on it.
/// </summary>
public sealed class PackedNode(
    FamilyTreeNodeResponse source, int depth, int branchIndex, IReadOnlyList<PackedNode> children)
{
    public FamilyTreeNodeResponse Source { get; } = source;
    public int Depth { get; } = depth;

    /// <summary>Index of the top-level ancestor this node hangs from; drives hue (design §3.1).</summary>
    public int BranchIndex { get; } = branchIndex;

    public IReadOnlyList<PackedNode> Children { get; } = children;

    /// <summary>Vertical centre. Set by pass 2, translated by passes 4 and 5.</summary>
    public double Y { get; set; }

    /// <summary>Left edge. Set by pass 3, translated by pass 5.</summary>
    public double X { get; set; }

    public double Width { get; set; }

    /// <summary>Top of the vertical band this node's whole subtree occupies.</summary>
    public double Top { get; set; }

    /// <summary>Bottom of that band. Kept explicitly rather than derived from Y, because a
    /// parent's centre is a straddle and so does not sit at the band's midpoint.</summary>
    public double Bottom { get; set; }

    public double Height => Bottom - Top;

    public bool IsLeaf => Children.Count == 0;

    /// <summary>Moves this node and its whole subtree down by <paramref name="delta"/>.</summary>
    public void Shift(double delta)
    {
        foreach (var node in Descend())
        {
            node.Y += delta;
            node.Top += delta;
            node.Bottom += delta;
        }
    }

    /// <summary>This node and every descendant, pre-order.</summary>
    public IEnumerable<PackedNode> Descend()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.Descend())
                yield return node;
    }
}
```

- [ ] **Step 4: Write the packing pass**

`src/FamilyTree.Application/Export/VerticalPacking.cs`:

```csharp
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

/// <summary>
/// Pass 2 (design §4.3). Bottom-up: gives every node a block height and a vertical centre,
/// relative to the top of its own subtree. Later passes translate the result into page space.
/// </summary>
public static class VerticalPacking
{
    public static IReadOnlyList<PackedNode> Pack(
        IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutMetrics metrics, MeasureText measure) =>
        roots
            .Select((root, index) => Build(root, depth: 0, branchIndex: index, metrics, measure))
            .ToList();

    private static PackedNode Build(
        FamilyTreeNodeResponse source, int depth, int branchIndex,
        LayoutMetrics metrics, MeasureText measure)
    {
        // A top-level child owns its own hue; deeper nodes inherit their ancestor's.
        var children = source.Children
            .Select((child, index) => Build(
                child, depth + 1, depth == 0 ? index : branchIndex, metrics, measure))
            .ToList();

        var node = new PackedNode(source, depth, branchIndex, children)
        {
            Width = measure(source.Name, metrics.FontSizeForDepth(depth)) + metrics.LabelPadding * 2
        };

        if (node.IsLeaf)
        {
            node.Top = 0;
            node.Bottom = metrics.LeafPitch;
            node.Y = metrics.LeafPitch / 2;
            return node;
        }

        StackChildren(children, metrics);

        var first = children[0];
        var last = children[^1];

        node.Top = first.Top;
        node.Bottom = last.Bottom;

        // Straddle first and last rather than averaging every child: with a lopsided subtree
        // the two differ, and the reference uses the straddle (design §4.3 pass 2). This is
        // also why Top/Bottom are tracked explicitly — Y is not the band's midpoint.
        node.Y = (first.Y + last.Y) / 2;
        return node;
    }

    private static void StackChildren(IReadOnlyList<PackedNode> children, LayoutMetrics metrics)
    {
        var cursor = 0.0;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];

            if (i > 0)
            {
                // The wider separation marks where one sibling group ends and the next begins.
                // Two adjacent leaves are simply one leaf pitch apart.
                var needsGroupGap = !children[i - 1].IsLeaf || !child.IsLeaf;
                if (needsGroupGap) cursor += metrics.SiblingGroupGap;
            }

            child.Shift(cursor - child.Top);
            cursor = child.Bottom;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter VerticalPackingTests`
Expected: PASS — 7 tests.

If `A_parent_block_is_as_tall_as_its_children_plus_their_gaps` fails, `StackChildren` is
advancing the cursor by something other than the child's own `Bottom`. Fix the implementation,
not the test.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyTree.Application/Export tests/FamilyTree.Application.Tests/Export
git commit -m "feat: pack the export tree vertically with the reference's bimodal pitch"
```

---

## Task 3: Column assignment (pass 3)

**Files:**
- Create: `src/FamilyTree.Application/Export/ColumnAssignment.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/ColumnAssignmentTests.cs`

**Interfaces:**
- Consumes: `PackedNode`, `LayoutMetrics`.
- Produces: `ColumnAssignment.Assign(PackedNode branchRoot, double startX, int direction, LayoutMetrics metrics) -> double`, where `direction` is `+1` (grows right) or `-1` (grows left). Sets `X` on every node in the branch and returns the branch's outer extent.

Column width is the **widest label at that depth within this branch** — which is why the reference's pitch varies between 50 and 69 pt. Columns are per-branch, so a wide name in one branch does not push another outward.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/ColumnAssignmentTests.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class ColumnAssignmentTests
{
    private static readonly LayoutMetrics Metrics = new();

    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static PackedNode Packed(FamilyTreeNodeResponse root) =>
        VerticalPacking.Pack([root], Metrics, Stub).Single();

    [Fact]
    public void Every_node_at_the_same_depth_shares_one_column()
    {
        var root = Packed(Node("r", Node("aaaa", Node("x")), Node("b", Node("yyyyyy"))));
        ColumnAssignment.Assign(root, startX: 0, direction: 1, Metrics);

        root.Children[0].X.Should().Be(root.Children[1].X);
        root.Children[0].Children[0].X.Should().Be(root.Children[1].Children[0].X);
    }

    [Fact]
    public void A_column_is_as_wide_as_its_widest_label_plus_the_gap()
    {
        var root = Packed(Node("r", Node("aaaa", Node("x")), Node("b", Node("yyyyyy"))));
        ColumnAssignment.Assign(root, startX: 0, direction: 1, Metrics);

        var widestAtDepthOne = Math.Max(root.Children[0].Width, root.Children[1].Width);
        var pitch = root.Children[0].Children[0].X - root.Children[0].X;

        pitch.Should().BeApproximately(widestAtDepthOne + Metrics.ColumnGap, 1e-9);
    }

    [Fact]
    public void A_leftward_branch_mirrors_a_rightward_one()
    {
        var right = Packed(Node("r", Node("a", Node("x"))));
        var left = Packed(Node("r", Node("a", Node("x"))));

        ColumnAssignment.Assign(right, startX: 0, direction: 1, Metrics);
        ColumnAssignment.Assign(left, startX: 0, direction: -1, Metrics);

        // Mirrored about startX: the left branch's node right edge lands where the right
        // branch's left edge does, reflected.
        var rightChild = right.Children[0].Children[0];
        var leftChild = left.Children[0].Children[0];

        (leftChild.X + leftChild.Width).Should().BeApproximately(-rightChild.X, 1e-9);
    }

    [Fact]
    public void The_returned_extent_is_the_branch_outer_edge()
    {
        var root = Packed(Node("r", Node("a", Node("x"))));
        var extent = ColumnAssignment.Assign(root, startX: 0, direction: 1, Metrics);

        var deepest = root.Children[0].Children[0];
        extent.Should().BeApproximately(deepest.X + deepest.Width, 1e-9);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter ColumnAssignmentTests`
Expected: FAIL — `ColumnAssignment` does not exist.

- [ ] **Step 3: Write the column pass**

`src/FamilyTree.Application/Export/ColumnAssignment.cs`:

```csharp
namespace FamilyTree.Application.Export;

/// <summary>
/// Pass 3 (design §4.3). Within one branch, every node at the same depth shares an x, and each
/// column is sized to its own widest label — which is why the reference's column pitch varies
/// between 50 and 69pt rather than being a fixed indent. Columns are per-branch so a wide name
/// in one branch cannot push a sibling branch outward.
/// </summary>
public static class ColumnAssignment
{
    /// <param name="direction">+1 grows to the right, -1 mirrors to the left.</param>
    /// <returns>The branch's outer extent, as a signed x in scene coordinates.</returns>
    public static double Assign(
        PackedNode branchRoot, double startX, int direction, LayoutMetrics metrics)
    {
        var widestByDepth = new Dictionary<int, double>();
        foreach (var node in branchRoot.Descend())
            widestByDepth[node.Depth] = Math.Max(
                widestByDepth.GetValueOrDefault(node.Depth), node.Width);

        var leadingEdgeByDepth = new Dictionary<int, double>();
        var cursor = startX;
        for (var depth = branchRoot.Depth; widestByDepth.ContainsKey(depth); depth++)
        {
            leadingEdgeByDepth[depth] = cursor;
            cursor += direction * (widestByDepth[depth] + metrics.ColumnGap);
        }

        var extent = startX;
        foreach (var node in branchRoot.Descend())
        {
            var leadingEdge = leadingEdgeByDepth[node.Depth];
            // X always means the left edge. Growing leftwards, the leading edge is the node's
            // right edge, so shift by its own width to keep X meaning one thing everywhere.
            node.X = direction > 0 ? leadingEdge : leadingEdge - node.Width;

            var outer = direction > 0 ? node.X + node.Width : node.X;
            extent = direction > 0 ? Math.Max(extent, outer) : Math.Min(extent, outer);
        }

        return extent;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter ColumnAssignmentTests`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyTree.Application/Export tests/FamilyTree.Application.Tests/Export
git commit -m "feat: assign per-branch content-driven columns to the export layout"
```

---

## Task 4: Side assignment (pass 4)

**Files:**
- Create: `src/FamilyTree.Application/Export/SideAssignment.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/SideAssignmentTests.cs`

**Interfaces:**
- Consumes: `PackedNode`.
- Produces: `enum Side { Right, Left }` and `SideAssignment.Assign(IReadOnlyList<PackedNode> topLevel) -> IReadOnlyDictionary<PackedNode, Side>`. Heaviest-first greedy onto the lighter side; original sibling order preserved within each side.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/SideAssignmentTests.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class SideAssignmentTests
{
    private static readonly LayoutMetrics Metrics = new();

    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static FamilyTreeNodeResponse Leaves(string name, int count) =>
        Node(name, Enumerable.Range(0, count).Select(i => Node($"{name}{i}")).ToArray());

    private static IReadOnlyList<PackedNode> TopLevel(params FamilyTreeNodeResponse[] children) =>
        VerticalPacking.Pack([Node("root", children)], Metrics, Stub).Single().Children;

    [Fact]
    public void The_heaviest_branch_goes_opposite_the_rest_when_it_dominates()
    {
        var top = TopLevel(Leaves("big", 40), Leaves("a", 5), Leaves("b", 5), Node("c"));
        var sides = SideAssignment.Assign(top);

        var heavy = top.OrderByDescending(n => n.Height).First();
        top.Where(n => n != heavy).Should().OnlyContain(n => sides[n] != sides[heavy]);
    }

    [Fact]
    public void The_two_sides_end_up_balanced()
    {
        var top = TopLevel(Leaves("a", 20), Leaves("b", 12), Leaves("c", 9), Node("d"));
        var sides = SideAssignment.Assign(top);

        var right = top.Where(n => sides[n] == Side.Right).Sum(n => n.Height);
        var left = top.Where(n => sides[n] == Side.Left).Sum(n => n.Height);

        Math.Min(right, left).Should().BeGreaterThanOrEqualTo(0.8 * Math.Max(right, left));
    }

    [Fact]
    public void Every_branch_is_assigned_exactly_one_side()
    {
        var top = TopLevel(Leaves("a", 20), Leaves("b", 3), Leaves("c", 18), Leaves("d", 2));
        var sides = SideAssignment.Assign(top);

        sides.Should().HaveCount(top.Count);
        top.Should().OnlyContain(node => sides.ContainsKey(node));
    }

    // Ties must not depend on an unstable sort, or two runs of the same tree produce
    // different pictures.
    [Fact]
    public void Equal_weight_branches_assign_deterministically()
    {
        var first = SideAssignment.Assign(TopLevel(Leaves("a", 5), Leaves("b", 5)));
        var second = SideAssignment.Assign(TopLevel(Leaves("a", 5), Leaves("b", 5)));

        first.Values.Should().Equal(second.Values);
    }

    [Fact]
    public void A_single_branch_takes_the_right_side()
    {
        var top = TopLevel(Leaves("only", 4));
        SideAssignment.Assign(top).Values.Should().AllBeEquivalentTo(Side.Right);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter SideAssignmentTests`
Expected: FAIL — `SideAssignment` does not exist.

- [ ] **Step 3: Write the side pass**

`src/FamilyTree.Application/Export/SideAssignment.cs`:

```csharp
namespace FamilyTree.Application.Export;

public enum Side { Right, Left }

/// <summary>
/// Pass 4 (design §4.3). Greedy heaviest-first packing across two sides. On the seeded tree
/// this reproduces the reference: the dominant branch alone on one side, the rest stacked
/// opposite, with the two masses within a fifth of each other.
/// </summary>
public static class SideAssignment
{
    public static IReadOnlyDictionary<PackedNode, Side> Assign(IReadOnlyList<PackedNode> topLevel)
    {
        var sides = new Dictionary<PackedNode, Side>();
        var loads = new Dictionary<Side, double> { [Side.Right] = 0, [Side.Left] = 0 };

        // Ties break by original index so the assignment is deterministic across runs — a
        // coordinate-level test cannot tolerate an unstable sort.
        var heaviestFirst = topLevel
            .Select((node, index) => (node, index))
            .OrderByDescending(entry => entry.node.Height)
            .ThenBy(entry => entry.index);

        foreach (var (node, _) in heaviestFirst)
        {
            var side = loads[Side.Right] <= loads[Side.Left] ? Side.Right : Side.Left;
            sides[node] = side;
            loads[side] += node.Height;
        }

        return sides;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter SideAssignmentTests`
Expected: PASS — 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyTree.Application/Export tests/FamilyTree.Application.Tests/Export
git commit -m "feat: balance top-level export branches across both sides"
```

---

## Task 5: Connectors and the assembled scene (passes 1 and 5)

**Files:**
- Create: `src/FamilyTree.Application/Export/ConnectorBuilder.cs`
- Create: `src/FamilyTree.Application/Export/ILayoutStrategy.cs`
- Create: `src/FamilyTree.Application/Export/SceneNormaliser.cs`
- Create: `src/FamilyTree.Application/Export/XmindLayoutStrategy.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/XmindLayoutStrategyTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–4.
- Produces: `interface ILayoutStrategy { string Name { get; } TreeScene Build(IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutOptions options, MeasureText measure); }`; `sealed class XmindLayoutStrategy : ILayoutStrategy`; `ConnectorBuilder.Ribbon/Elbow/Tick`; `SceneNormaliser.Normalise(nodes, connectors, metrics) -> TreeScene`.

A **forest** (more than one stored root) gets a synthetic invisible centre so no root is dropped. A single root becomes the centre directly.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/XmindLayoutStrategyTests.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class XmindLayoutStrategyTests
{
    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static TreeScene Build(params FamilyTreeNodeResponse[] roots) =>
        new XmindLayoutStrategy().Build(roots, LayoutOptions.Default, Stub);

    [Fact]
    public void Every_member_appears_exactly_once_in_the_scene()
    {
        var scene = Build(Node("r", Node("a", Node("a1"), Node("a2")), Node("b")));

        scene.Nodes.Should().HaveCount(5);
        scene.Nodes.Select(n => n.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Descendants_inherit_their_top_level_ancestors_hue()
    {
        var scene = Build(Node("r", Node("a", Node("a1", Node("a2"))), Node("b")));
        var byLabel = scene.Nodes.ToDictionary(n => n.Label);

        byLabel["a1"].Color.Should().Be(byLabel["a"].Color);
        byLabel["a2"].Color.Should().Be(byLabel["a"].Color);
        byLabel["b"].Color.Should().NotBe(byLabel["a"].Color);
    }

    [Fact]
    public void The_centre_and_its_children_are_boxed_and_everything_deeper_is_a_tick()
    {
        var scene = Build(Node("r", Node("a", Node("a1"))));
        var byLabel = scene.Nodes.ToDictionary(n => n.Label);

        byLabel["r"].Shape.Should().Be(NodeShape.RoundedBox);
        byLabel["a"].Shape.Should().Be(NodeShape.RoundedBox);
        byLabel["a1"].Shape.Should().Be(NodeShape.Tick);
    }

    [Fact]
    public void The_centre_uses_the_reserved_centre_colour()
    {
        var scene = Build(Node("r", Node("a")));
        scene.Nodes.Single(n => n.Label == "r").Color
            .Should().Be(BranchPalette.Default.CentreColor);
    }

    [Fact]
    public void Centre_to_level_one_links_are_ribbons_and_deeper_links_are_elbows()
    {
        var scene = Build(Node("r", Node("a", Node("a1"))));

        scene.Connectors.Count(c => c.Kind == ConnectorKind.Ribbon).Should().Be(1);
        scene.Connectors.Should().Contain(c => c.Kind == ConnectorKind.Elbow);
    }

    [Fact]
    public void A_ribbon_carries_the_eight_points_of_a_closed_teardrop()
    {
        var scene = Build(Node("r", Node("a")));
        scene.Connectors.Single(c => c.Kind == ConnectorKind.Ribbon).Points.Should().HaveCount(8);
    }

    [Fact]
    public void Branches_land_on_both_sides_of_the_centre()
    {
        var scene = Build(Node("r", Node("a"), Node("b")));
        var centre = scene.Nodes.Single(n => n.Label == "r");

        scene.Nodes.Where(n => n.Label is "a" or "b")
            .Select(n => n.X > centre.X)
            .Should().OnlyHaveUniqueItems("one branch goes right and the other left");
    }

    [Fact]
    public void The_scene_is_normalised_to_the_origin()
    {
        var scene = Build(Node("r", Node("a", Node("a1")), Node("b")));

        scene.Bounds.MinX.Should().Be(0);
        scene.Bounds.MinY.Should().Be(0);
        scene.Nodes.Select(n => n.X).Min().Should().BeGreaterThanOrEqualTo(0);
        scene.Bounds.Width.Should().BeGreaterThan(0);
    }

    // Design §4.3: the API returns RootMembers as a collection, and no root may be silently
    // dropped just because the tree is a forest.
    [Fact]
    public void A_forest_gets_a_synthetic_centre_and_keeps_every_root()
    {
        var scene = Build(Node("one", Node("x")), Node("two", Node("y")));

        scene.Nodes.Select(n => n.Label).Should().Contain(["one", "two", "x", "y"]);
        scene.Nodes.Should().NotContain(n => n.Label == string.Empty);
        scene.Nodes.Single(n => n.Label == "one").Color
            .Should().NotBe(scene.Nodes.Single(n => n.Label == "two").Color);
    }

    [Fact]
    public void An_empty_tree_produces_an_empty_scene_rather_than_throwing()
    {
        var scene = new XmindLayoutStrategy().Build([], LayoutOptions.Default, Stub);

        scene.Nodes.Should().BeEmpty();
        scene.Connectors.Should().BeEmpty();
        scene.Bounds.Width.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter XmindLayoutStrategyTests`
Expected: FAIL — `XmindLayoutStrategy` does not exist.

- [ ] **Step 3: Write the connector builder**

`src/FamilyTree.Application/Export/ConnectorBuilder.cs`:

```csharp
namespace FamilyTree.Application.Export;

/// <summary>
/// Builds the reference's two connector vocabularies (design §4.3). The renderer draws what it
/// is given and makes no geometric decisions of its own.
/// </summary>
public static class ConnectorBuilder
{
    /// <summary>
    /// Centre → level 1: a closed teardrop, thick at the centre and tapering to the child. The
    /// reference achieves its taper by filling a shape rather than stroking a line, so this is
    /// a fill path — hence a zero stroke width.
    /// </summary>
    public static SceneConnector Ribbon(
        ScenePoint from, ScenePoint to, double halfWidth, string color)
    {
        var midX = (from.X + to.X) / 2;

        var upper = new ScenePoint(from.X, from.Y - halfWidth);
        var lower = new ScenePoint(from.X, from.Y + halfWidth);

        return new SceneConnector(
            ConnectorKind.Ribbon,
            [
                upper,
                new ScenePoint(midX, upper.Y),
                new ScenePoint(midX, to.Y),
                to,
                to,
                new ScenePoint(midX, to.Y),
                new ScenePoint(midX, lower.Y),
                lower
            ],
            color,
            StrokeWidth: 0);
    }

    /// <summary>
    /// Level 2+: parent tick outer end → shared junction column → child row → child tick start.
    /// An orthogonal polyline; the renderer rounds each interior vertex by the corner radius.
    /// </summary>
    public static SceneConnector Elbow(
        ScenePoint from, ScenePoint to, double junctionX, string color, double stroke) =>
        new(
            ConnectorKind.Elbow,
            [
                from,
                new ScenePoint(junctionX, from.Y),
                new ScenePoint(junctionX, to.Y),
                to
            ],
            color,
            stroke);

    /// <summary>The short horizontal rule a label sits on — the connector's final run.</summary>
    public static SceneConnector Tick(ScenePoint from, ScenePoint to, string color, double stroke) =>
        new(ConnectorKind.Elbow, [from, to], color, stroke);
}
```

- [ ] **Step 4: Write the normaliser**

`src/FamilyTree.Application/Export/SceneNormaliser.cs`:

```csharp
namespace FamilyTree.Application.Export;

/// <summary>
/// Pass 5's translation, shared by every layout strategy: shift the whole scene so it starts at
/// the margin and report its extent. Bounds are always origin-based, so page sizing is simply
/// the bounds' width and height.
/// </summary>
public static class SceneNormaliser
{
    public static TreeScene Normalise(
        IReadOnlyList<SceneNode> nodes,
        IReadOnlyList<SceneConnector> connectors,
        LayoutMetrics metrics)
    {
        if (nodes.Count == 0) return new TreeScene([], [], new SceneBounds(0, 0, 0, 0));

        var xs = nodes.SelectMany(n => new[] { n.X, n.X + n.Width })
            .Concat(connectors.SelectMany(c => c.Points.Select(p => p.X)))
            .ToList();
        var ys = nodes.SelectMany(n => new[] { n.Y - n.Height / 2, n.Y + n.Height / 2 })
            .Concat(connectors.SelectMany(c => c.Points.Select(p => p.Y)))
            .ToList();

        var dx = metrics.Margin - xs.Min();
        var dy = metrics.Margin - ys.Min();

        return new TreeScene(
            nodes.Select(n => n with { X = n.X + dx, Y = n.Y + dy }).ToList(),
            connectors.Select(c => c with
            {
                Points = c.Points.Select(p => new ScenePoint(p.X + dx, p.Y + dy)).ToList()
            }).ToList(),
            new SceneBounds(
                0, 0,
                xs.Max() + dx + metrics.Margin,
                ys.Max() + dy + metrics.Margin));
    }
}
```

- [ ] **Step 5: Write the strategy interface and implementation**

`src/FamilyTree.Application/Export/ILayoutStrategy.cs`:

```csharp
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

public interface ILayoutStrategy
{
    string Name { get; }

    TreeScene Build(
        IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutOptions options, MeasureText measure);
}
```

`src/FamilyTree.Application/Export/XmindLayoutStrategy.cs`:

```csharp
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

/// <summary>
/// Replicates the reference's mindmap vocabulary (design §4.3): a centre node with branches
/// balanced across both sides, tapered ribbons to the top level, orthogonal elbows below.
/// </summary>
public sealed class XmindLayoutStrategy : ILayoutStrategy
{
    public string Name => "xmind";

    public TreeScene Build(
        IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutOptions options, MeasureText measure)
    {
        if (roots.Count == 0) return new TreeScene([], [], new SceneBounds(0, 0, 0, 0));

        var metrics = options.Metrics;

        // One stored root becomes the centre. A forest gets a synthetic centre so every root
        // survives as its own coloured branch rather than being dropped. The synthetic node is
        // never emitted, only used to hold the branches.
        var isSynthetic = roots.Count > 1;
        var centreSource = isSynthetic
            ? new FamilyTreeNodeResponse(Guid.Empty, string.Empty, null, 0, false, roots)
            : roots[0];

        var centre = VerticalPacking.Pack([centreSource], metrics, measure).Single();
        if (isSynthetic) centre.Width = 0;

        var nodes = new List<SceneNode>();
        var connectors = new List<SceneConnector>();

        if (!isSynthetic) nodes.Add(ToScene(centre, options.Palette.CentreColor, metrics));

        var topLevel = centre.Children;
        if (topLevel.Count == 0)
            return SceneNormaliser.Normalise(nodes, connectors, metrics);

        var sides = SideAssignment.Assign(topLevel);
        PlaceSides(centre, topLevel, sides, metrics);

        foreach (var branch in topLevel)
        {
            var color = options.Palette.ColorAt(branch.BranchIndex);
            var direction = sides[branch] == Side.Right ? 1 : -1;

            foreach (var node in branch.Descend())
            {
                nodes.Add(ToScene(node, color, metrics));
                connectors.Add(ConnectorBuilder.Tick(
                    InnerEdge(node, direction), OuterEdge(node, direction),
                    color, metrics.ConnectorStroke));

                foreach (var child in node.Children)
                {
                    var from = OuterEdge(node, direction);
                    connectors.Add(ConnectorBuilder.Elbow(
                        from,
                        InnerEdge(child, direction),
                        junctionX: from.X + direction * metrics.ColumnGap / 2,
                        color,
                        metrics.ConnectorStroke));
                }
            }

            // A synthetic forest centre is invisible, so ribbons radiating from it would imply
            // a common ancestor that does not exist. Real roots get their ribbon; a forest
            // gets none.
            if (!isSynthetic)
                connectors.Add(ConnectorBuilder.Ribbon(
                    new ScenePoint(centre.X + (direction > 0 ? centre.Width : 0), centre.Y),
                    InnerEdge(branch, direction),
                    metrics.RibbonHalfWidth,
                    color));
        }

        return SceneNormaliser.Normalise(nodes, connectors, metrics);
    }

    /// <summary>
    /// Pass 5's placement. Each side's stack is centred on the centre node, which is what puts
    /// the centre at whatever fraction of the page balances the two masses (design §2.2).
    /// </summary>
    private static void PlaceSides(
        PackedNode centre, IReadOnlyList<PackedNode> topLevel,
        IReadOnlyDictionary<PackedNode, Side> sides, LayoutMetrics metrics)
    {
        centre.X = 0;
        centre.Y = 0;

        foreach (var side in new[] { Side.Right, Side.Left })
        {
            var onSide = topLevel.Where(n => sides[n] == side).ToList();
            if (onSide.Count == 0) continue;

            var total = onSide.Sum(n => n.Height) + (onSide.Count - 1) * metrics.SiblingGroupGap;
            var direction = side == Side.Right ? 1 : -1;
            var startX = direction > 0
                ? centre.Width + metrics.ColumnGap * 2
                : -metrics.ColumnGap * 2;

            var cursor = -total / 2;
            foreach (var branch in onSide)
            {
                branch.Shift(cursor - branch.Top);
                ColumnAssignment.Assign(branch, startX, direction, metrics);
                cursor = branch.Bottom + metrics.SiblingGroupGap;
            }
        }
    }

    private static SceneNode ToScene(PackedNode node, string color, LayoutMetrics metrics)
    {
        var fontSize = metrics.FontSizeForDepth(node.Depth);
        return new SceneNode(
            node.Source.Id,
            node.Source.Name,
            node.X,
            node.Y,
            node.Width,
            fontSize * 1.6,
            fontSize,
            color,
            metrics.ShapeForDepth(node.Depth));
    }

    private static ScenePoint OuterEdge(PackedNode node, int direction) =>
        new(direction > 0 ? node.X + node.Width : node.X, node.Y);

    private static ScenePoint InnerEdge(PackedNode node, int direction) =>
        new(direction > 0 ? node.X : node.X + node.Width, node.Y);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter XmindLayoutStrategyTests`
Expected: PASS — 10 tests.

For the forest case, the synthetic centre must not appear in `Nodes` — that is what `Should().NotContain(n => n.Label == string.Empty)` checks.

- [ ] **Step 7: Run the whole Application suite**

Run: `dotnet test tests/FamilyTree.Application.Tests`
Expected: PASS — no regression in the existing tests.

- [ ] **Step 8: Commit**

```bash
git add src/FamilyTree.Application/Export tests/FamilyTree.Application.Tests/Export
git commit -m "feat: assemble the xmind export layout into a positioned scene"
```

---

## Task 6: Overflow scaling and the too-large error

**Files:**
- Create: `src/FamilyTree.Application/Export/SceneScaler.cs`
- Modify: `src/FamilyTree.Domain/Common/DomainException.cs`
- Modify: `src/FamilyTree.Api/Errors/ExceptionHandler.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/SceneScalerTests.cs`

**Interfaces:**
- Consumes: `TreeScene`, `LayoutMetrics`.
- Produces: `SceneScaler.FitToSheet(TreeScene scene, LayoutMetrics metrics) -> TreeScene` (sets `Scale`); `TooLargeException(string code, string message, string reason)` with a `Reason` property.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/SceneScalerTests.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Domain.Common;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class SceneScalerTests
{
    private static readonly LayoutMetrics Metrics = new();

    private static TreeScene SceneOfHeight(double height) =>
        new(
            [new SceneNode(Guid.NewGuid(), "x", 0, height / 2, 10, 10, 13.34, "#000000", NodeShape.Tick)],
            [],
            new SceneBounds(0, 0, 100, height));

    [Fact]
    public void A_scene_inside_the_ceiling_is_returned_unscaled()
    {
        SceneScaler.FitToSheet(SceneOfHeight(3642), Metrics).Scale.Should().Be(1.0);
    }

    [Fact]
    public void A_scene_past_the_ceiling_is_scaled_to_fit_exactly()
    {
        var fitted = SceneScaler.FitToSheet(SceneOfHeight(Metrics.MaxPageExtent * 2), Metrics);

        fitted.Scale.Should().BeApproximately(0.5, 1e-9);
        (fitted.Bounds.Height * fitted.Scale).Should()
            .BeLessThanOrEqualTo(Metrics.MaxPageExtent + 1e-6);
    }

    // Design §4.4: emitting an illegible page is the one outcome explicitly ruled out.
    [Fact]
    public void A_scene_needing_a_font_below_the_floor_is_refused()
    {
        // Body text is 13.34pt and the floor is 6pt, so any scale under ~0.45 must refuse.
        var act = () => SceneScaler.FitToSheet(SceneOfHeight(Metrics.MaxPageExtent * 10), Metrics);

        act.Should().Throw<TooLargeException>()
            .Where(e => e.Code == "EXPORT_TREE_TOO_LARGE" && e.Reason == "sheet-overflow");
    }

    [Fact]
    public void Width_overflow_is_caught_as_well_as_height()
    {
        var scene = new TreeScene([], [], new SceneBounds(0, 0, Metrics.MaxPageExtent * 1.5, 100));

        SceneScaler.FitToSheet(scene, Metrics).Scale.Should().BeApproximately(1 / 1.5, 1e-9);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter SceneScalerTests`
Expected: FAIL — `SceneScaler` and `TooLargeException` do not exist.

- [ ] **Step 3: Add the exception type**

Append to `src/FamilyTree.Domain/Common/DomainException.cs`:

```csharp
/// <summary>
/// The request is well-formed but the result would exceed a hard limit. Carries a
/// <see cref="Reason"/> because only some causes have a remedy the caller can act on, and a
/// client must not offer the wrong one (design §5.3).
/// </summary>
public sealed class TooLargeException(string code, string message, string reason)
    : DomainException(code, message)
{
    public string Reason { get; } = reason;
}
```

- [ ] **Step 4: Map it in the exception handler**

In `src/FamilyTree.Api/Errors/ExceptionHandler.cs`, extend the status switch:

```csharp
        var (status, title) = domainException switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Request conflicts with the current state"),
            TooLargeException => (StatusCodes.Status413PayloadTooLarge, "Result exceeds a hard limit"),
            _ => (StatusCodes.Status400BadRequest, "Request violates a business rule")
        };
```

and immediately after the `problem` object is constructed, before the response is written:

```csharp
        // Only some causes have a remedy the caller can act on; the reason is how a client
        // knows whether to offer one (design §5.3).
        if (domainException is TooLargeException tooLarge)
            problem.Extensions["reason"] = tooLarge.Reason;
```

- [ ] **Step 5: Write the scaler**

`src/FamilyTree.Application/Export/SceneScaler.cs`:

```csharp
using FamilyTree.Domain.Common;

namespace FamilyTree.Application.Export;

/// <summary>
/// Design §4.4. The PDF format caps a page dimension at 14,400 units. Past that we scale the
/// whole scene uniformly rather than cropping it, and below the legibility floor we refuse
/// outright — an invalid or unreadable page is worse than an honest error.
/// </summary>
public static class SceneScaler
{
    public static TreeScene FitToSheet(TreeScene scene, LayoutMetrics metrics)
    {
        var longest = Math.Max(scene.Bounds.Width, scene.Bounds.Height);
        if (longest <= metrics.MaxPageExtent) return scene with { Scale = 1.0 };

        var scale = metrics.MaxPageExtent / longest;

        if (metrics.BodyFontSize * scale < metrics.MinFontSize)
            throw new TooLargeException(
                "EXPORT_TREE_TOO_LARGE",
                "This tree cannot fit a single sheet legibly. Export it as A4 pages instead.",
                "sheet-overflow");

        return scene with { Scale = scale };
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter SceneScalerTests`
Expected: PASS — 4 tests.

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build`
Expected: success with no warnings — warnings are errors here.

- [ ] **Step 8: Commit**

```bash
git add src/FamilyTree.Application/Export src/FamilyTree.Domain/Common src/FamilyTree.Api/Errors tests/FamilyTree.Application.Tests/Export
git commit -m "feat: scale oversized export scenes and refuse below the legibility floor"
```

---

## Task 7: Embedded fonts and the Skia text measurer

**Files:**
- Create: `src/FamilyTree.Infrastructure/Fonts/NotoSansArabic-Bold.ttf`, `NotoSans-Bold.ttf`, `OFL.txt`
- Create: `src/FamilyTree.Infrastructure/Export/EmbeddedFonts.cs`
- Create: `src/FamilyTree.Infrastructure/Export/SkiaTextMeasurer.cs`
- Modify: `src/FamilyTree.Infrastructure/FamilyTree.Infrastructure.csproj`
- Test: `tests/FamilyTree.Application.Tests/Export/SkiaTextMeasurerTests.cs`

**Interfaces:**
- Consumes: `MeasureText` (Task 1).
- Produces: `EmbeddedFonts.Arabic`, `EmbeddedFonts.Latin`, `EmbeddedFonts.For(string)` (all `SKTypeface`); `SkiaTextMeasurer.Measure(string, double) -> double` and `SkiaTextMeasurer.Delegate` (a `MeasureText`).

This task acquires third-party binaries and pins the SkiaSharp API surface. **Do not guess the shaping API — Step 4 discovers it.**

- [ ] **Step 1: Acquire the fonts**

```bash
mkdir -p src/FamilyTree.Infrastructure/Fonts
curl -L -o src/FamilyTree.Infrastructure/Fonts/NotoSansArabic-Bold.ttf \
  https://github.com/notofonts/notofonts.github.io/raw/main/fonts/NotoSansArabic/hinted/ttf/NotoSansArabic-Bold.ttf
curl -L -o src/FamilyTree.Infrastructure/Fonts/NotoSans-Bold.ttf \
  https://github.com/notofonts/notofonts.github.io/raw/main/fonts/NotoSans/hinted/ttf/NotoSans-Bold.ttf
curl -L -o src/FamilyTree.Infrastructure/Fonts/OFL.txt \
  https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/fonts/NotoSansArabic/OFL.txt
```

Verify they are real TrueType files and not HTML error pages — a failed download that silently
becomes a 200-byte HTML file is the classic failure here:

```bash
ls -l src/FamilyTree.Infrastructure/Fonts/
head -c 4 src/FamilyTree.Infrastructure/Fonts/NotoSansArabic-Bold.ttf | od -An -tx1
head -c 4 src/FamilyTree.Infrastructure/Fonts/NotoSans-Bold.ttf | od -An -tx1
```

Expected: each `.ttf` is ≥ 100 KB and starts with `00 01 00 00` (or `4f 54 54 4f` for OTF). If a
URL 404s, find the current path in the `notofonts/notofonts.github.io` repository rather than
substituting a different font — the metrics in the spec assume Noto.

- [ ] **Step 2: Add the packages and embed the fonts**

```bash
dotnet add src/FamilyTree.Infrastructure package SkiaSharp
dotnet add src/FamilyTree.Infrastructure package SkiaSharp.HarfBuzz
dotnet add src/FamilyTree.Infrastructure package SkiaSharp.NativeAssets.Linux
```

Add to `src/FamilyTree.Infrastructure/FamilyTree.Infrastructure.csproj`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Fonts\NotoSansArabic-Bold.ttf" />
    <EmbeddedResource Include="Fonts\NotoSans-Bold.ttf" />
  </ItemGroup>
```

Record the resolved versions — Task 10's Dockerfile must match:

```bash
grep -i skiasharp src/FamilyTree.Infrastructure/FamilyTree.Infrastructure.csproj
```

- [ ] **Step 3: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/SkiaTextMeasurerTests.cs`:

```csharp
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class SkiaTextMeasurerTests
{
    [Fact]
    public void The_embedded_arabic_typeface_loads()
    {
        EmbeddedFonts.Arabic.Should().NotBeNull();
        EmbeddedFonts.Arabic.FamilyName.Should().Contain("Noto");
    }

    [Fact]
    public void A_measured_label_has_positive_width()
    {
        SkiaTextMeasurer.Measure("سليمان", 13.34).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Width_scales_with_font_size()
    {
        var small = SkiaTextMeasurer.Measure("سليمان", 13.34);
        var large = SkiaTextMeasurer.Measure("سليمان", 26.68);

        large.Should().BeGreaterThan(small);
    }

    // Arabic is cursive: joined forms are narrower than the same letters separated. Without
    // shaping this comes out the other way round, which is exactly the bug this test catches.
    [Fact]
    public void Shaping_is_applied_so_a_joined_word_is_narrower_than_its_separated_letters()
    {
        var joined = SkiaTextMeasurer.Measure("سليمان", 13.34);
        var separated = SkiaTextMeasurer.Measure("س ل ي م ا ن", 13.34);

        joined.Should().BeLessThan(separated);
    }

    [Fact]
    public void Latin_text_measures_too()
    {
        SkiaTextMeasurer.Measure("Suleiman", 13.34).Should().BeGreaterThan(0);
    }
}
```

The test project already references Infrastructure — confirm rather than assume:

```bash
grep Infrastructure tests/FamilyTree.Application.Tests/FamilyTree.Application.Tests.csproj
```

- [ ] **Step 4: Run test to verify it fails, then pin the shaping API**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter SkiaTextMeasurerTests`
Expected: FAIL — `EmbeddedFonts` does not exist.

Before writing Step 5, confirm the actual `SKShaper` surface for the resolved package version.
The overloads differ across major versions and guessing produces a confidently wrong result:

- SkiaSharp **3.x**: `shaper.Shape(string text, SKFont font)` returning a result with `Width`; `canvas.DrawShapedText(SKShaper, string, float x, float y, SKFont, SKPaint)`.
- SkiaSharp **2.88**: `shaper.Shape(string text, SKPaint paint)`; `canvas.DrawShapedText(SKShaper, string, float x, float y, SKPaint)`.

Write Step 5 against whichever the build accepts. If the result type exposes no `Width`, sum the
advances from its `Points` instead. Verify by compiling, not by reading:

```bash
dotnet build src/FamilyTree.Infrastructure
```

- [ ] **Step 5: Write the font loader and measurer**

`src/FamilyTree.Infrastructure/Export/EmbeddedFonts.cs`:

```csharp
using System.Reflection;
using SkiaSharp;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// The reference used Arial Bold and Open Sans Bold; neither ships (Arial is proprietary, Open
/// Sans has no Arabic coverage). Noto is SIL OFL and metrically close, so the reference's
/// column and row proportions survive (design §3.3).
///
/// Typefaces load once and are shared: they are immutable and thread-safe, and reloading per
/// request would re-parse the font on every export.
/// </summary>
public static class EmbeddedFonts
{
    private static readonly Lazy<SKTypeface> ArabicFont =
        new(() => Load("NotoSansArabic-Bold.ttf"), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<SKTypeface> LatinFont =
        new(() => Load("NotoSans-Bold.ttf"), LazyThreadSafetyMode.ExecutionAndPublication);

    public static SKTypeface Arabic => ArabicFont.Value;
    public static SKTypeface Latin => LatinFont.Value;

    /// <summary>Arabic covers the names; Latin appears only in Latin captions.</summary>
    public static SKTypeface For(string text) =>
        text.Any(IsArabic) ? Arabic : Latin;

    // U+0600–U+06FF Arabic, U+0750–U+077F Supplement, U+FB50–U+FDFF and U+FE70–U+FEFF forms.
    private static bool IsArabic(char c) =>
        c is >= '؀' and <= 'ۿ'
            or >= 'ݐ' and <= 'ݿ'
            or >= 'ﭐ' and <= '﷿'
            or >= 'ﹰ' and <= '﻿';

    private static SKTypeface Load(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded font '{fileName}' is missing. Check the EmbeddedResource item in the csproj.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException($"'{fileName}' is not a usable typeface.");
    }
}
```

`src/FamilyTree.Infrastructure/Export/SkiaTextMeasurer.cs` (SkiaSharp 3.x form — adjust per Step 4):

```csharp
using FamilyTree.Application.Export;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// Supplies the <see cref="MeasureText"/> delegate the layout engine consumes (design §4.2).
/// Widths must come from *shaped* text: Arabic is cursive, so joined forms are narrower than
/// the isolated glyphs, and measuring unshaped would size every column too wide.
/// </summary>
public static class SkiaTextMeasurer
{
    public static MeasureText Delegate { get; } = Measure;

    public static double Measure(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var typeface = EmbeddedFonts.For(text);
        using var font = new SKFont(typeface, (float)fontSize);
        using var shaper = new SKShaper(typeface);

        return shaper.Shape(text, font).Width;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter SkiaTextMeasurerTests`
Expected: PASS — 5 tests.

If `Shaping_is_applied...` fails, measurement is not going through HarfBuzz. Do not relax the
test — it is the only guard that the Arabic pipeline works at all.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Infrastructure tests/FamilyTree.Application.Tests/Export
git commit -m "feat: embed the Noto typefaces and measure shaped Arabic text"
```

---

## Task 8: The PDF renderer and the searchability gate

**Files:**
- Create: `src/FamilyTree.Infrastructure/Export/SkiaTreeRenderer.cs`
- Create: `src/FamilyTree.Infrastructure/Export/SheetPaginator.cs`
- Create: `src/FamilyTree.Infrastructure/Export/A4Paginator.cs` (stub; Task 13 replaces it)
- Create: `src/FamilyTree.Infrastructure/Export/TreeRendererAdapter.cs`
- Create: `src/FamilyTree.Application/Export/IFamilyTreeExporter.cs`
- Create: `src/FamilyTree.Application/Export/FamilyTreeExportService.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/PdfText.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/SkiaTreeRendererTests.cs`

**Interfaces:**
- Consumes: `TreeScene`, `SceneScaler`, `EmbeddedFonts`, `SkiaTextMeasurer`, `IFamilyTreeService`.
- Produces: `enum ExportPageFormat { Sheet, A4 }`, `enum ExportStyle { Xmind, Clean }`, `readonly record struct PageWindow(float Width, float Height, float OffsetX, float OffsetY)`, `ITreeRenderer`, `ITreeRendererAdapter`, `IFamilyTreeExporter`, `ExportResult(byte[] Content, string FamilyTreeName)`.

The **unconditional gate** is text recovery: a PDF whose names cannot be extracted is a regression against the reference, which was searchable.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/PdfText.cs`:

```csharp
using System.Diagnostics;

namespace FamilyTree.Application.Tests.Export;

/// <summary>Extracts a PDF's text layer with poppler's pdftotext.</summary>
public static class PdfText
{
    public static string Extract(string pdfPath)
    {
        var output = Path.ChangeExtension(pdfPath, ".txt");

        using var process = Process.Start(new ProcessStartInfo("pdftotext", $"\"{pdfPath}\" \"{output}\"")
        {
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("pdftotext is not installed");

        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(process.StandardError.ReadToEnd());

        try { return File.ReadAllText(output); }
        finally { if (File.Exists(output)) File.Delete(output); }
    }
}
```

`tests/FamilyTree.Application.Tests/Export/SkiaTreeRendererTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter SkiaTreeRendererTests`
Expected: FAIL — `SkiaTreeRenderer` does not exist.

- [ ] **Step 3: Write the paginators**

`src/FamilyTree.Infrastructure/Export/SheetPaginator.cs`:

```csharp
using FamilyTree.Application.Export;

namespace FamilyTree.Infrastructure.Export;

/// <param name="OffsetX">Scene-space origin of this page, used for tiling.</param>
public readonly record struct PageWindow(float Width, float Height, float OffsetX, float OffsetY);

/// <summary>One page, sized to the whole scene (design §4.5).</summary>
public static class SheetPaginator
{
    public static IEnumerable<PageWindow> Pages(TreeScene scene)
    {
        yield return new PageWindow(
            (float)(scene.Bounds.Width * scene.Scale),
            (float)(scene.Bounds.Height * scene.Scale),
            0,
            0);
    }
}
```

`src/FamilyTree.Infrastructure/Export/A4Paginator.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Domain.Common;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// Tiling across A4 pages, implemented in Task 13. Until then the format is refused explicitly
/// rather than silently falling back to a single sheet, which would hand the user a page their
/// printer cannot take while reporting success.
/// </summary>
public static class A4Paginator
{
    public static IEnumerable<PageWindow> Pages(TreeScene scene) =>
        throw new TooLargeException(
            "EXPORT_TREE_TOO_LARGE",
            "A4 pagination is not available yet. Export a single sheet instead.",
            "format-unavailable");
}
```

- [ ] **Step 4: Write the renderer**

`src/FamilyTree.Infrastructure/Export/SkiaTreeRenderer.cs`:

```csharp
using FamilyTree.Application.Export;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FamilyTree.Infrastructure.Export;

public enum ExportPageFormat { Sheet, A4 }

public interface ITreeRenderer
{
    byte[] Render(TreeScene scene, ExportPageFormat format);
}

/// <summary>
/// Draws a <see cref="TreeScene"/> into a PDF (design §4.1). Makes no layout decisions: every
/// coordinate arrives already computed, which is what keeps the geometry unit-testable without
/// a font or a native binary.
/// </summary>
public sealed class SkiaTreeRenderer : ITreeRenderer
{
    private const float CornerRadius = 6f;

    public byte[] Render(TreeScene scene, ExportPageFormat format)
    {
        using var stream = new MemoryStream();

        using (var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
        {
            Creator = "Family Tree",
            Title = "Family Tree"
        }))
        {
            foreach (var page in Paginate(scene, format))
            {
                var canvas = document.BeginPage(page.Width, page.Height);
                canvas.Clear(SKColors.White);
                canvas.Translate(-page.OffsetX, -page.OffsetY);
                canvas.Scale((float)scene.Scale);

                foreach (var connector in scene.Connectors) DrawConnector(canvas, connector);
                foreach (var node in scene.Nodes) DrawNode(canvas, node);

                document.EndPage();
            }

            document.Close();
        }

        return stream.ToArray();
    }

    private static IEnumerable<PageWindow> Paginate(TreeScene scene, ExportPageFormat format) =>
        format switch
        {
            ExportPageFormat.Sheet => SheetPaginator.Pages(scene),
            ExportPageFormat.A4 => A4Paginator.Pages(scene),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    private static void DrawConnector(SKCanvas canvas, SceneConnector connector)
    {
        var isRibbon = connector.Kind == ConnectorKind.Ribbon;

        using var paint = new SKPaint
        {
            Color = SKColor.Parse(connector.Color),
            IsAntialias = true,
            Style = isRibbon ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            StrokeWidth = (float)connector.StrokeWidth,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        using var path = isRibbon ? RibbonPath(connector) : ElbowPath(connector);
        canvas.DrawPath(path, paint);
    }

    /// <summary>Eight points: start edge, two controls, tip — then back along the mirror.</summary>
    private static SKPath RibbonPath(SceneConnector connector)
    {
        var p = connector.Points;
        var path = new SKPath();

        path.MoveTo(F(p[0]));
        path.CubicTo(F(p[1]), F(p[2]), F(p[3]));
        path.LineTo(F(p[4]));
        path.CubicTo(F(p[5]), F(p[6]), F(p[7]));
        path.Close();

        return path;
    }

    /// <summary>Orthogonal polyline, rounded at each interior vertex.</summary>
    private static SKPath ElbowPath(SceneConnector connector)
    {
        var p = connector.Points;
        var path = new SKPath();

        path.MoveTo(F(p[0]));
        for (var i = 1; i < p.Count - 1; i++)
            path.ArcTo(F(p[i]), F(p[i + 1]), CornerRadius);
        path.LineTo(F(p[^1]));

        return path;
    }

    private static void DrawNode(SKCanvas canvas, SceneNode node)
    {
        if (node.Shape == NodeShape.RoundedBox) DrawBox(canvas, node);
        DrawLabel(canvas, node);
    }

    private static void DrawBox(SKCanvas canvas, SceneNode node)
    {
        var rect = new SKRect(
            (float)node.X, (float)(node.Y - node.Height / 2),
            (float)(node.X + node.Width), (float)(node.Y + node.Height / 2));

        using var fill = new SKPaint
        {
            Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true
        };
        using var stroke = new SKPaint
        {
            Color = SKColor.Parse(node.Color),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.48f,
            IsAntialias = true
        };

        canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, fill);
        canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, stroke);
    }

    private static void DrawLabel(SKCanvas canvas, SceneNode node)
    {
        var typeface = EmbeddedFonts.For(node.Label);
        using var font = new SKFont(typeface, (float)node.FontSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var shaper = new SKShaper(typeface);

        // Shaped drawing is what joins Arabic correctly AND what makes Skia emit the
        // /ToUnicode CMap the searchability test depends on. Do not swap this for DrawText.
        var baseline = (float)(node.Y + node.FontSize * 0.35);
        canvas.DrawShapedText(shaper, node.Label, (float)node.X, baseline, font, paint);
    }

    private static SKPoint F(ScenePoint p) => new((float)p.X, (float)p.Y);
}
```

- [ ] **Step 5: Write the application service and adapter**

`src/FamilyTree.Application/Export/IFamilyTreeExporter.cs`:

```csharp
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

public enum ExportStyle { Xmind, Clean }

/// <returns>The rendered PDF, and the family tree name used for the download filename.</returns>
public sealed record ExportResult(byte[] Content, string FamilyTreeName);

public interface IFamilyTreeExporter
{
    Task<ExportResult> ExportAsync(
        Guid? rootId, int? maxDepth, ExportStyle style, string pageFormat, CancellationToken ct);
}

/// <summary>
/// The Infrastructure seam. Application defines the shape; Infrastructure owns SkiaSharp, which
/// is what keeps the SkiaSharp package out of this project (design §4.2).
/// </summary>
public interface ITreeRendererAdapter
{
    byte[] Render(IReadOnlyList<FamilyTreeNodeResponse> roots, ExportStyle style, string pageFormat);
}
```

`src/FamilyTree.Application/Export/FamilyTreeExportService.cs`:

```csharp
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.Common;

namespace FamilyTree.Application.Export;

/// <summary>
/// Turns a tree into PDF bytes. Rendering is CPU-bound, so concurrency is bounded here rather
/// than left to the thread pool: one tenant's repeated exports must not starve everyone else's
/// requests (design §5.2).
/// </summary>
public sealed class FamilyTreeExportService(
    IFamilyTreeService trees, ITreeRendererAdapter renderer) : IFamilyTreeExporter
{
    private const int MemberCap = 10_000;

    // Process-wide, not per-request: the limit exists to cap total CPU, so a per-instance
    // semaphore would not bound anything.
    private static readonly SemaphoreSlim RenderSlots = new(2, 2);

    public async Task<ExportResult> ExportAsync(
        Guid? rootId, int? maxDepth, ExportStyle style, string pageFormat, CancellationToken ct)
    {
        var view = await trees.GetViewAsync(rootId, maxDepth, ct);

        // Assemble returns an empty list for an unknown id; the tenant-safe response is the
        // same 404 an unknown member gets anywhere else (design §5.3).
        if (rootId is not null && view.RootMembers.Count == 0)
            throw new NotFoundException("MEMBER_NOT_FOUND", "No such member for this tenant.");

        var count = view.RootMembers.Sum(Count);
        if (count > MemberCap)
            throw new TooLargeException(
                "EXPORT_TREE_TOO_LARGE",
                $"This tree has {count} members, above the {MemberCap} export limit.",
                "member-cap");

        await RenderSlots.WaitAsync(ct);
        try
        {
            return new ExportResult(renderer.Render(view.RootMembers, style, pageFormat), view.Name);
        }
        finally
        {
            RenderSlots.Release();
        }
    }

    private static int Count(FamilyTreeNodeResponse node) => 1 + node.Children.Sum(Count);
}
```

`src/FamilyTree.Infrastructure/Export/TreeRendererAdapter.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Infrastructure.Export;

public sealed class TreeRendererAdapter : ITreeRendererAdapter
{
    private static readonly ILayoutStrategy Xmind = new XmindLayoutStrategy();

    public byte[] Render(
        IReadOnlyList<FamilyTreeNodeResponse> roots, ExportStyle style, string pageFormat)
    {
        var options = LayoutOptions.Default;

        // Task 14 adds the clean strategy; until then both styles share this geometry.
        var strategy = Xmind;

        var scene = strategy.Build(roots, options, SkiaTextMeasurer.Delegate);
        var format = pageFormat == "a4" ? ExportPageFormat.A4 : ExportPageFormat.Sheet;

        var fitted = format == ExportPageFormat.Sheet
            ? SceneScaler.FitToSheet(scene, options.Metrics)
            : scene;

        return new SkiaTreeRenderer().Render(fitted, format);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter SkiaTreeRendererTests`
Expected: PASS — 3 tests.

If `Every_name_is_recoverable_from_the_rendered_pdf` reports missing names, labels are being
drawn as paths rather than glyph runs. Confirm `DrawShapedText` is used and that no
`SKPathEffect` is set on the text paint.

If it fails because `pdftotext` is absent, install poppler locally
(`choco install poppler` / `apt-get install poppler-utils`) — this gate is not optional.

- [ ] **Step 7: Ensure CI has poppler**

```bash
grep -n "runs-on\|dotnet test\|apt-get" .github/workflows/*.yml
```

Add to the job that runs `dotnet test`, before that step:

```yaml
      - name: Install poppler for PDF text extraction
        run: sudo apt-get update && sudo apt-get install -y poppler-utils
```

- [ ] **Step 8: Commit**

```bash
git add src/FamilyTree.Infrastructure src/FamilyTree.Application/Export tests/FamilyTree.Application.Tests/Export .github
git commit -m "feat: render the export scene to a searchable PDF"
```

---

## Task 9: The export endpoint

**Files:**
- Modify: `src/FamilyTree.Api/Endpoints/FamilyTrees/FamilyTreeEndpoints.cs`
- Modify: `src/FamilyTree.Api/Program.cs`
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyTreeExportTests.cs`

**Interfaces:**
- Consumes: `IFamilyTreeExporter`, `ExportResult`, `ExportStyle`, `Permissions.FamilyTree.View`, `TreeRendererAdapter`.
- Produces: `GET /api/v1/family-tree/export.pdf`.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Api.IntegrationTests/Endpoints/FamilyTreeExportTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class FamilyTreeExportTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string ExportPath = "/api/v1/family-tree/export.pdf";

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    [Fact]
    public async Task Exporting_without_authentication_is_rejected()
    {
        (await _client.GetAsync(ExportPath)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Exporting_the_seeded_tree_returns_a_pdf()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync(ExportPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task The_download_is_an_attachment_with_a_filename()
    {
        await AuthenticateAsync();

        var disposition = (await _client.GetAsync(ExportPath)).Content.Headers.ContentDisposition!;

        disposition.DispositionType.Should().Be("attachment");
        // Arabic must travel percent-encoded in filename*, never raw in filename.
        (disposition.FileNameStar ?? disposition.FileName).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_unknown_root_id_is_not_found()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync($"{ExportPath}?rootId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("MEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task A_subtree_export_is_smaller_than_the_whole_tree()
    {
        await AuthenticateAsync();

        var whole = await (await _client.GetAsync(ExportPath)).Content.ReadAsByteArrayAsync();

        var members = await _client.GetFromJsonAsync<FamilyMemberResponse[]>("/api/v1/family-members");
        var child = members!.First(m => m.ParentId is not null);

        var subtree = await (await _client.GetAsync($"{ExportPath}?rootId={child.Id}"))
            .Content.ReadAsByteArrayAsync();

        subtree.Length.Should().BeLessThan(whole.Length);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FamilyTreeExportTests`
Expected: FAIL — 404, the route does not exist. Docker must be running.

- [ ] **Step 3: Register the services**

In `src/FamilyTree.Api/Program.cs`, alongside the existing registrations:

```csharp
builder.Services.AddScoped<ITreeRendererAdapter, TreeRendererAdapter>();
builder.Services.AddScoped<IFamilyTreeExporter, FamilyTreeExportService>();
```

with `using FamilyTree.Application.Export;` and `using FamilyTree.Infrastructure.Export;`.

- [ ] **Step 4: Add the endpoint**

In `src/FamilyTree.Api/Endpoints/FamilyTrees/FamilyTreeEndpoints.cs`, before `return app;`:

```csharp
        // Guarded by FamilyTree.View, not a new permission: the export reveals exactly the data
        // /view already returns, so a separate code would add a lockout surface the
        // last-administrator guard has to reason about, without adding protection (design §5.1).
        group.MapGet("/export.pdf", async (
            Guid? rootId,
            int? maxDepth,
            string? style,
            string? page,
            IFamilyTreeExporter exporter,
            CancellationToken ct) =>
        {
            var chosenStyle = string.Equals(style, "clean", StringComparison.OrdinalIgnoreCase)
                ? ExportStyle.Clean
                : ExportStyle.Xmind;

            var format = string.Equals(page, "a4", StringComparison.OrdinalIgnoreCase)
                ? "a4"
                : "sheet";

            var result = await exporter.ExportAsync(rootId, maxDepth, chosenStyle, format, ct);

            // Results.File percent-encodes a non-ASCII download name into filename* per
            // RFC 5987, which is what lets an Arabic family name survive the header.
            return Results.File(
                result.Content,
                contentType: "application/pdf",
                fileDownloadName: $"{result.FamilyTreeName}.pdf");
        })
            .RequirePermission(Permissions.FamilyTree.View);
```

Add `using FamilyTree.Application.Export;` to the file's usings.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter FamilyTreeExportTests`
Expected: PASS — 5 tests.

- [ ] **Step 6: Add the permission case to the existing authorization suite**

Read `tests/FamilyTree.Api.IntegrationTests/Endpoints/AuthorizationTests.cs` and add a case in
its existing style asserting that a user *without* `FamilyTree.View` receives 403 from
`/api/v1/family-tree/export.pdf`. Mirror the surrounding cases' construction exactly rather
than inventing a new fixture.

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter AuthorizationTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Api tests/FamilyTree.Api.IntegrationTests
git commit -m "feat: add the family tree PDF export endpoint"
```

---

## Task 10: Container native assets

**Files:**
- Modify: `src/FamilyTree.Api/Dockerfile`

SkiaSharp needs native libraries the ASP.NET runtime image does not carry. This passes every local test and then fails in the container, so it is verified in a running image.

- [ ] **Step 1: Get a token and prove the current behaviour**

```bash
docker compose up -d postgres
docker compose build api && docker compose up -d api
sleep 5

TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$SEED_ADMIN_EMAIL\",\"password\":\"$SEED_ADMIN_PASSWORD\"}" \
  | python -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")

curl -s -o /tmp/probe.pdf -w "%{http_code}\n" \
  -H "Authorization: Bearer $TOKEN" \
  http://localhost:5000/api/v1/family-tree/export.pdf
docker compose logs api | tail -20
```

Expected: `500`, with the logs showing `DllNotFoundException` for `libSkiaSharp` or a fontconfig
error. **If it already returns 200 and `/tmp/probe.pdf` starts with `%PDF-`, the base image
already carries the dependencies — record that in the commit message and skip to Step 4.**

- [ ] **Step 2: Add the native dependencies**

In `src/FamilyTree.Api/Dockerfile`, in the runtime stage **before** `USER $APP_UID`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SkiaSharp draws the PDF export through a native library the aspnet image does not ship.
# libfontconfig1 and libfreetype6 are what libSkiaSharp links against for text; without them
# the export endpoint throws DllNotFoundException at first use, long after startup succeeded.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
```

- [ ] **Step 3: Rebuild and verify in the running container**

```bash
docker compose build api && docker compose up -d api
sleep 5
curl -s -o /tmp/probe.pdf -w "%{http_code}\n" \
  -H "Authorization: Bearer $TOKEN" \
  http://localhost:5000/api/v1/family-tree/export.pdf
head -c 5 /tmp/probe.pdf; echo
```

Expected: `200` and `%PDF-`.

- [ ] **Step 4: Verify Arabic actually rendered rather than tofu**

```bash
pdftotext /tmp/probe.pdf - | head -20
```

Expected: Arabic names. Empty output means the fonts did not embed — check the
`EmbeddedResource` items from Task 7 survived into the published output.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyTree.Api/Dockerfile
git commit -m "fix: install the native libraries SkiaSharp needs in the API image"
```

---

## Task 11: Frontend export control

**Files:**
- Modify: `frontend/src/services/apiClient.ts`
- Create: `frontend/src/features/tree/exportApi.ts`
- Create: `frontend/src/features/tree/ExportDialog.tsx`
- Modify: `frontend/src/features/tree/TreePage.tsx`
- Modify: `frontend/src/i18n/locales/ar.json`, `frontend/src/i18n/locales/en.json`
- Test: `frontend/src/features/tree/exportApi.test.ts`

**Interfaces:**
- Consumes: `GET /api/v1/family-tree/export.pdf`.
- Produces: `apiFetchBlob(path: string, init?: RequestInit): Promise<Blob>`; `downloadTreePdf(options: ExportOptions, fileName: string): Promise<void>`; `type ExportStyle = 'xmind' | 'clean'`; `type ExportPage = 'sheet' | 'a4'`.

`apiFetch` currently forces `Content-Type: application/json` and parses JSON. The refresh-and-replay logic must be **shared, not duplicated** — a second copy would drift the moment either is touched.

- [ ] **Step 1: Write the failing test**

`frontend/src/features/tree/exportApi.test.ts`:

```ts
import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiFetchBlob } from '../../services/apiClient'
import { tokenStorage } from '../../services/tokenStorage'

describe('apiFetchBlob', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    tokenStorage.clear()
  })

  it('returns the response body as a blob', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(new Blob(['%PDF-1.4']), {
        status: 200,
        headers: { 'Content-Type': 'application/pdf' },
      }),
    )

    const blob = await apiFetchBlob('/api/v1/family-tree/export.pdf')

    expect(blob.size).toBeGreaterThan(0)
  })

  it('does not force a JSON content type on the request', async () => {
    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(new Blob(['%PDF-1.4']), { status: 200 }))

    await apiFetchBlob('/api/v1/family-tree/export.pdf')

    const init = fetchSpy.mock.calls[0][1] as RequestInit
    expect(new Headers(init.headers).get('Content-Type')).toBeNull()
  })

  it('surfaces a coded ApiError when the export is refused', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ code: 'EXPORT_TREE_TOO_LARGE', reason: 'sheet-overflow' }), {
        status: 413,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    )

    await expect(apiFetchBlob('/api/v1/family-tree/export.pdf')).rejects.toMatchObject({
      code: 'EXPORT_TREE_TOO_LARGE',
      status: 413,
    })
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/features/tree/exportApi.test.ts`
Expected: FAIL — `apiFetchBlob` is not exported.

- [ ] **Step 3: Extract the shared core and add the blob path**

In `frontend/src/services/apiClient.ts`, make the JSON content type opt-in and split the request
core out of `apiFetch`. Replace `withAuth` and the existing `apiFetch` with:

```ts
const withAuth = (init: RequestInit, accessToken?: string, json = true): RequestInit => {
  const headers = new Headers(init.headers)
  if (json) headers.set('Content-Type', 'application/json')
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  return { ...init, headers }
}

/**
 * The shared request core: attempt, refresh once on 401, replay. Both apiFetch and
 * apiFetchBlob route through this so the refresh rules live in exactly one place — a second
 * copy would drift the moment either is touched.
 */
const request = async (path: string, init: RequestInit, json: boolean): Promise<Response> => {
  const attempt = async (): Promise<Response> =>
    fetch(path, withAuth(init, tokenStorage.read()?.accessToken, json))

  let response = await attempt()

  if (response.status === 401 && path !== REFRESH_PATH && path !== LOGIN_PATH) {
    if (await tryRefresh()) {
      response = await attempt()
    }
  }

  if (!response.ok) throw await errorFrom(response)
  return response
}

export const apiFetch = async <T>(path: string, init: RequestInit = {}): Promise<T> => {
  const response = await request(path, init, true)

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

/** Binary responses (the PDF export): no JSON content type, no body parsing. */
export const apiFetchBlob = async (path: string, init: RequestInit = {}): Promise<Blob> => {
  const response = await request(path, init, false)
  return await response.blob()
}
```

Delete the old `apiFetch` body that this replaces — do not leave both.

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/tree/exportApi.test.ts src/services/apiClient.test.ts`
Expected: PASS — the new tests plus the existing `apiClient` suite, unchanged.

- [ ] **Step 5: Add the download helper**

`frontend/src/features/tree/exportApi.ts`:

```ts
import { apiFetchBlob } from '../../services/apiClient'

export type ExportStyle = 'xmind' | 'clean'
export type ExportPage = 'sheet' | 'a4'

export interface ExportOptions {
  rootId?: string
  style: ExportStyle
  page: ExportPage
}

/**
 * Fetches the PDF and hands it to the browser as a download. The object URL is revoked
 * immediately after the click: leaking it pins the whole blob in memory for the tab's
 * lifetime, and these documents are large.
 */
export const downloadTreePdf = async (
  options: ExportOptions,
  fileName: string,
): Promise<void> => {
  const query = new URLSearchParams({ style: options.style, page: options.page })
  if (options.rootId) query.set('rootId', options.rootId)

  const blob = await apiFetchBlob(`/api/v1/family-tree/export.pdf?${query.toString()}`)
  const url = URL.createObjectURL(blob)

  try {
    const link = document.createElement('a')
    link.href = url
    link.download = fileName
    document.body.appendChild(link)
    link.click()
    link.remove()
  } finally {
    URL.revokeObjectURL(url)
  }
}
```

- [ ] **Step 6: Add the translations**

Add an `export` object to the existing `tree` section of **both** locale files. `locales.test.ts`
asserts the two locales carry identical key sets, so a key missing from either fails the suite.

`frontend/src/i18n/locales/ar.json`:

```json
"export": {
  "button": "تصدير PDF",
  "title": "تصدير شجرة العائلة",
  "style": "النمط",
  "styleXmind": "خريطة ذهنية",
  "styleClean": "تصميم واضح",
  "page": "الصفحة",
  "pageSheet": "صفحة واحدة",
  "pageA4": "صفحات A4",
  "confirm": "تصدير",
  "cancel": "إلغاء",
  "busy": "جارٍ التصدير…",
  "failed": "تعذّر التصدير"
}
```

`frontend/src/i18n/locales/en.json`:

```json
"export": {
  "button": "Export PDF",
  "title": "Export family tree",
  "style": "Style",
  "styleXmind": "Mind map",
  "styleClean": "Clean design",
  "page": "Page",
  "pageSheet": "Single sheet",
  "pageA4": "A4 pages",
  "confirm": "Export",
  "cancel": "Cancel",
  "busy": "Exporting…",
  "failed": "Export failed"
}
```

Run: `cd frontend && npx vitest run src/i18n/locales.test.ts`
Expected: PASS.

- [ ] **Step 7: Add the dialog and wire it into TreePage**

Read `frontend/src/features/tree/TreePage.tsx` and `MemberPanel.tsx` first and match their
existing conventions — inline style objects, `var(--surface)` / `var(--divider)` tokens,
`useTranslation`, and the `direction` prop threading.

Create `frontend/src/features/tree/ExportDialog.tsx` with two radio groups — style
(`xmind` / `clean`) and page (`sheet` / `a4`) — plus Cancel and Export buttons that call
`downloadTreePdf`. While the request is in flight show `tree.export.busy` and disable the
buttons; on an `ApiError` show `tree.export.failed`. Add a button to `TreePage`'s existing
toolbar, labelled `tree.export.button`, that opens it.

- [ ] **Step 8: Run the full frontend suite**

Run: `cd frontend && npm test`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add frontend/src
git commit -m "feat: add the PDF export control to the tree screen"
```

---

## Task 12: Generalise the import classifier and round-trip the export

**Files:**
- Modify: `tools/FamilyTree.Import/Geometry.cs`
- Modify: `tests/FamilyTree.Import.Tests/FamilyTree.Import.Tests.csproj`
- Test: `tests/FamilyTree.Import.Tests/SkiaExportRoundTripTests.cs`

`Geometry.Classify` matches path operator signatures tuned to XMind's emission — `"ll"`/`h` ticks, `"lclclclc"` rounded rects, `"llcl"`/`"lc"`/`"lclcl"` connectors. Skia constructs paths differently, so the first run will fail in **classification**, not because the export is wrong. The reference fixture's existing assertions must keep passing afterwards.

- [ ] **Step 1: Read the existing pipeline and add references**

```bash
sed -n '1,60p' tools/FamilyTree.Import/Program.cs
grep -n "public static" tools/FamilyTree.Import/Reconstruct.cs tools/FamilyTree.Import/PdfStreams.cs
dotnet add tests/FamilyTree.Import.Tests reference src/FamilyTree.Application src/FamilyTree.Infrastructure
```

If the reconstruction pipeline is only reachable from `Program`, extract a
`Reconstruct.FromPdf(string path)` seam as part of this task rather than duplicating the wiring
inside the test. Use the actual entry-point names you find in the test below.

- [ ] **Step 2: Write the failing round-trip test**

`tests/FamilyTree.Import.Tests/SkiaExportRoundTripTests.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Import.Tests;

/// <summary>
/// The flagship acceptance test (design §7.2): our own reconstruction engine, pointed at our
/// own export, must recover the same hierarchy. It validates geometry, glyph encoding,
/// connector direction, and searchability at once.
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

    [Fact]
    public void Every_exported_member_is_classified_as_a_node()
    {
        var path = RenderToFile();
        try
        {
            var classified = Geometry.Classify(PdfStreams.ReadFirstPage(path));

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
            var reconstruction = Reconstruct.FromPdf(path);

            reconstruction.Members.Should().HaveCount(7);
            reconstruction.Members.Where(m => m.ParentId is null).Should().ContainSingle()
                .Which.Name.Should().Be("سليمان");
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 3: Run test to verify it fails, and record what Skia emits**

Run: `dotnet test tests/FamilyTree.Import.Tests --filter SkiaExportRoundTripTests`
Expected: FAIL — the box count is wrong, most likely 0, because Skia's operator signatures do
not match the XMind patterns.

Before changing the classifier, capture the real signatures. Add a temporary assertion in
`Every_exported_member_is_classified_as_a_node` that prints them, run it, note the output, then
remove the diagnostic:

```csharp
            var page = PdfStreams.ReadFirstPage(path);
            var signatures = page.Paths
                .GroupBy(p => (p.Ops, p.Terminator))
                .Select(g => $"{g.Key.Ops} end={g.Key.Terminator}: {g.Count()}");
            throw new Exception(string.Join(" | ", signatures));
```

- [ ] **Step 4: Generalise the classifier**

In `tools/FamilyTree.Import/Geometry.cs`, replace `Classify`'s exact-signature matching with
shape-based predicates that accept both emitters, keeping the XMind behaviour intact:

- A **tick** is any open path whose points are collinear and horizontal within a small epsilon — regardless of whether its ops are `"ll"` or Skia's equivalent.
- A **rounded rect** is any closed path whose points touch all four corners of its own bounding box with curve segments between, detected by that coverage rather than by the literal `"lclclclc"` string.
- A **connector** is any remaining open path with at least two distinct points.

Document the widening at the top of `Classify` with the same care the existing comments show:
name both emitters, and state that the reference fixture's counts must not move.

- [ ] **Step 5: Verify both fixtures**

Run: `dotnet test tests/FamilyTree.Import.Tests`
Expected: PASS — **including** the pre-existing tests over `familytree.pdf`. If the reference's
349-member reconstruction or its box and connector counts changed, the widening is too loose.
Narrow it until both fixtures pass; the reference is the regression guard.

- [ ] **Step 6: Commit**

```bash
git add tools/FamilyTree.Import tests/FamilyTree.Import.Tests
git commit -m "feat: classify export paths by shape so Skia output round-trips like XMind's"
```

---

## Task 13: A4 pagination

**Files:**
- Modify: `src/FamilyTree.Infrastructure/Export/A4Paginator.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/A4PaginatorTests.cs`

**Interfaces:**
- Consumes: `TreeScene`, `PageWindow`.
- Produces: `A4Paginator.Pages(TreeScene scene) -> IEnumerable<PageWindow>` — real tiling, replacing the Task 8 stub.

A4 is 595 × 842 pt with an 18 pt bleed on each cut.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/A4PaginatorTests.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class A4PaginatorTests
{
    private static TreeScene Scene(double width, double height) =>
        new([], [], new SceneBounds(0, 0, width, height));

    [Fact]
    public void A_scene_smaller_than_one_page_produces_one_page()
    {
        A4Paginator.Pages(Scene(400, 600)).Should().ContainSingle();
    }

    [Fact]
    public void A_tall_scene_is_tiled_down_the_page()
    {
        var pages = A4Paginator.Pages(Scene(400, 2400)).ToList();

        pages.Count.Should().BeGreaterThan(2);
        pages.Should().OnlyContain(p => p.Width <= 595 && p.Height <= 842);
    }

    [Fact]
    public void A_wide_and_tall_scene_is_tiled_in_both_directions()
    {
        var pages = A4Paginator.Pages(Scene(1400, 2000)).ToList();

        pages.Select(p => p.OffsetX).Distinct().Count().Should().BeGreaterThan(1);
        pages.Select(p => p.OffsetY).Distinct().Count().Should().BeGreaterThan(1);
    }

    // A connector crossing a cut must appear on both sheets, or the printed poster cannot be
    // reassembled (design §4.5).
    [Fact]
    public void Consecutive_rows_overlap_by_the_bleed()
    {
        var pages = A4Paginator.Pages(Scene(400, 2400)).ToList();

        var first = pages[0];
        var second = pages[1];

        (first.OffsetY + first.Height - second.OffsetY).Should().BeApproximately(18, 1e-4);
    }

    [Fact]
    public void Every_part_of_the_scene_is_covered_by_some_page()
    {
        var pages = A4Paginator.Pages(Scene(1400, 2000)).ToList();

        pages.Max(p => p.OffsetX + p.Width).Should().BeGreaterThanOrEqualTo(1400);
        pages.Max(p => p.OffsetY + p.Height).Should().BeGreaterThanOrEqualTo(2000);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter A4PaginatorTests`
Expected: FAIL — the stub throws `TooLargeException`.

- [ ] **Step 3: Implement the paginator**

Replace the body of `src/FamilyTree.Infrastructure/Export/A4Paginator.cs`:

```csharp
using FamilyTree.Application.Export;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// Tiles one scene across A4 pages (design §4.5). Pages overlap by <see cref="Bleed"/> so a
/// connector crossing a cut appears on both sheets — without it the printed poster cannot be
/// reassembled, because the reader cannot tell which line continues where.
/// </summary>
public static class A4Paginator
{
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Bleed = 18f;

    public static IEnumerable<PageWindow> Pages(TreeScene scene)
    {
        var width = (float)(scene.Bounds.Width * scene.Scale);
        var height = (float)(scene.Bounds.Height * scene.Scale);

        // Each step advances by less than a full page, so successive windows overlap by Bleed.
        var stepX = PageWidth - Bleed;
        var stepY = PageHeight - Bleed;

        for (var y = 0f; ; y += stepY)
        {
            for (var x = 0f; ; x += stepX)
            {
                yield return new PageWindow(PageWidth, PageHeight, x, y);
                if (x + PageWidth >= width) break;
            }

            if (y + PageHeight >= height) break;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter A4PaginatorTests`
Expected: PASS — 5 tests.

- [ ] **Step 5: Verify a real A4 export end to end**

With the container running and `$TOKEN` set as in Task 10:

```bash
curl -s -o /tmp/a4.pdf -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/api/v1/family-tree/export.pdf?page=a4"
pdfinfo /tmp/a4.pdf | grep -E "Pages|Page size"
```

Expected: multiple pages, each 595 × 842 pt.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyTree.Infrastructure/Export tests/FamilyTree.Application.Tests/Export
git commit -m "feat: tile the export across A4 pages with a reassembly bleed"
```

---

## Task 14: The clean style

**Files:**
- Create: `src/FamilyTree.Application/Export/CleanLayoutStrategy.cs`
- Modify: `src/FamilyTree.Infrastructure/Export/TreeRendererAdapter.cs`
- Test: `tests/FamilyTree.Application.Tests/Export/CleanLayoutStrategyTests.cs`

**Interfaces:**
- Consumes: `ILayoutStrategy`, `VerticalPacking`, `ColumnAssignment`, `ConnectorBuilder`, `SceneNormaliser`.
- Produces: `sealed class CleanLayoutStrategy : ILayoutStrategy` with `Name => "clean"`.

Single-direction: root on the leading edge, generations in aligned columns growing one way, elbows throughout, no ribbons. Colours still bind to top-level branch.

- [ ] **Step 1: Write the failing test**

`tests/FamilyTree.Application.Tests/Export/CleanLayoutStrategyTests.cs`:

```csharp
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class CleanLayoutStrategyTests
{
    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static TreeScene Build(params FamilyTreeNodeResponse[] roots) =>
        new CleanLayoutStrategy().Build(roots, LayoutOptions.Default, Stub);

    [Fact]
    public void The_strategy_names_itself_clean()
    {
        new CleanLayoutStrategy().Name.Should().Be("clean");
    }

    [Fact]
    public void Every_member_appears_exactly_once()
    {
        var scene = Build(Node("r", Node("a", Node("a1")), Node("b")));

        scene.Nodes.Should().HaveCount(4);
        scene.Nodes.Select(n => n.Id).Should().OnlyHaveUniqueItems();
    }

    // The clean style is single-direction: nothing may sit on the opposite side of the root.
    [Fact]
    public void Every_branch_grows_the_same_way_from_the_root()
    {
        var scene = Build(Node("r", Node("a"), Node("b"), Node("c")));
        var root = scene.Nodes.Single(n => n.Label == "r");

        scene.Nodes.Where(n => n.Label != "r").Should().OnlyContain(n => n.X > root.X);
    }

    [Fact]
    public void Generations_line_up_in_shared_columns()
    {
        var scene = Build(Node("r", Node("aaaa", Node("x")), Node("b", Node("yyyy"))));
        var byLabel = scene.Nodes.ToDictionary(n => n.Label);

        byLabel["aaaa"].X.Should().Be(byLabel["b"].X);
        byLabel["x"].X.Should().Be(byLabel["yyyy"].X);
    }

    [Fact]
    public void No_ribbons_are_emitted()
    {
        Build(Node("r", Node("a", Node("a1"))))
            .Connectors.Should().NotContain(c => c.Kind == ConnectorKind.Ribbon);
    }

    [Fact]
    public void Descendants_still_inherit_their_branch_hue()
    {
        var scene = Build(Node("r", Node("a", Node("a1")), Node("b")));
        var byLabel = scene.Nodes.ToDictionary(n => n.Label);

        byLabel["a1"].Color.Should().Be(byLabel["a"].Color);
        byLabel["b"].Color.Should().NotBe(byLabel["a"].Color);
    }

    [Fact]
    public void An_empty_tree_produces_an_empty_scene()
    {
        var scene = new CleanLayoutStrategy().Build([], LayoutOptions.Default, Stub);
        scene.Nodes.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter CleanLayoutStrategyTests`
Expected: FAIL — `CleanLayoutStrategy` does not exist.

- [ ] **Step 3: Implement the strategy**

`src/FamilyTree.Application/Export/CleanLayoutStrategy.cs`:

```csharp
using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

/// <summary>
/// A designed single-direction layout (design §1.1): root on the leading edge, generations in
/// aligned columns, elbows throughout. Shares passes 2, 3 and 5 with the xmind style and
/// differs only in never splitting the tree across two sides — which is what lets the two
/// styles stay additive rather than becoming separate engines.
/// </summary>
public sealed class CleanLayoutStrategy : ILayoutStrategy
{
    public string Name => "clean";

    public TreeScene Build(
        IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutOptions options, MeasureText measure)
    {
        if (roots.Count == 0) return new TreeScene([], [], new SceneBounds(0, 0, 0, 0));

        var metrics = options.Metrics;
        var packed = VerticalPacking.Pack(roots, metrics, measure);

        var nodes = new List<SceneNode>();
        var connectors = new List<SceneConnector>();
        var cursorY = 0.0;

        foreach (var root in packed)
        {
            root.Shift(cursorY - root.Top);
            ColumnAssignment.Assign(root, startX: 0, direction: 1, metrics);
            cursorY = root.Bottom + metrics.SiblingGroupGap;

            foreach (var node in root.Descend())
            {
                // Depth 0 is the root itself; everything below it wears its branch's hue.
                var color = node.Depth == 0
                    ? options.Palette.CentreColor
                    : options.Palette.ColorAt(node.BranchIndex);

                var fontSize = metrics.FontSizeForDepth(node.Depth);

                nodes.Add(new SceneNode(
                    node.Source.Id, node.Source.Name, node.X, node.Y, node.Width,
                    fontSize * 1.6, fontSize, color, metrics.ShapeForDepth(node.Depth)));

                connectors.Add(ConnectorBuilder.Tick(
                    new ScenePoint(node.X, node.Y),
                    new ScenePoint(node.X + node.Width, node.Y),
                    color, metrics.ConnectorStroke));

                foreach (var child in node.Children)
                    connectors.Add(ConnectorBuilder.Elbow(
                        new ScenePoint(node.X + node.Width, node.Y),
                        new ScenePoint(child.X, child.Y),
                        junctionX: node.X + node.Width + metrics.ColumnGap / 2,
                        options.Palette.ColorAt(child.BranchIndex),
                        metrics.ConnectorStroke));
            }
        }

        return SceneNormaliser.Normalise(nodes, connectors, metrics);
    }
}
```

- [ ] **Step 4: Wire it into the adapter**

In `src/FamilyTree.Infrastructure/Export/TreeRendererAdapter.cs`, replace the placeholder:

```csharp
    private static readonly ILayoutStrategy Xmind = new XmindLayoutStrategy();
    private static readonly ILayoutStrategy Clean = new CleanLayoutStrategy();
```

and swap `var strategy = Xmind;` for:

```csharp
        var strategy = style switch
        {
            ExportStyle.Xmind => Xmind,
            ExportStyle.Clean => Clean,
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter CleanLayoutStrategyTests`
Expected: PASS — 7 tests.

- [ ] **Step 6: Run everything**

```bash
dotnet test
cd frontend && npm test
```

Expected: PASS across backend unit and integration suites (Docker required) and the frontend.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyTree.Application/Export src/FamilyTree.Infrastructure/Export tests/FamilyTree.Application.Tests/Export
git commit -m "feat: add the clean single-direction export style"
```

---

## Task 15: Documentation

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add the error code to the table**

In the README's "API error codes" table, after `TENANT_INACTIVE`:

```markdown
| `EXPORT_TREE_TOO_LARGE` | 413 | Tree exceeds the export member cap, or cannot fit one sheet legibly. The `reason` extension is `member-cap` or `sheet-overflow`. |
```

- [ ] **Step 2: Document the endpoint**

Add after the "Search" section:

```markdown
## PDF export

`GET /api/v1/family-tree/export.pdf?rootId=<guid>&maxDepth=<n>&style=<xmind|clean>&page=<sheet|a4>`
requires `FamilyTree.View` and streams a PDF poster of the tree.

`style` chooses between the mind-map replica of `familytree.pdf` and a cleaner single-direction
design; `page` chooses one tall sheet or tiled A4. `rootId` selects a **subtree** — it does not
re-root, so no value reproduces the reference's centring of سليمان. Design:
`docs/superpowers/specs/2026-08-18-tree-pdf-export-design.md`.

Arabic is shaped with HarfBuzz against embedded Noto fonts, and the output keeps a `/ToUnicode`
map, so names in the PDF stay selectable and searchable.

A single sheet is capped at 14,400 pt by the PDF format. Past that the diagram is scaled down;
below a 6 pt font it is refused with `EXPORT_TREE_TOO_LARGE` rather than emitted illegibly.

The API image installs `libfontconfig1` and `libfreetype6` for SkiaSharp; without them the
endpoint throws at first use even though startup succeeds.
```

- [ ] **Step 3: Verify the claims match the code**

```bash
grep -rn "EXPORT_TREE_TOO_LARGE" README.md src/FamilyTree.Application/Export/
grep -n "libfontconfig1" src/FamilyTree.Api/Dockerfile
grep -n "export.pdf" README.md src/FamilyTree.Api/Endpoints/FamilyTrees/FamilyTreeEndpoints.cs
```

Expected: each string appears in both the README and the code, spelled identically.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: document the PDF export endpoint and its error code"
```

---

## Self-Review

**1. Spec coverage**

| Spec section | Task |
|---|---|
| §1.1 two styles | 5 (xmind), 14 (clean) |
| §1.1 two page formats | 8 (sheet), 13 (a4) |
| §2.3 `rootId` selects a subtree, no re-rooting | 9, 15 |
| §3.1 palette, hue inheritance, greyscale separation | 1, 5, 14 |
| §3.2 measured geometry | 1 (`LayoutMetrics`), 2, 3 |
| §3.3 Noto fonts embedded | 7 |
| §4.1 SkiaSharp + HarfBuzz | 7, 8 |
| §4.2 module boundaries, injected measurement | 1, 2, 7, 8 |
| §4.3 five passes, forests, connectors | 2, 3, 4, 5 |
| §4.4 overflow scaling, 6 pt floor | 6 |
| §4.5 A4 tiling and bleed | 13 |
| §4.6 caption furniture | **not covered — see below** |
| §5.1 endpoint, permission, RFC 5987 filename | 9 |
| §5.2 member cap, render semaphore | 8 |
| §5.3 error codes and the `reason` extension | 6, 9 |
| §5.4 frontend blob path and control | 11 |
| §6 container native assets | 10 |
| §7.1 layout unit tests | 2, 3, 4, 5, 6 |
| §7.2 `pdftotext` gate + round-trip | 8 (gate), 12 (round-trip) |
| §7.3 integration tests | 9 |

**One gap, deliberately left:** §4.6's bottom-margin caption (tree name, member count,
generation count, export date) has no task. It depends on nothing and is a small addition near
`SkiaTreeRenderer.DrawLabel`, but it is genuinely unbuilt when Task 15 finishes. The spec permits
a pixel-bare diagram, so this is a decision to make rather than an oversight to absorb: add it as
Task 16 if it should ship, or drop it.

**Two spec details relaxed, and why:** §4.5's continuation markers ("يتبع ص N") and the
title page with a page-grid map are not in Task 13, which delivers correct tiling and bleed only.
§4.5's rule that cuts prefer a sibling-group gap within ±40 pt is likewise not implemented — the
bleed alone prevents sliced labels. Both are refinements over a working paginator rather than
prerequisites for it. If they matter for the first release, they belong in a Task 13b.

**2. Placeholder scan**

No `TBD`/`TODO`. Task 8's `A4Paginator` throws explicitly rather than silently degrading, and
Task 13 replaces it. Task 7 Step 4, Task 11 Step 7, and Task 12 Step 1 direct the implementer to
read named files or compile against the resolved package before writing — those are real
instructions with named targets, not deferred decisions.

**3. Type consistency**

Checked across tasks: `MeasureText`, `PackedNode`, `TreeScene`, `SceneNode`, `SceneConnector`,
`ScenePoint`, `SceneBounds`, `NodeShape`, `ConnectorKind`, `LayoutMetrics`, `BranchPalette`,
`LayoutOptions`, `Side`, `ILayoutStrategy`, `SceneNormaliser`, `ITreeRenderer`,
`ITreeRendererAdapter`, `IFamilyTreeExporter`, `ExportResult`, `ExportStyle`,
`ExportPageFormat`, `PageWindow`, `TooLargeException`. Each is defined once and spelled
identically everywhere it is used.

`ColumnAssignment.Assign(branchRoot, startX, direction, metrics)` keeps one signature across
Tasks 3, 5 and 14. `ConnectorBuilder.Elbow`'s `junctionX` parameter is named consistently in
Tasks 5 and 14. `PageWindow` is declared once, in `SheetPaginator.cs` (Task 8), and consumed by
`SkiaTreeRenderer` and `A4Paginator`. `SceneNormaliser` was extracted in Task 5 specifically so
Task 14 would not duplicate the normalisation logic — an earlier draft had two copies.

`PackedNode` carries explicit `Top`/`Bottom` with `Height` derived, and a `Shift` method, rather
than deriving the band from `Y`. That is deliberate: a parent's `Y` is a *straddle* of its first
and last child, so it is not the midpoint of its own band, and any arithmetic that assumes
otherwise is wrong for exactly the lopsided case Task 2's discriminating test covers. All three
consumers — `VerticalPacking.StackChildren`, `XmindLayoutStrategy.PlaceSides`, and
`CleanLayoutStrategy` — use the same `Shift(cursor - node.Top)` / `cursor = node.Bottom` idiom.

**4. Known risk carried deliberately**

Task 7 Step 4 does not assert a SkiaSharp API shape; it has the implementer discover the overload
for the resolved package version. The `SKFont` and `SKPaint` forms differ across major versions,
and a confidently wrong code block would cost more than the discovery step does.
