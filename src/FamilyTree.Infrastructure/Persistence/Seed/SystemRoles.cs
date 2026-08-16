using FamilyTree.Domain.Authorization;

namespace FamilyTree.Infrastructure.Persistence.Seed;

/// <summary>
/// The four predefined roles from SRS §19, expressed as permission sets rather than as
/// hard-coded role checks. Custom roles created later sit alongside these as equals.
/// </summary>
public static class SystemRoles
{
    public const string SuperAdmin = "Super Admin";
    public const string Administrator = "Administrator";
    public const string Editor = "Editor";
    public const string Viewer = "Viewer";

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Definitions { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [SuperAdmin] = Permissions.All,

            [Administrator] =
            [
                Permissions.FamilyTree.View, Permissions.FamilyTree.Edit,
                Permissions.Member.View, Permissions.Member.Create, Permissions.Member.Edit,
                Permissions.Member.Move, Permissions.Member.Delete,
                Permissions.User.View, Permissions.User.Create, Permissions.User.Edit,
                Permissions.User.Deactivate,
                Permissions.Role.View,
                Permissions.Audit.View,
                Permissions.PublicLink.Create, Permissions.PublicLink.Revoke
            ],

            [Editor] =
            [
                Permissions.FamilyTree.View,
                Permissions.Member.View, Permissions.Member.Create,
                Permissions.Member.Edit, Permissions.Member.Move
            ],

            [Viewer] =
            [
                Permissions.FamilyTree.View,
                Permissions.Member.View
            ]
        };
}
