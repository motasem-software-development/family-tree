namespace FamilyTree.Domain.Common;

public class DomainException(string code, string message) : Exception(message)
{
    /// <summary>Stable machine-readable code. Surfaces in Problem Details; clients translate from it.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// The requested entity does not exist *for this caller*. Deliberately indistinguishable from
/// "belongs to another tenant" — a 403 would confirm the identifier exists (design spec §4.4).
/// </summary>
public sealed class NotFoundException(string code, string message) : DomainException(code, message);

/// <summary>A rule that depends on current state, not on the request: 409 rather than 400.</summary>
public sealed class ConflictException(string code, string message) : DomainException(code, message);

/// <summary>
/// The request is well-formed but the result would exceed a hard limit. Carries a
/// <see cref="Reason"/> because only some causes have a remedy the caller can act on, and a
/// client must not offer the wrong one (design §5.3).
/// </summary>
public sealed class TooLargeException(string code, string message, string reason)
    : DomainException(code, message)
{
    public string Reason { get; } = reason;
}
