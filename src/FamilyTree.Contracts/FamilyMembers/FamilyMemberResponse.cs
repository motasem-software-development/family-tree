namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// A single member as returned by the API. <paramref name="Version"/> must be echoed back on
/// update — it is the optimistic concurrency token (design spec §3.1).
/// </summary>
public sealed record FamilyMemberResponse(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateOnly? DateOfBirth,
    DateOnly? DateOfDeath,
    bool IsDeceased);
