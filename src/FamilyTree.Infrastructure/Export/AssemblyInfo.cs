using System.Runtime.CompilerServices;

// The caption's internal layout geometry (SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting)
// is exposed as internal, not public, and verified directly by tests rather than by parsing
// rendered PDF bytes -- Round-2 review noted /ToUnicode is hand-built from the source string, so
// text extraction cannot prove glyph *position* (or, in general, glyph identity) at all.
//
// Round-2's version of this comment claimed these were "the exact numbers DrawCaption draws
// with; there is no second, divergent computation" -- that was false: the seam accepted a
// caller-supplied pageWidth while production's capped sheet path resolved the layout against a
// fixed CaptionMaxWidth, so the two could (and did) diverge for the same caption (Round-3
// review, finding 2). The seam now calls SkiaTreeRenderer.ResolveLayoutForFormat -- the same
// method Render calls -- so there genuinely is only one resolution per (caption, format); see
// ResolveLayoutForFormat and ComputeCaptionRunPositionsForTesting for exactly what is, and is
// not, guaranteed to match an actual rendered page.
[assembly: InternalsVisibleTo("FamilyTree.Application.Tests")]
