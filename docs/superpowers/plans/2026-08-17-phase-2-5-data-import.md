# Phase 2.5 — Data Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconstruct the ~349-member family hierarchy from `familytree.pdf` into a reviewed, human-confirmed artifact, and only then turn it into seed data.

**Architecture:** A standalone .NET console tool, `FamilyTree.Import`, reads the PDF's content stream directly — no PDF library — because the file is a single-page Skia export whose geometry has been characterised and is highly regular. The tool is a pipeline of pure, individually testable stages: tokenize → resolve `ToUnicode` → classify paths → assign glyphs to boxes → derive links → normalize Arabic → emit an indented tree. The emitted tree is a **review gate**, not an intermediate. Only after a human confirms it does a separate task turn the committed JSON into a seed migration with raw SQL.

**Tech Stack:** .NET 10 / C# 14, xUnit, `System.IO.Compression` for FlateDecode, EF Core migrations with `migrationBuilder.Sql`.

**Spec:** `docs/superpowers/specs/2026-08-16-family-tree-saas-design.md` — §7 (Data import) is the governing section; §8 places this phase between 2 and 3.

---

## Global Constraints

- **The §7.2 step-5 gate is a HARD STOP.** Task 6 emits the reviewed artifact and execution **stops there** for human confirmation. Tasks 7–8 do not begin until a human has confirmed the tree file. This is one of subagent-driven-development's four legitimate stops — it overrides continuous execution. Do not seed unreviewed data.
- No PDF parsing library. The content stream is decoded directly; this is deliberate and characterised below.
- Every extraction stage is a pure function over its input, unit-testable without the database.
- `familytree.pdf` is the test fixture. Extraction is deterministic, so tests assert exact counts.
- Arabic normalization is `String.Normalize(NormalizationForm.FormKC)` — never a hand-written presentation-form table.
- Names are bounded by `MaxNameLength = 200` (matches `FamilyMember.Create`).
- The tool never writes to the database. It writes files only. Seeding is a migration.
- No secrets, no connection strings, no network access in the import tool.

## Characterisation already performed

These facts were established by probing the actual file. Implementers should treat them as
expected values and **report immediately if their code disagrees** — a mismatch means the
probe or the implementation is wrong, and that is worth surfacing, not working around.

| Property | Measured value |
|---|---|
| PDF | 67,473 bytes, PDF-1.4, single page, Producer `Skia/PDF m73` |
| Streams | 5 FlateDecode streams; the content stream is the largest (244,206 bytes inflated) |
| Fonts | 2 subset CID fonts, `Identity-H`, both with `ToUnicode` CMaps |
| Glyphs | 1,887 `Tj` operators, one glyph each, each with its own `Tm` |
| CMap grammar | `bfchar` blocks **and** one `bfrange` block; parsing them with one regex is a bug that silently drops ح، ع، ف، ق، ل |
| `bfrange` contents | `<03A2><03A3><FEA2>`, `<03CB><03CC><FECB>`, `<03D3><03D4><FED3>`, `<03D6><03D7><FED6>`, `<03DF><03E0><FEDF>` |
| Unresolved glyphs | **zero**, once `bfrange` is parsed correctly |
| Text runs | 349 (spec says "approximately 350 names") |
| Paths | 1,218 total |
| Path signatures | `l+ end=h` ×344, `l+cl end=S` ×338, `c+ end=h` ×258, `l end=f` ×129, `l end=S` ×129, `lclclclc` ×10 |
| Font sizes | 35.57, 23.71, 17.78 — the largest is expected to be a title/root label, not a member |

**Known-unsolved at plan time:** which signature family is a node box and which is a connector.
Both `l+ end=h` (344) and `l+cl end=S` (338) are close to 349. Task 4 resolves this empirically
rather than by assumption.

---

### Task 1: Project skeleton and PDF stream decoding

**Files:**
- Create: `tools/FamilyTree.Import/FamilyTree.Import.csproj`
- Create: `tools/FamilyTree.Import/PdfStreams.cs`
- Create: `tests/FamilyTree.Import.Tests/FamilyTree.Import.Tests.csproj`
- Create: `tests/FamilyTree.Import.Tests/PdfStreamsTests.cs`
- Create: `tests/FamilyTree.Import.Tests/TestPaths.cs`
- Modify: `FamilyTree.sln` — add both projects

