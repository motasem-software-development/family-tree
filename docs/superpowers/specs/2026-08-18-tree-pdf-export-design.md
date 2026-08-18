# Family Tree PDF Export — Design

**Date:** 2026-08-18
**Status:** Approved design, ready for implementation planning
**Reference artifact:** `familytree.pdf` (repository root) — the XMind 10 export the exported
document is modelled on, and the same file `tools/FamilyTree.Import` reconstructed the seed
data from.
**Extends:** `2026-08-16-family-tree-saas-design.md` §5 (tree visualization), §4 (API and
authorization), §6 (testing).

This document records the design for exporting a family tree as a PDF poster. Every metric
quoted from the reference was measured from the file, not estimated; the measurements are in
§3 and are the numeric contract the renderer is held to.

---

## 1. Scope

A user with `FamilyTree.View` exports the family tree as a PDF: a colour-coded diagram in the
style of `familytree.pdf`, with correctly shaped right-to-left Arabic names, suitable for
printing and framing.

### 1.1 Decisions taken during design

| Question | Decision |
|---|---|
| Visual fidelity | Two styles, selected at export time: `xmind` (replicates the reference) and `clean` (a designed layout with title page and legend). |
| Page format | Two formats, selected at export time: `sheet` (one page sized to the tree) and `a4` (tiled, printable anywhere). |
| Primary combination | `xmind` + `sheet`. It is designed in full detail here and implemented first; the other three are variations over the same scene. |
| Renderer | SkiaSharp + HarfBuzzSharp, drawing directly to `SKDocument.CreatePdf`. §4.1 records the alternatives and why they lost. |
| Centre node | The export centres the requested `rootId`, defaulting to the family tree's stored root. §2.3 explains why this matters. |
| Delivery | Synchronous streamed response. No job queue in V1. |
| Permission | Existing `FamilyTree.View`. No new permission code. |
| Fonts | Noto Sans Arabic Bold (Arabic) and Noto Sans Bold (Latin), both SIL OFL, embedded as resources. |

### 1.2 Out of scope

- PNG, SVG, or print-to-clipboard export. The layout engine makes these cheap later; nothing
  here is designed to prevent them, and nothing here is designed to enable them.
- Exporting a filtered or searched subset. The export takes a centre node and depth, not a
  query.
- Correcting the imported hierarchy discrepancy described in §2.3. That is a data question,
  recorded here as an observation only.
- Server-side caching of rendered PDFs.

---

## 2. Reference analysis

### 2.1 What the reference is

A single-page XMind 10 export, 1182 × 3709.9 pt. One centre node, four top-level branches,
349 nodes, 219 leaves. Names are Arabic, right-to-left, rendered with contextual shaping. The
document carries a `/ToUnicode` CMap, so its text is selectable and searchable — this is how
`tools/FamilyTree.Import` recovered the names.

### 2.2 Structure

The centre node sits at x ≈ 579, y ≈ 2182 — 59% down the page, **not** centred. One branch
extends right with increasing x; three extend left, mirrored. The centre's vertical position
is whatever balances the two masses, and falls out of the layout algorithm rather than being
a constant (§4.3, pass 5).

Only five nodes are drawn as rounded rectangles: the centre and the four top-level children.
The remaining 344 are a label sitting on a short horizontal tick, where the tick is the final
segment of the connector arriving from the parent. This 5 + 344 split is what
`tools/FamilyTree.Import/Geometry.cs` independently found when classifying path operators,
and it is a useful cross-check that this reading of the document is correct.

Connectors are of two kinds. Centre → top-level children are **filled** tapered ribbons —
thick at the centre, tapering to the child — drawn as closed bézier paths, not strokes.
Level 2 and deeper are **stroked** orthogonal elbows with rounded corners.

### 2.3 The centre node is not the stored root

The reference centres **سليمان**. The seeded data in `docs/import/family-tree.json` roots at
**داوود**, whose children are طالب (subtree 1), محمود (3), سليمان (93), and سلمان (251) — so
the reference's centre is stored as a child of the reference's blue branch.

