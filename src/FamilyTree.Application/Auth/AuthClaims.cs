namespace FamilyTree.Application.Auth;

/// <summary>
/// Single source of truth for custom claim type names. Both the token issuer
/// (JwtTokenService) and the token consumer (HttpTenantContext) must agree on
/// these literals — a drift here silently collapses tenant isolation to
/// <see cref="Guid.Empty"/> with no compile error, so both sides reference this
/// class instead of redeclaring the string.
/// </summary>
public static class AuthClaims
{
    public const string TenantId = "tenant_id";
    public const string Permission = "permission";
    public const string MustChangePassword = "must_change_password";
}
