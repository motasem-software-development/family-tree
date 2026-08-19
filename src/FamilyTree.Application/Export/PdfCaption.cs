namespace FamilyTree.Application.Export;

/// <summary>
/// The two languages the frontend supports (frontend/src/i18n, SUPPORTED_LANGUAGES =
/// ['ar', 'en']). There is no server-side localisation infrastructure in this codebase, and one
/// caption does not justify building one (design §4.6) -- this is the whole lookup.
/// </summary>
public enum CaptionLanguage { Ar, En }

/// <summary>
/// The bottom-margin caption data (design §4.6): a restrained line identifying a printed sheet
/// that would otherwise be a bare diagram. <see cref="ExportDate"/> is a value threaded in by
/// the caller, never read from the clock inside rendering -- that is what keeps
/// <c>SkiaTreeRenderer</c> byte-deterministic for a fixed input.
/// </summary>
public sealed record PdfCaption(
    string FamilyTreeName,
    int MemberCount,
    int GenerationCount,
    DateOnly ExportDate,
    CaptionLanguage Language);
