using FamilyTree.Api.Endpoints.FamilyTrees;
using FamilyTree.Application.Export;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

/// <summary>
/// Pure function of the request's Accept-Language header (design §4.6) -- no database, no
/// PostgresFixture, so this runs without Docker unlike the rest of this test project.
/// </summary>
public sealed class CaptionLanguageResolverTests
{
    private static HttpRequest RequestWithAcceptLanguage(string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
            context.Request.Headers.AcceptLanguage = headerValue;

        return context.Request;
    }

    [Fact]
    public void No_header_defaults_to_arabic()
    {
        CaptionLanguageResolver.Resolve(RequestWithAcceptLanguage(null)).Should().Be(CaptionLanguage.Ar);
    }

    [Fact]
    public void An_english_preference_resolves_to_english()
    {
        CaptionLanguageResolver.Resolve(RequestWithAcceptLanguage("en-US,en;q=0.9"))
            .Should().Be(CaptionLanguage.En);
    }

    [Fact]
    public void An_arabic_preference_resolves_to_arabic()
    {
        CaptionLanguageResolver.Resolve(RequestWithAcceptLanguage("ar,en;q=0.5"))
            .Should().Be(CaptionLanguage.Ar);
    }

    [Fact]
    public void An_unsupported_language_falls_back_to_arabic()
    {
        CaptionLanguageResolver.Resolve(RequestWithAcceptLanguage("fr-FR,fr;q=0.9"))
            .Should().Be(CaptionLanguage.Ar);
    }

    [Fact]
    public void The_highest_quality_preference_wins_over_declaration_order()
    {
        CaptionLanguageResolver.Resolve(RequestWithAcceptLanguage("ar;q=0.3,en;q=0.9"))
            .Should().Be(CaptionLanguage.En);
    }

    // RFC 9110 §12.5.1: q=0 means "not acceptable", not "lowest priority" -- it must not be
    // treated as a usable preference at all.
    [Fact]
    public void A_zero_quality_preference_is_not_acceptable_and_falls_back_to_arabic()
    {
        CaptionLanguageResolver.Resolve(RequestWithAcceptLanguage("en;q=0"))
            .Should().Be(CaptionLanguage.Ar);
    }

    // An unknown language ahead of a known one must not sink the whole header to the Ar
    // default -- the resolver should fall through to the next KNOWN language in quality order.
    [Fact]
    public void An_unknown_top_preference_falls_through_to_the_next_known_language()
    {
        CaptionLanguageResolver.Resolve(RequestWithAcceptLanguage("de,en;q=0.9"))
            .Should().Be(CaptionLanguage.En);
    }
}