The reconstruction had to infer edge direction from geometry; the import artifact records this
in its `"orientation": "connector-start-is-parent"` field, and §7.2 of the V1 design notes
that four of 348 edges carried no recoverable direction at all. Whether the stored hierarchy
or the reference's centring is genealogically correct is **not decided here**.

The export does not need that question answered. XMind's centre node is a display choice, not
a claim about ancestry, and the existing `GET /api/v1/family-tree/view` already accepts a
`rootId`. The export inherits the same parameter: centre on the requested member, default to
the stored root. Pointed at سليمان it reproduces the reference; left alone it exports the
genealogical whole.

This is recorded so that a future reader who compares an export against `familytree.pdf` and
finds a different name in the middle understands why, and does not treat it as a rendering
defect.

---

## 3. Measured metrics

Measured from `familytree.pdf`. These are the values the `xmind` style is implemented against
and asserted on.

### 3.1 Palette

| Role | Hex | Use |
|---|---|---|
| Branch 1 | `#518CD8` | blue |
| Branch 2 | `#FD6D5A` | coral |
| Branch 3 | `#6DC354` | green |
| Branch 4 | `#FEB40B` | amber |
| Centre | `#8793A5` | centre box stroke only |

Colour binds to **top-level branch index**, and every descendant inherits its branch's hue.
That inheritance is what makes the diagram readable at a glance and is a hard requirement, not
a stylistic preference.

The reference has exactly four branches. The palette extends to twelve hues — these four
first, in this order, then eight more chosen under two constraints: each must stay
distinguishable from its neighbours at the 13.34 pt body size, and each must remain distinct
from the others when converted to greyscale, since these documents get printed on mono
printers. Beyond twelve the palette cycles. A tree with more than twelve top-level children
repeats colours; the diagram remains correct because hue is a reading aid, not an identifier.

### 3.2 Geometry

| Property | Value |
|---|---|
| Page | 1182 × 3709.9 pt |
| Centre box | 107.9 × 61.1 pt, white fill, `#8793A5` stroke @ 2.22 pt |
| Level-1 box | ≈ 55.5 × 40.7 pt, white fill, branch-colour stroke @ 1.48 pt |
| Connector stroke | 1.48 pt |
| Leaf row pitch | ≈ 15.0 pt (median of 550 distinct baselines spanning 3642 pt) |
| Sibling-group separation | ≈ 29.5 pt |
| Column pitch | 50–69 pt — content-driven, see §4.3 pass 3 |
| Font size, centre | 26.68 pt |
| Font size, level 1 | 17.78 pt |
| Font size, body | 13.34 pt |

The row pitch is bimodal: ~15 pt between adjacent leaves, ~29.5 pt where one sibling group
ends and the next begins. A single constant pitch does not reproduce the reference's rhythm.

Column pitch varies because each depth's column is sized to the widest label at that depth
within its branch. It is not a fixed indent.

### 3.3 Fonts

The reference uses Arial-BoldMT for Arabic and OpenSans-Bold for Latin. Neither ships: Arial
is proprietary, and Open Sans has no Arabic coverage.

**Noto Sans Arabic Bold** (SIL OFL) is the substitute — freely embeddable and metrically close
to Arial Bold's Arabic, so the reference's column and row proportions survive. **Noto Sans
Bold** (SIL OFL) covers Latin and digits in the caption.

Amiri was considered and rejected for the default: it is a more beautiful classical naskh, but
noticeably wider, which shifts every measurement in §3.2. It remains available as a future
style option — the font is a `LayoutOptions` field, not a constant.

---

## 4. Architecture

### 4.1 Renderer choice

**Chosen: SkiaSharp + HarfBuzzSharp**, drawing to `SKDocument.CreatePdf`.

