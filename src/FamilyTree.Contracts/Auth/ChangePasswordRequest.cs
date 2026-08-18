namespace FamilyTree.Contracts.Auth;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