**Interfaces:**
- Produces: `public static IReadOnlyList<byte[]> PdfStreams.Inflate(byte[] pdf)` — every FlateDecode stream, in file order, skipping any that fail to inflate.
- Produces: `public static byte[] PdfStreams.LargestOf(IReadOnlyList<byte[]> streams)` — the content stream.
- Produces: `public static class TestPaths { public static string FamilyTreePdf { get; } }`

- [ ] **Step 1: Write the failing test**

```csharp
public static class TestPaths
{
    public static string FamilyTreePdf { get; } = Resolve();

    private static string Resolve()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "familytree.pdf")))
            dir = dir.Parent;

        return dir is null
            ? throw new FileNotFoundException("familytree.pdf not found above the test binary.")
            : Path.Combine(dir.FullName, "familytree.pdf");
    }
}

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
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/FamilyTree.Import.Tests --filter PdfStreamsTests`
Expected: FAIL — `PdfStreams` does not exist.

- [ ] **Step 3: Implement**

Scan for `stream`, skip the EOL after it (`\r`, `\n`, or `\r\n`), read to `endstream`,
decompress with `ZLibStream`, ignore failures.

```csharp
public static class PdfStreams
{
    public static IReadOnlyList<byte[]> Inflate(byte[] pdf)
    {
        var results = new List<byte[]>();
        var marker = "stream"u8.ToArray();
        var end = "endstream"u8.ToArray();

        for (var i = 0; ; )
        {
            var start = IndexOf(pdf, marker, i);
            if (start < 0) break;

            var data = start + marker.Length;
            if (data < pdf.Length && pdf[data] == (byte)'\r') data++;
            if (data < pdf.Length && pdf[data] == (byte)'\n') data++;

            var stop = IndexOf(pdf, end, data);
            if (stop < 0) break;

            try
            {
                using var input = new MemoryStream(pdf, data, stop - data);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                results.Add(output.ToArray());
            }
            catch (InvalidDataException)
            {
                // Not a Flate stream — the geometry we need is, so skipping is correct here.
            }

            i = stop + end.Length;
        }

        return results;
    }

    public static byte[] LargestOf(IReadOnlyList<byte[]> streams) =>
        streams.MaxBy(s => s.Length) ?? throw new InvalidOperationException("No streams.");

    private static int IndexOf(byte[] haystack, byte[] needle, int from) { /* plain scan */ }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Import.Tests --filter PdfStreamsTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add tools/FamilyTree.Import tests/FamilyTree.Import.Tests FamilyTree.sln
git commit -m "feat: decode familytree.pdf flate streams"
```

---

### Task 2: ToUnicode CMap — both grammars

**Files:**
- Create: `tools/FamilyTree.Import/ToUnicodeCMap.cs`
- Create: `tests/FamilyTree.Import.Tests/ToUnicodeCMapTests.cs`

**Interfaces:**
- Consumes: `PdfStreams.Inflate`
- Produces: `public sealed class ToUnicodeCMap { public static ToUnicodeCMap Parse(IEnumerable<byte[]> streams); public string? Lookup(int glyphId); public int Count { get; } }`

**This is the task the characterisation singles out.** `bfchar` maps single glyphs; `bfrange`
maps an inclusive range `<lo> <hi> <dstStart>` where glyph `lo + k` maps to `dstStart + k`.
Parsing both with one regex silently drops ح، ع، ف، ق، ل — names still *look* plausible
(محمد becomes ممد), so this fails quietly. Parse the two block types separately.

- [ ] **Step 1: Write the failing test**

```csharp
public sealed class ToUnicodeCMapTests
{
    private static ToUnicodeCMap Map() =>
        ToUnicodeCMap.Parse(PdfStreams.Inflate(File.ReadAllBytes(TestPaths.FamilyTreePdf)));

    [Theory]
    [InlineData(0x03A3, "ﺣ")] // inside a bfrange — the letter ح
    [InlineData(0x03CB, "ﻋ")] // inside a bfrange — the letter ع
    [InlineData(0x03DF, "ﻟ")] // inside a bfrange — the letter ل
    [InlineData(0x038D, "ا")] // a plain bfchar entry — alef
    public void Resolves_both_bfchar_and_bfrange_entries(int glyphId, string expected)
    {
        Assert.Equal(expected, Map().Lookup(glyphId));
    }

    [Fact]
    public void Range_endpoints_both_resolve()
    {
        // <03A2> <03A3> <FEA2>: lo and hi must both map, offset by their distance from lo.
        Assert.Equal("ﺢ", Map().Lookup(0x03A2));
        Assert.Equal("ﺣ", Map().Lookup(0x03A3));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/FamilyTree.Import.Tests --filter ToUnicodeCMapTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement**

```csharp
public sealed class ToUnicodeCMap
{
    private readonly Dictionary<int, string> _map = new();

