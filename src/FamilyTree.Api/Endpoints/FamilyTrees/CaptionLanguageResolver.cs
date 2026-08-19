using FamilyTree.Application.Export;

namespace FamilyTree.Api.Endpoints.FamilyTrees;

/// <summary>
/// Picks the export caption's language from the request's <c>Accept-Language</c> header (design
/// §4.6). There is no server-side localisation infrastructure in this codebase -- i18n is
/// frontend-only (frontend/src/i18n, SUPPORTED_LANGUAGES = ['ar', 'en'], default 'ar') -- so
/// this mirrors that default rather than building a negotiation framework for one caption.
/// </summary>
public static class CaptionLanguageResolver
{
    public static CaptionLanguage Resolve(HttpRequest request)
    {
        var preferences = request.GetTypedHeaders().AcceptLanguage;
        if (preferences is null || preferences.Count == 0) return CaptionLanguage.Ar;

        // q=0 means "explicitly not acceptable" (RFC 9110 §12.5.1), so it is filtered out here
        // rather than treated like an unweighted (implicit q=1) entry. The remaining entries are
        // walked highest-quality-first, and the first one that names a language we actually have
        // (en or ar) wins -- an unknown language ahead of a known one (e.g. "de,en;q=0.9") must
        // fall through to that known one, not fall all the way back to the Ar default.
        var acceptable = preferences.Where(p => (p.Quality ?? 1.0) > 0)
            .OrderByDescending(p => p.Quality ?? 1.0);

        foreach (var preference in acceptable)
        {
            var tag = preference.Value.Value;
            if (tag is null) continue;

            if (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return CaptionLanguage.En;
            if (tag.StartsWith("ar", StringComparison.OrdinalIgnoreCase)) return CaptionLanguage.Ar;
        }

        return CaptionLanguage.Ar;
    }
}
