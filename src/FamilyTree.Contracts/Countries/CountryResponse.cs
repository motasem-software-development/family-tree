namespace FamilyTree.Contracts.Countries;

/// <summary>
/// One country of residence. Both names ship on every row rather than one resolved server-side:
/// the client switches language without refetching, and the same cached response serves both.
///
/// No flag field — the client derives the emoji from <paramref name="Code"/> by
/// regional-indicator arithmetic (design §2.1).
/// </summary>
public sealed record CountryResponse(
    int Id,
    string Code,
    string NameAr,
    string NameEn,
    string DialCode);
