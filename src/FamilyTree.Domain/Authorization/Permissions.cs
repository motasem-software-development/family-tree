namespace FamilyTree.Domain.Authorization;

/// <summary>
/// The permission catalog from SRS §21. Authorization is evaluated against these codes,
/// never against role names — that is what makes custom roles possible.
/// Adding a capability means adding a constant here plus a seed row; no handler changes.
/// </summary>
public static class Permissions
{
    public static class FamilyTree
    {
        public const string View = "FamilyTree.View";
        public const string Edit = "FamilyTree.Edit";
    }

    public static class Member
    {
        public const string View = "Member.View";
        public const string Create = "Member.Create";
        public const string Edit = "Member.Edit";
        public const string Move = "Member.Move";
        public const string Delete = "Member.Delete";
    }

    public static class User
    {
        public const string View = "User.View";
        public const string Create = "User.Create";
        public const string Edit = "User.Edit";
        public const string Deactivate = "User.Deactivate";
    }

    public static class Role
    {
        public const string View = "Role.View";
        public const string Create = "Role.Create";
        public const string Edit = "Role.Edit";
        public const string Delete = "Role.Delete";
    }

    public static class Audit
    {
        public const string View = "Audit.View";
    }

    public static class PublicLink
    {
        public const string Create = "PublicLink.Create";
        public const string Revoke = "PublicLink.Revoke";
    }

    public static IReadOnlyList<string> All { get; } =
    [
        FamilyTree.View, FamilyTree.Edit,
        Member.View, Member.Create, Member.Edit, Member.Move, Member.Delete,
        User.View, User.Create, User.Edit, User.Deactivate,
        Role.View, Role.Create, Role.Edit, Role.Delete,
        Audit.View,
        PublicLink.Create, PublicLink.Revoke
    ];
}