A mindmap poster needs no document flow engine. There is no text wrapping, no tables, and no
widow/orphan handling — only "draw this shaped run at (x, y)" and "stroke this path". That is
Skia's native job. The A4 format becomes a translate-and-clip loop over the same drawn scene
rather than a second layout system. Fonts embed and subset automatically, Skia's PDF backend
emits `/ToUnicode` so text stays searchable, and SkiaSharp and HarfBuzzSharp are both MIT.

**QuestPDF** was the strong alternative. Current releases support RTL and Arabic shaping, and
its `ContinuousSize` mode is exactly the `sheet` format — including the same 14,400 pt ceiling.
It would earn its keep on the `a4` variant's title page and furniture. It lost on two counts:
it is SkiaSharp + HarfBuzz underneath, so on the primary combination it is an abstraction over
a canvas we would rather address directly; and its Community licence is free only under USD 1M
annual gross revenue, excludes public-sector and publicly traded entities, and would become a
recurring compliance question for a multi-tenant SaaS.

**Headless Chromium** (print-to-PDF over HTML/SVG) was rejected. Shaping and font fallback come
free, but it adds a browser to the API image, costs a process per export, and hands typographic
control to a renderer we cannot pin. For a document whose entire value is precise visual
design, that is the wrong trade.

The layout engine is renderer-agnostic, so if measured Arabic output disappoints, swapping to
QuestPDF replaces exactly one class.

### 4.2 Modules

Three units. The dependency arrow points away from the PDF, consistent with the V1 design's
inward-pointing rule.

**`FamilyTree.Application/Export/TreeLayout`** — pure geometry, no I/O, **no SkiaSharp
reference**. Input: the tree, a `LayoutOptions` (metrics, palette, font sizes, style), and a
text-measurement delegate. Output: an immutable `TreeScene` — positioned nodes, connector paths
as control points, branch colours, bounding box.

Text measurement enters as an injected `Func<string, float, float>` (label, font size → width)
because shaped-text width is a font fact and `Application` may not reference Skia. Layout tests
run against a stub metric with no font or native binary loaded.

**`FamilyTree.Infrastructure/Export/SkiaTreeRenderer`** — takes a `TreeScene`, draws it. Owns
SkiaSharp, HarfBuzz, the embedded fonts, and `SKDocument.CreatePdf`. It also *supplies* the
measurement delegate the layout engine consumes. It makes no layout decisions.

**`FamilyTree.Api/Endpoints/FamilyTrees`** — one endpoint, streams bytes.

`TreeScene` is the critical seam. Being plain data, it can be asserted coordinate-by-coordinate
in tests without producing a PDF.

The two styles are `ILayoutStrategy` implementations inside the layout unit. The two page
formats are paginators that tile one `TreeScene`. Neither multiplies the other — this is what
keeps 2 × 2 from becoming four independent designs.

### 4.3 Layout algorithm

Five passes, each pure.

**Pass 1 — Measure.** Each label is shaped once and measured at its depth's font size, giving
a box width. The only pass that touches the metric delegate.

**Pass 2 — Vertical packing** (bottom-up). A leaf occupies one row of the leaf pitch. Sibling
groups are separated by the wider group gap (§3.2). An internal node's height is the sum of its
children's heights; its centre is **the midpoint between its first and last child's centres**,
not their arithmetic mean. This distinction is what produces the reference's characteristic
look, where a parent of a lopsided subtree drifts toward the dense side.

**Pass 3 — Column assignment.** Within a top-level branch, all nodes at the same depth share
one x. Column width is the widest label at that depth plus a gap, which is why the reference's
pitch varies between 50 and 69 pt. Columns are per-branch, so a wide name in one branch does
not push another branch outward.

**Pass 4 — Side assignment.** Top-level children are sorted by packed height, largest first,
and each is assigned to whichever side is currently lighter. Sibling order is preserved within
a side. On the real data this reproduces the reference: the heaviest branch alone on the right,
the other three stacked on the left, the two masses within a few percent.

The right half grows with increasing x; the left half mirrors it. A node's tick runs outward
from its incoming connector and the label sits on it, anchored at the tick's inner end. Glyph
ordering within a label is HarfBuzz's concern; the anchor rule is direction-agnostic.

