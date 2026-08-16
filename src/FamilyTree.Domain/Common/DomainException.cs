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
