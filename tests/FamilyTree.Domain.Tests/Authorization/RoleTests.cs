using FluentAssertions;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Tests.Authorization;

public class RoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Fact]
    public void Create_makes_a_tenant_scoped_custom_role()
    {
        var role = Role.Create(TenantId, "Genealogy Editor", "Can edit members", Now);

        role.TenantId.Should().Be(TenantId);
        role.Name.Should().Be("Genealogy Editor");
        role.Description.Should().Be("Can edit members");
        role.IsSystem.Should().BeFalse();
    }

    [Fact]
    public void CreateSystem_marks_the_role_as_system_owned()
    {
        var role = Role.CreateSystem(TenantId, "Super Admin", "All permissions", Now);

        role.IsSystem.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => Role.Create(TenantId, name, null, Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("ROLE_NAME_REQUIRED");
    }

    [Fact]
    public void EnsureNotSystem_rejects_modifying_a_system_role()
    {
        var role = Role.CreateSystem(TenantId, "Super Admin", null, Now);

        var act = role.EnsureNotSystem;

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("ROLE_IS_SYSTEM");
    }

    [Fact]
    public void EnsureNotSystem_allows_modifying_a_custom_role()
    {
        var role = Role.Create(TenantId, "Genealogy Editor", null, Now);

        var act = role.EnsureNotSystem;

        act.Should().NotThrow();
    }

    [Fact]
    public void Rename_rejects_renaming_a_system_role()
    {
        var role = Role.CreateSystem(TenantId, "Viewer", null, Now);

        var act = () => role.Rename("Something Else", Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("ROLE_IS_SYSTEM");
    }
}