**Pass 5 — Centre placement and normalisation.** The centre is placed against the combined
vertical extent of both sides, then the scene is translated so its bounding box starts at the
margin.

**Forests.** The API returns `rootMembers` as a collection. When a tree has more than one root,
a synthetic invisible centre holds them and each real root becomes a top-level branch with its
own colour. No root is silently dropped.

**Connectors.** Centre → level 1 is a filled tapered ribbon: two cubic béziers closed into a
teardrop, thick at the centre, tapering to the child. Level 2 and deeper are stroked orthogonal
elbows at 1.48 pt with a **6 pt corner radius**, whose final horizontal run is the tick beneath
the label. Junction points carry the small dot markers the reference draws.

### 4.4 Overflow

The PDF format caps a page dimension at 14,400 units (200 in); QuestPDF documents the same
ceiling for its continuous-size mode. At the measured pitch this is roughly 865 leaves — about 1,400 members — so the seeded tree
(219 leaves, 3,642 pt) sits comfortably inside it.

Past the ceiling the engine scales the whole scene uniformly to fit, down to a **6 pt font
floor**. Below that floor it does not render: it fails with `EXPORT_TREE_TOO_LARGE`, whose
message directs the caller to the `a4` format. Emitting an invalid or illegible page is the one
outcome explicitly ruled out.

### 4.5 A4 pagination

The same `TreeScene`, tiled across 595 × 842 pt pages in reading order for the document's
direction. An 18 pt bleed on each cut means a connector crossing a boundary is visible on both
sheets. A cut is moved to the nearest sibling-group gap within **±40 pt** of the nominal
boundary; if no gap falls in that window the cut stays put, and the bleed guarantees no label
is sliced. The title page carries the family tree name, the caption data, and a
page-grid map; each cut carries a "‏يتبع ص N" continuation marker.

### 4.6 Furniture

The reference is a bare diagram. The export adds a restrained caption in the bottom margin:
family tree name, member count, generation count, export date. A printed copy is then
self-identifying. The caption is localised; the diagram itself is not, since the names are the
data.

---

## 5. API

### 5.1 Endpoint

```
GET /api/v1/family-tree/export.pdf?rootId=<guid>&style=<xmind|clean>&page=<sheet|a4>
```

Requires `FamilyTree.View`. All query parameters are optional; defaults are the stored root,
`xmind`, and `sheet`.

No new permission is introduced. The export reveals exactly the data `/family-tree/view`
already returns, so a separate `Export` permission would add a lockout surface — one more thing
the last-administrator guard would have to reason about — without adding protection.

Response is `application/pdf`, streamed, with `Content-Disposition: attachment` and an
RFC 5987‑encoded `filename*` so Arabic family names survive the header.

### 5.2 Delivery model

Synchronous. At 349 members a Skia render is well under a second; a job queue would be
infrastructure without a customer. The assumption is recorded here so it is revisited
deliberately rather than rediscovered under load.

Two guardrails. A **10,000-member cap**, above which the request is rejected with
`EXPORT_TREE_TOO_LARGE` rather than rendered — comfortably above the ~1,400 members a single
sheet can hold (§4.4) and above any plausible V1 tenant, so it is a runaway guard, not a
product limit. And a **semaphore of 2 concurrent renders** process-wide, with requests queued
behind it. Rendering is CPU-bound, and a multi-tenant API must not let one tenant's repeated
exports starve request threads for everyone else.

### 5.3 Errors

Consistent with V1 design §4.8 — RFC 7807 Problem Details carrying a stable `code`.

| Code | Status | Meaning |
|---|---|---|
| `MEMBER_NOT_FOUND` | 404 | `rootId` is not a member of this tenant's tree (reused) |
| `FAMILY_TREE_NOT_FOUND` | 404 | This tenant has no family tree (reused) |
| `EXPORT_TREE_TOO_LARGE` | 413 | Tree exceeds the 10,000-member cap, or cannot fit one sheet at the 6 pt floor (new) |