    public int Count => _map.Count;
    public string? Lookup(int glyphId) => _map.GetValueOrDefault(glyphId);

    public static ToUnicodeCMap Parse(IEnumerable<byte[]> streams)
    {
        var cmap = new ToUnicodeCMap();
        foreach (var raw in streams)
        {
            var text = Encoding.Latin1.GetString(raw);
            if (!text.Contains("begincmap", StringComparison.Ordinal)) continue;

            foreach (Match block in Regex.Matches(text, "beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match e in Regex.Matches(block.Groups[1].Value, "<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>"))
                    cmap._map[Hex(e.Groups[1])] = Char(e.Groups[2]);

            foreach (Match block in Regex.Matches(text, "beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match e in Regex.Matches(block.Groups[1].Value,
                             "<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>"))
                {
                    int lo = Hex(e.Groups[1]), hi = Hex(e.Groups[2]), dst = Hex4(e.Groups[3]);
                    for (var g = lo; g <= hi; g++)
                        cmap._map[g] = char.ConvertFromUtf32(dst + (g - lo));
                }
        }
        return cmap;
    }

    private static int Hex(Group g) => Convert.ToInt32(g.Value, 16);
    private static int Hex4(Group g) => Convert.ToInt32(g.Value[..4], 16);
    private static string Char(Group g) => char.ConvertFromUtf32(Hex4(g));
}
```

- [ ] **Step 4: Run tests to verify they pass** — Expected: PASS

- [ ] **Step 5: Commit**

```bash
git commit -am "feat: parse ToUnicode bfchar and bfrange blocks"
```

---

### Task 3: Content-stream interpreter — glyphs and paths in page space

**Files:**
- Create: `tools/FamilyTree.Import/ContentStream.cs`
- Create: `tests/FamilyTree.Import.Tests/ContentStreamTests.cs`
- Create: `tests/FamilyTree.Import.Tests/TestPdf.cs` — memoized pipeline fixture

**Interfaces:**
- Consumes: `PdfStreams.LargestOf`, `ToUnicodeCMap`
- Produces:
  - `public readonly record struct Glyph(int GlyphId, string Text, double X, double Y, double Size);`
  - `public sealed record PdfPath(IReadOnlyList<(double X, double Y)> Points, string Ops, char Terminator);`
  - `public sealed record PageContent(IReadOnlyList<Glyph> Glyphs, IReadOnlyList<PdfPath> Paths);`
  - `public static PageContent ContentStream.Read(byte[] content, ToUnicodeCMap cmap);`

Track the CTM through `q`/`Q`/`cm`; track `Tm` and the `Tf` size; on `Tj` transform the text
origin by `Tm × CTM`. For paths, `m` starts one, `l`/`c` append (for `c` take the final control
point — the endpoint, which is what bounding boxes and endpoints need), and `S`/`f`/`h`/`B`/`n`
terminate it. Record the operator sequence and terminator: Task 4 classifies on them.

- [ ] **Step 1: Write the failing test**

```csharp
public static class TestPdf
{
    private static readonly Lazy<PageContent> _page = new(() =>
    {
        var streams = PdfStreams.Inflate(File.ReadAllBytes(TestPaths.FamilyTreePdf));
        return ContentStream.Read(PdfStreams.LargestOf(streams), ToUnicodeCMap.Parse(streams));
    });

    public static PageContent Page() => _page.Value;
}

public sealed class ContentStreamTests
{
    [Fact]
    public void Reads_every_glyph() => Assert.Equal(1887, TestPdf.Page().Glyphs.Count);

    [Fact]
    public void Leaves_no_glyph_unresolved()
    {
        // One unmapped glyph silently corrupts a name, so this is an exact zero.
        Assert.Empty(TestPdf.Page().Glyphs.Where(g => g.Text.Length == 0));
    }

    [Fact]
    public void Reads_every_path() => Assert.Equal(1218, TestPdf.Page().Paths.Count);

    [Fact]
    public void Reports_the_three_font_sizes()
    {
        var sizes = TestPdf.Page().Glyphs.Select(g => Math.Round(g.Size, 2)).Distinct().Order().ToArray();

        Assert.Equal(new[] { 17.78, 23.71, 35.57 }, sizes);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL, type does not exist.

- [ ] **Step 3: Implement the interpreter**

Matrix multiply in the standard PDF 3×2 form, then a token loop over whitespace-split tokens.

- [ ] **Step 4: Run tests to verify they pass**

Expected: PASS. If any count disagrees with the characterisation table, **stop and report** —
do not adjust the expected value to match the code.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat: interpret the PDF content stream into glyphs and paths"
```

---

### Task 4: Classify paths into node boxes and connectors

**Files:**
- Create: `tools/FamilyTree.Import/Geometry.cs`
- Create: `tests/FamilyTree.Import.Tests/GeometryTests.cs`

**Interfaces:**
- Consumes: `PageContent`
- Produces:
  - `public readonly record struct Box(double X0, double Y0, double X1, double Y1);`
  - `public sealed record Connector((double X, double Y) A, (double X, double Y) B);`
  - `public sealed record Classified(IReadOnlyList<Box> Boxes, IReadOnlyList<Connector> Connectors);`
  - `public static Classified Geometry.Classify(PageContent page);`
  - `public static bool Geometry.Contains(Box b, double x, double y);`
  - `public static bool Geometry.Overlaps(Box a, Box b);`

**This task resolves the one open question in the characterisation.** Two signature families
sit near the 349 expected nodes: `l+ end=h` (344) and `l+cl end=S` (338). Decide empirically:

1. Compute both candidate box sets.
2. For each, count how many of the 1,887 glyphs fall inside exactly one box.
3. The node-box family is the one containing essentially all glyphs. The other is connectors.

Write down which family won, and why, in a comment — the next reader should not have to
re-derive it. Include the `lclclclc` rounded rects as boxes.

- [ ] **Step 1: Write the failing test**

```csharp
public sealed class GeometryTests
{
    private static Classified Classify() => Geometry.Classify(TestPdf.Page());

    [Fact]
    public void Finds_one_box_per_name()
    {
        // 349 text runs were measured in the source PDF; every one needs a box to live in.
        Assert.InRange(Classify().Boxes.Count, 345, 355);
    }

    [Fact]
    public void Every_glyph_lands_inside_a_box()
    {
        var boxes = Classify().Boxes;
        var orphans = TestPdf.Page().Glyphs
            .Where(g => !boxes.Any(b => Geometry.Contains(b, g.X, g.Y)))
            .ToArray();

        Assert.Empty(orphans);
    }

    [Fact]
    public void Boxes_do_not_overlap()
    {
        // Overlapping boxes make glyph assignment ambiguous and names interleave.
        var boxes = Classify().Boxes;
        var overlaps =
            from i in Enumerable.Range(0, boxes.Count)
            from j in Enumerable.Range(i + 1, boxes.Count - i - 1)
            where Geometry.Overlaps(boxes[i], boxes[j])
            select (i, j);

        Assert.Empty(overlaps);
    }

    [Fact]
    public void Finds_a_connector_for_all_but_the_root()
    {
        // A tree of N nodes has exactly N-1 edges; allow margin for decorative strokes the
        // classifier may not distinguish, but not a wholesale mismatch.
        var c = Classify();
        Assert.InRange(c.Connectors.Count, c.Boxes.Count - 5, c.Boxes.Count + 20);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL

- [ ] **Step 3: Implement `Classify`, `Contains`, `Overlaps`**

- [ ] **Step 4: Run tests to verify they pass**

If `Every_glyph_lands_inside_a_box` cannot be made to pass, **stop and report the orphan
glyphs' coordinates and font sizes**. The most likely benign cause is the 35.57pt title text
sitting outside every node box — if so, exclude it explicitly by size and say so in a comment,
rather than loosening the assertion to "most glyphs".

- [ ] **Step 5: Commit**

```bash
git commit -am "feat: classify PDF paths into node boxes and connectors"
```

---

### Task 5: Names and hierarchy

**Files:**
- Create: `tools/FamilyTree.Import/Reconstruct.cs`
- Create: `tests/FamilyTree.Import.Tests/ReconstructTests.cs`

**Interfaces:**
- Consumes: `PageContent`, `Classified`
- Produces:
  - `public sealed record ImportedMember(int Id, string Name, int? ParentId);`
  - `public sealed record Reconstruction(IReadOnlyList<ImportedMember> Members, string Orientation);`
  - `public static Reconstruction Reconstruct.Build(PageContent page, Classified geometry);`

**Name assembly.** Group a box's glyphs into rows by Y, order each row by X, then **reverse** —
Skia emits Arabic glyphs in visual (right-to-left) order, so logical order is the reverse. Join
rows top to bottom, `Normalize(NormalizationForm.FormKC)` to fold presentation forms to base
letters, collapse whitespace, trim.

**Hierarchy.** Each connector's two endpoints attach to the nearest box (by distance to the box
rectangle, rejecting anything further than ~30 units). Direction is decided **globally, not per
edge**: build the parent map under both hypotheses (parent is the left box / parent is the
right box) and keep the one yielding the fewest roots with no cycles. Record which won in
`Orientation`.

- [ ] **Step 1: Write the failing test**

```csharp
public sealed class ReconstructTests
{
    private static Reconstruction Build() => TestPdf.Reconstruction();

    [Fact]
    public void Every_member_has_a_name()
    {
        Assert.Empty(Build().Members.Where(m => string.IsNullOrWhiteSpace(m.Name)));
    }

    [Fact]
    public void Names_contain_only_Arabic_letters_and_spaces()
    {
        // Catches unmapped glyphs and un-normalised presentation forms (U+FB50..U+FEFF) alike.
        var bad = Build().Members
            .Where(m => m.Name.Any(c => c != ' ' && (c < 'ؠ' || c > 'ي')))
            .ToArray();

        Assert.Empty(bad);
    }

    [Fact]
    public void Decodes_known_names()
    {
        var names = Build().Members.Select(m => m.Name).ToHashSet();

        // Each of these exercises a letter that lives in a bfrange block.
        Assert.Contains("محمد", names);   // ح
        Assert.Contains("سليمان", names); // ل
        Assert.Contains("علي", names);    // ع
    }

    [Fact]
    public void Forms_a_single_tree()
    {
        Assert.Single(Build().Members.Where(m => m.ParentId is null));
    }

    [Fact]
    public void Has_no_orphans_or_cycles()
    {
        var members = Build().Members;
        var byId = members.ToDictionary(m => m.Id);

        foreach (var m in members)
        {
            var seen = new HashSet<int>();
            var cur = m;
            while (cur.ParentId is { } p)
            {
                Assert.True(byId.ContainsKey(p), $"{cur.Name} points at a missing parent.");
                Assert.True(seen.Add(cur.Id), $"{m.Name} sits on a cycle.");
                cur = byId[p];
            }
        }
    }

    [Fact]
    public void Names_fit_the_domain_limit()
    {
        Assert.Empty(Build().Members.Where(m => m.Name.Length > 200));
    }
}
```

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL

- [ ] **Step 3: Implement `Build`**

- [ ] **Step 4: Run tests to verify they pass**

`Forms_a_single_tree` is the assertion most likely to need iteration. If more than one root
survives, **report the extra roots by name and coordinates** — genuine multiple roots are
possible in the source and are a finding for the human reviewer, not necessarily a bug. Do not
force them under a synthetic parent to make the test pass.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat: reconstruct names and parent-child links from geometry"
```

---

### Task 6: Emit the review artifact — **THEN STOP**

**Files:**
- Create: `tools/FamilyTree.Import/Emit.cs`
- Create: `tools/FamilyTree.Import/Program.cs`
- Create: `tests/FamilyTree.Import.Tests/EmitTests.cs`
- Output: `docs/import/family-tree.txt` (indented, human-readable — the §7.2 gate)
- Output: `docs/import/family-tree.json` (machine-readable, feeds Task 7)

**Interfaces:**
- Produces: `public static string Emit.IndentedTree(Reconstruction r)` and `public static string Emit.Json(Reconstruction r)`

The console app takes the PDF path and an output directory, runs the pipeline, writes both
files, and prints a summary: member count, root count, max depth, distinct-name count, and the
ten most frequently repeated names. **The repeated-name summary matters** — §3.4 and §5.4
justify trigram search and ancestor paths by the presence of many duplicate names, and this is
the first real measurement of that claim.

The indented file uses two spaces per generation, one name per line, siblings ordered by Y.

- [ ] **Step 1: Write the failing test**

```csharp
public sealed class EmitTests
{
    [Fact]
    public void Indented_tree_has_one_line_per_member()
    {
        var r = TestPdf.Reconstruction();
        var lines = Emit.IndentedTree(r).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(r.Members.Count, lines.Length);
    }

    [Fact]
    public void Indentation_equals_generation_depth()
    {
        var lines = Emit.IndentedTree(TestPdf.Reconstruction())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(0, lines[0].TakeWhile(char.IsWhiteSpace).Count()); // the root
        Assert.All(lines, l => Assert.Equal(0, l.TakeWhile(char.IsWhiteSpace).Count() % 2));
    }
}
```

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL

- [ ] **Step 3: Implement and run the tool**

```bash
dotnet run --project tools/FamilyTree.Import -- familytree.pdf docs/import
```

- [ ] **Step 4: Commit the artifacts**

```bash
git add tools/FamilyTree.Import tests/FamilyTree.Import.Tests docs/import
git commit -m "feat: emit reviewable family tree reconstruction"
```

- [ ] **Step 5: STOP — present the artifact for human review**

**Execution halts here.** Report to the human partner:
- the summary counts,
- the top ten repeated names with their frequencies,
- the first ~40 lines of `docs/import/family-tree.txt`,
- anything ambiguous the pipeline had to decide.

Then ask them to review `docs/import/family-tree.txt` and confirm. Spec §7.2: *"Step 5 is a
gate, not a formality."* Do not start Task 7 without that confirmation.

---

### Task 7: Seed migration — **only after confirmation**

**Files:**
- Create: `src/FamilyTree.Infrastructure/Persistence/Migrations/<timestamp>_SeedImportedFamily.cs`
- Modify: `src/FamilyTree.Infrastructure/Persistence/Seed/DatabaseSeeder.cs`
- Modify: whichever integration tests assert against the demo family

**Interfaces:**
- Consumes: `docs/import/family-tree.json`

**Decision recorded here for the human to override at the Task 6 gate:** the imported family
**replaces** the 14-member demo family in the seeded tenant. That demo set was scaffolding for
Phase 2 smoke testing; this is the real data, and keeping both would make member counts and
search results meaningless. If the reviewer wants them side by side, that is a second tenant,
not a second root.

Insert with raw SQL via `migrationBuilder.Sql`, **ordered by generation** — parents before
children — so the `fk_member_parent` composite foreign key is satisfied row by row. This
sidesteps EF's inability to order a parent before its child within one `SaveChanges`, which is
precisely why a migration is the right vehicle rather than a seeder loop.

- [ ] **Step 1: Write the failing integration test**

```csharp
[Fact]
public async Task Seeded_tree_matches_the_reviewed_artifact()
{
    var members = await Client.GetFromJsonAsync<FamilyMemberResponse[]>("/api/v1/family-members");

    Assert.Equal(ExpectedCountFromArtifact, members!.Length);
    Assert.Single(members.Where(m => m.ParentId is null));
}

[Fact]
public async Task Whole_tree_is_reachable_from_the_root()
{
    // The composite (family_tree_id, tenant_id) FK should make a stray row impossible, so this
    // guards the migration's literal values rather than the constraint itself.
    var view = await Client.GetFromJsonAsync<FamilyTreeView>("/api/v1/family-tree/view");

    Assert.Equal(ExpectedCountFromArtifact, Count(view!.RootMembers));
}
```

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL, count mismatch against the demo seed.

- [ ] **Step 3: Generate the migration from the JSON, escaping single quotes in names**

- [ ] **Step 4: Run the full backend suite**

Run: `dotnet test`
Expected: PASS, including the existing 164 tests. Several integration tests assert against the
demo family; update those to the imported data rather than preserving the demo set.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat: seed the imported family tree"
```

---

### Task 8: Verify against the running application

**Files:** none — this is a verification task. Findings go in this plan's closing notes.

- [ ] **Step 1: Start the stack and confirm the tree loads**

```bash
dotnet run --project src/FamilyTree.Api --no-launch-profile --urls http://localhost:5000
cd frontend && npm run dev
```

- [ ] **Step 2: Confirm counts in the UI**

The header stat line should report the imported member and generation counts. Expand to the
deepest generation. **Record how the indented outline behaves at ~349 nodes — that measurement
is the input to Phase 3's virtualization work.**

- [ ] **Step 3: Search a repeated name**

Search the most frequent name from Task 6's summary. Search is currently client-side and its
results show only "Generation N" as meta. With many identical names those results will be
genuinely ambiguous — **this is the concrete demonstration of why §5.4 makes the ancestor path
required rather than decorative**, and it is the motivating evidence for Phase 3.

- [ ] **Step 4: Record findings and commit**

```bash
git commit -am "docs: record import verification findings"
```

---

## Self-review

**Spec coverage.** §7.2's six steps map to tasks: step 1 → Tasks 1 and 3, step 2 → Tasks 4
and 5, step 3 → Task 5, step 4 → Task 5, step 5 → Task 6, step 6 → Task 7. §8's "seed
migration" is Task 7.

**Deliberate deviations.**
1. **No PDF library.** §7.1 says the content stream is directly extractable, and
   characterisation confirmed it. A library would add a dependency and still leave the same
   custom geometry work, since node boxes are drawn paths rather than annotations.
2. **Path classification is decided empirically in Task 4** rather than specified here. Two
   signature families both sit near 349; asserting one in the plan would be a guess dressed as
   a requirement.
3. **The demo family is replaced, not merged** (Task 7) — stated explicitly so the reviewer can
   overrule it at the Task 6 gate, before anything is destroyed.

**Open risk.** Two questions remain genuinely unresolved: whether every glyph lands in exactly
one box (Task 4) and whether the edge graph closes into a single tree (Task 5). Both carry
explicit stop-and-report instructions rather than assertions to loosen.

---

## Task 8 — verification findings (recorded 2026-08-17)

Verified against the running stack (API on :5000, Vite on :5174), signed in as the seeded admin.

**Import landed intact.**

| Check | Result |
|---|---|
| Members via `GET /api/v1/family-members` | 349 |
| `GET /api/v1/family-tree/view` | 349 nodes, single root داوود, 10 generations |
| Header stat line | `349 فرداً · 10 أجيال` — correct Arabic plural categories (`many` for 349, `few` for 10) |
| Most frequent names | محمد ×39, أحمد ×18, محمود ×10, خالد ×8 |
| Deep chain renders | داوود → سلمان → أمد → علي → أحمد → عايش → أكرم → خالد |

The two source typos (`ممد`, `أمد`) are present as imported, per the confirmed decision to reproduce
the PDF faithfully.

### Input to Phase 3

**1. No virtualization — all 349 rows are in the DOM at once.** Expanding every branch puts
`document.querySelectorAll('[role="treeitem"]').length === 349` simultaneously. Design spec §5.4
requires "rendering is viewport-virtualized: only nodes whose projected bounds intersect the visible
rectangle plus a margin are in the DOM." At 349 nodes the outline stays responsive, so this is not
urgent — but the requirement is unimplemented and the measurement is now real rather than assumed.

**2. Search results are capped at 8, and the count label reports the cap rather than the truth.**
`flattenTree.ts` `searchNodes(..., limit = 8)` slices matches, and `AppShell` renders
`t('tree.resultCount', { count: results.length })` — the *displayed* count. Searching محمد therefore
reads "8 نتائج" when 39 members match. The label states a falsehood. Two ways out, both Phase 3's to
choose: report the true total and label the list as a preview ("showing 8 of 39"), or let the
server-side search endpoint return a total alongside a page of hits.

**3. The ancestor-path requirement is now demonstrated, not argued.** Searching محمد returns rows
distinguished only by "الجيل 8 / الجيل 9 / الجيل 8 / الجيل 8" — four entries, three of them
identical in every visible respect. Design spec §5.4 calls the ancestor path "required rather than
decorative"; with 39 identical names and generation as the only discriminator, a user cannot pick
the right person. This is the concrete case Phase 3 must solve, and it only became visible with real
data — which is exactly why §8 sequences this import before Phase 3.
