namespace FamilyTree.Contracts.Roles;

/// <summary>
/// Permissions are sent as codes and replaced wholesale, matching how the role editor presents
/// them: a set of checkboxes whose final state is the request.
/// </summary>
public sealed record SaveRoleRequest(
    string Name, string? Description, IReadOnlyList<string> Permissions);
