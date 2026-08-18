namespace FamilyTree.Contracts.Roles;

/// <summary>
/// Permissions are codes, not ids: the catalog is global and stable, and codes are what the
/// frontend already reasons about (it holds them as claims). UserCount lets the UI warn before
/// a change that affects people.
/// </summary>
public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    int UserCount,
    IReadOnlyList<string> Permissions);
