namespace FamilyTree.Application.Auth;

/// <summary>
/// The single definition of the password-length floor. Api and Infrastructure both enforce it
/// — the administrator reset path and the self-service change path must agree, and a value
/// duplicated in two layers drifts silently.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;
}