`EXPORT_TREE_TOO_LARGE` is the only new code. It carries a `reason` extension distinguishing
its two causes — `member-cap` and `sheet-overflow` — because only the second has a remedy the
caller can act on (`page=a4`), and a client must not offer that remedy for the first.

### 5.4 Frontend

An export control on `TreePage` opens a small dialog for style and format, then downloads the
blob. `services/apiClient.ts` currently assumes JSON responses and gains a binary-response
path — a real extension of the client, not a bypass around it.

---

## 6. Deployment

SkiaSharp requires `SkiaSharp.NativeAssets.Linux` and fontconfig in the API container. This is
a small Dockerfile change that passes every local test and then fails in the container, so it
is verified as its own step rather than assumed.

Licensing across the addition is clean: SkiaSharp and HarfBuzzSharp are MIT, the Noto fonts are
SIL OFL. No revenue thresholds and no per-seat terms to track.

---

## 7. Testing

Extends V1 design §6.

### 7.1 Layout — unit

Pure tests against a stub text metric, no font or native binary loaded:

- Parent centre is the midpoint of first and last child centres, including the lopsided case.
- Column width equals the widest label at that depth within the branch, and columns are
  independent across branches.
- Side assignment on the real seeded data produces the reference's split, with the lighter
  side's packed height at least **80%** of the heavier side's.
- Branch colour is inherited by every descendant.
- Leaf pitch and sibling-group separation are applied as two distinct values.
- Overflow scales uniformly, and rejects with `EXPORT_TREE_TOO_LARGE` at the 6 pt floor.
- A forest produces a synthetic centre and drops no root.

### 7.2 Round-trip — the primary acceptance test

`tools/FamilyTree.Import` already parses PDF content streams, path geometry, and `/ToUnicode`
CMaps, and was built to turn a diagram of exactly this shape back into a member hierarchy.

Running it over **our own export** and asserting it reconstructs the same members with the same
parent-child edges validates geometry, glyph encoding, connector direction, and text
searchability in a single test. This is a stronger criterion than any pixel comparison, and it
costs almost nothing because the reconstruction code already exists and is already tested.

A narrower companion test runs `pdftotext` over the output and asserts every name is recovered,
which pins the `/ToUnicode` guarantee specifically.

### 7.3 Integration

- 401 unauthenticated; 403 without `FamilyTree.View`.
- Correct content type, `Content-Disposition`, and RFC 5987 filename for an Arabic tree name.
- Unknown `rootId` returns `MEMBER_NOT_FOUND`.
- A tenant cannot export another tenant's tree.
- Low-DPI raster of the output compared against a committed baseline, to catch visual drift.

---

## 8. Delivery sequence

1. `TreeScene` and `LayoutOptions` types; the `xmind` layout strategy; layout unit tests.
2. `SkiaTreeRenderer` for the `sheet` format; fonts embedded; round-trip test green.
3. Endpoint, permission, errors, streaming; integration tests.
4. Container native assets verified in a running image.
5. Frontend export control and binary `apiClient` path.
6. `a4` paginator.
7. `clean` style.

Steps 1–5 deliver the approved primary combination end to end. Steps 6 and 7 are the
variations, and the seam at `TreeScene` is what lets them land without reopening earlier work.

---

## 9. Definition of done

- Exporting the seeded tree with `style=xmind&page=sheet&rootId=<سليمان>` produces a document
  whose structure matches `familytree.pdf`: four coloured branches, the heaviest on the right,
  centre box, tapered ribbons, elbow connectors, label-on-tick nodes.
- `tools/FamilyTree.Import` reconstructs the exported PDF to the same hierarchy as the source.
- All Arabic names render with correct contextual shaping and are selectable and searchable.
- The endpoint enforces `FamilyTree.View` and tenant isolation.
- The API container renders successfully, verified in a running image, not only locally.
- No new permission code; one new error code.
