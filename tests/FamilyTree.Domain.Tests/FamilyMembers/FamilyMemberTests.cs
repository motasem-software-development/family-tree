using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Domain.Tests.FamilyMembers;

public class FamilyMemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    [Fact]
    public void Create_makes_a_first_generation_member_when_no_parent_is_given()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);

        member.Id.Should().NotBeEmpty();
        member.TenantId.Should().Be(TenantId);
        member.FamilyTreeId.Should().Be(TreeId);
        member.ParentId.Should().BeNull();
        member.Name.Should().Be("سليمان");
        member.CreatedAt.Should().Be(Now);
        member.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_links_a_descendant_to_its_parent()
    {
        var parent = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);

        var child = FamilyMember.Create(TenantId, TreeId, parent.Id, "فارس", Now);

        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public void Create_starts_the_concurrency_version_at_one()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);

        member.Version.Should().Be(1);
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_id()
    {
        var act = () => FamilyMember.Create(Guid.Empty, TreeId, null, "سليمان", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_TENANT_REQUIRED");
    }

    [Fact]
    public void Create_rejects_an_empty_family_tree_id()
    {
        var act = () => FamilyMember.Create(TenantId, Guid.Empty, null, "سليمان", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_TREE_REQUIRED");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => FamilyMember.Create(TenantId, TreeId, null, name, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_NAME_REQUIRED");
    }

    [Fact]
    public void Create_rejects_a_name_longer_than_200_characters()
    {
        var act = () => FamilyMember.Create(TenantId, TreeId, null, new string('x', 201), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_NAME_TOO_LONG");
    }

    [Fact]
    public void Create_trims_surrounding_whitespace_from_the_name()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "  فارس  ", Now);

        member.Name.Should().Be("فارس");
    }

    [Fact]
    public void Create_accepts_an_empty_parent_id_as_no_parent()
    {
        // Guid.Empty arriving from a caller means "no parent", not "a parent whose id is zero".
        var member = FamilyMember.Create(TenantId, TreeId, Guid.Empty, "سليمان", Now);

        member.ParentId.Should().BeNull();
    }

    [Fact]
    public void Rename_changes_the_name_and_advances_the_version()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);
        var later = Now.AddDays(1);

        member.Rename("فارس أحمد", later);

        member.Name.Should().Be("فارس أحمد");
        member.Version.Should().Be(2);
        member.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Rename_does_not_change_the_tenant_the_tree_or_the_parent()
    {
        var parent = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);
        var child = FamilyMember.Create(TenantId, TreeId, parent.Id, "فارس", Now);

        child.Rename("فارس أحمد", Now.AddDays(1));

        child.TenantId.Should().Be(TenantId);
        child.FamilyTreeId.Should().Be(TreeId);
        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public void Rename_applies_the_same_name_rules_as_create()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);

        var act = () => member.Rename("   ", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_NAME_REQUIRED");
    }

    [Fact]
    public void Rename_does_not_advance_the_version_when_validation_fails()
    {
        // A rejected rename must leave the entity untouched, or a client that retries
        // after a validation error would find its version stale for no reason.
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);

        var act = () => member.Rename("", Now);

        act.Should().Throw<DomainException>();
        member.Version.Should().Be(1);
        member.Name.Should().Be("فارس");
    }
}
