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

        var top = preferences.OrderByDescending(p => p.Quality ?? 1.0).FirstOrDefault();

        return top?.Value.Value?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true
            ? CaptionLanguage.En
            : CaptionLanguage.Ar;
    }
}
