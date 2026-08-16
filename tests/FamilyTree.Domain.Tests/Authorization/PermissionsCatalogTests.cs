using FluentAssertions;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Domain.Tests.Authorization;

public class PermissionsCatalogTests
{
    [Fact]
    public void All_contains_every_permission_from_the_specification()
    {
        Permissions.All.Should().BeEquivalentTo(new[]
        {
            "FamilyTree.View", "FamilyTree.Edit",
            "Member.View", "Member.Create", "Member.Edit", "Member.Move", "Member.Delete",
            "User.View", "User.Create", "User.Edit", "User.Deactivate",
            "Role.View", "Role.Create", "Role.Edit", "Role.Delete",
            "Audit.View",
            "PublicLink.Create", "PublicLink.Revoke"
        });
    }

    [Fact]
    public void All_contains_no_duplicates()
    {
        Permissions.All.Should().OnlyHaveUniqueItems();
    }
}
