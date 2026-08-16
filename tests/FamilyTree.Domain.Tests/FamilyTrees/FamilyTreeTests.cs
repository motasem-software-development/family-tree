using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;

namespace FamilyTree.Domain.Tests.FamilyTrees;

public class FamilyTreeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Fact]
    public void Create_binds_the_tree_to_its_tenant_and_activates_it()
    {
        var tree = FamilyTreeAggregate.Create(TenantId, "عائلة السقا", Now);

        tree.Id.Should().NotBeEmpty();
        tree.TenantId.Should().Be(TenantId);
        tree.Name.Should().Be("عائلة السقا");
        tree.IsActive.Should().BeTrue();
        tree.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_id()
    {
        var act = () => FamilyTreeAggregate.Create(Guid.Empty, "Al-Saqqa Family", Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("FAMILY_TREE_TENANT_REQUIRED");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => FamilyTreeAggregate.Create(TenantId, name, Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("FAMILY_TREE_NAME_REQUIRED");
    }

    [Fact]
    public void Create_rejects_a_name_longer_than_200_characters()
    {
        var act = () => FamilyTreeAggregate.Create(TenantId, new string('x', 201), Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("FAMILY_TREE_NAME_TOO_LONG");
    }

    [Fact]
    public void Rename_changes_the_root_family_name_without_changing_the_tenant()
    {
        var tree = FamilyTreeAggregate.Create(TenantId, "Old Family", Now);
        var later = Now.AddDays(1);

        tree.Rename("عائلة السقا", later);

        tree.Name.Should().Be("عائلة السقا");
        tree.TenantId.Should().Be(TenantId);
        tree.UpdatedAt.Should().Be(later);
    }
}
