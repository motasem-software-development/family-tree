using System.Runtime.CompilerServices;

// The caption's internal layout geometry (SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting)
// is exposed as internal, not public, and verified directly by tests rather than by parsing
// rendered PDF bytes -- Round-2 review noted /ToUnicode is hand-built from the source string, so
// text extraction cannot prove glyph *position* (or, in general, glyph identity) at all. These
// are the exact numbers DrawCaption draws with; there is no second, divergent computation.
[assembly: InternalsVisibleTo("FamilyTree.Application.Tests")]
