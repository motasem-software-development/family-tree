using FamilyTree.Application.FamilyMembers;
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.FamilyTrees;

public class FamilyTreeAssemblerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(string name, Guid? parentId = null) =>
        FamilyMember.Create(TenantId, TreeId, parentId, name, Now);

    /// <summary>سليمان → فارس → محمود, plus a second first-generation member عمر.</summary>
    private static (FamilyMember Suleiman, FamilyMember Faris, FamilyMember Mahmoud, FamilyMember Omar)
        ThreeGenerations()
    {
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);
        var omar = Member("عمر");
        return (suleiman, faris, mahmoud, omar);
    }

    [Fact]
    public void Assemble_returns_an_empty_list_for_an_empty_tree()
    {
        FamilyTreeAssembler.Assemble([], MemberFilter.None, null).Should().BeEmpty();
    }

    [Fact]
    public void Assemble_puts_parentless_members_at_the_top_level()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None, null);

        roots.Select(n => n.Name).Should().BeEquivalentTo(["سليمان", "عمر"]);
    }

    [Fact]
    public void Assemble_nests_children_under_their_parent()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None, null);

        var suleimanNode = roots.Single(n => n.Name == "سليمان");
        var farisNode = suleimanNode.Children.Should().ContainSingle().Subject;
        farisNode.Name.Should().Be("فارس");
        farisNode.Children.Should().ContainSingle().Which.Name.Should().Be("محمود");
    }

    [Fact]
    public void Assemble_numbers_the_first_generation_from_one()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None, null);

        var suleimanNode = roots.Single(n => n.Name == "سليمان");
        suleimanNode.Generation.Should().Be(1);
        suleimanNode.Children[0].Generation.Should().Be(2);
        suleimanNode.Children[0].Children[0].Generation.Should().Be(3);
    }

    [Fact]
    public void Assemble_orders_siblings_by_name()
    {
        var suleiman = Member("سليمان");
        var zayd = Member("زيد", suleiman.Id);
        var ahmad = Member("أحمد", suleiman.Id);

        var roots = FamilyTreeAssembler.Assemble([suleiman, zayd, ahmad], MemberFilter.None, null);

        roots[0].Children.Select(c => c.Name).Should().ContainInOrder("أحمد", "زيد");
    }

    [Fact]
    public void Assemble_returns_only_the_requested_subtree_when_a_root_id_is_given()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None with { RootId = faris.Id }, null);

        var only = roots.Should().ContainSingle().Subject;
        only.Name.Should().Be("فارس");
        only.Children.Should().ContainSingle().Which.Name.Should().Be("محمود");
    }

    [Fact]
    public void Assemble_keeps_the_true_generation_of_a_subtree_root()
    {
        // A caller who fetched a subtree still needs to know how deep it sits in the family.
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None with { RootId = faris.Id }, null);

        roots[0].Generation.Should().Be(2);
        roots[0].Children[0].Generation.Should().Be(3);
    }

    [Fact]
    public void Assemble_returns_nothing_for_an_unknown_root_id()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None with { RootId = Guid.CreateVersion7() }, null)
            .Should().BeEmpty();
    }

    [Fact]
    public void Assemble_truncates_at_max_depth_and_flags_the_cut()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None, 2);

        var suleimanNode = roots.Single(n => n.Name == "سليمان");
        suleimanNode.HasMoreChildren.Should().BeFalse();

        var farisNode = suleimanNode.Children.Should().ContainSingle().Subject;
        farisNode.Children.Should().BeEmpty("depth 2 is the last level returned");
        farisNode.HasMoreChildren.Should().BeTrue("محمود exists but was not returned");
    }

    [Fact]
    public void Assemble_does_not_flag_a_childless_leaf_at_the_depth_limit()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None, 1);

        roots.Single(n => n.Name == "عمر").HasMoreChildren.Should()
             .BeFalse("عمر has no children at all, truncated or otherwise");
        roots.Single(n => n.Name == "سليمان").HasMoreChildren.Should().BeTrue();
    }

    [Fact]
    public void Assemble_treats_a_max_depth_of_one_as_the_top_level_only()
    {
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None, 1);

        roots.Should().HaveCount(2);
        roots.Should().OnlyContain(n => n.Children.Count == 0);
    }

    [Fact]
    public void Assemble_ignores_a_max_depth_below_one()
    {
        // A zero or negative depth is a client error that must not silently return an empty
        // tree, which would look like "this family has no members".
        var (suleiman, faris, mahmoud, omar) = ThreeGenerations();

        var roots = FamilyTreeAssembler.Assemble([suleiman, faris, mahmoud, omar], MemberFilter.None, 0);

        roots.Should().HaveCount(2);
        roots.Single(n => n.Name == "سليمان").Children.Should().ContainSingle();
    }

    [Fact]
    public void Assemble_drops_members_whose_parent_is_absent_from_the_input()
    {
        // Defensive: a partial fetch must never promote a descendant to first generation,
        // which would misrepresent the family.
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);

        var roots = FamilyTreeAssembler.Assemble([suleiman, mahmoud], MemberFilter.None, null);

        roots.Should().ContainSingle().Which.Name.Should().Be("سليمان");
        roots[0].Children.Should().BeEmpty();
    }

    [Fact]
    public void Assemble_handles_a_wide_generation()
    {
        var suleiman = Member("سليمان");
        var children = Enumerable.Range(0, 500)
            .Select(i => Member($"ابن {i:D3}", suleiman.Id))
            .ToList();

        var roots = FamilyTreeAssembler.Assemble([suleiman, .. children], MemberFilter.None, null);

        roots.Should().ContainSingle().Which.Children.Should().HaveCount(500);
    }
}
