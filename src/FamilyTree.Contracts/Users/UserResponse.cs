namespace FamilyTree.Contracts.Users;

/// <summary>
/// Deliberately carries no password material of any kind — not the hash, not a placeholder.
/// A field that does not exist cannot be leaked by a future serialization change.
/// </summary>
public sealed record UserResponse(
    Guid Id,
    string Email,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<UserRoleSummary> Roles);
