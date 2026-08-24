using FamilyTree.Application.FamilyMembers;
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.FamilyTrees;

/// <summary>
/// Design spec §4.2 — the tree filters during assembly, and a member who fails the filter but
/// has a matching descendant stays visible with <c>Matches</c> false. Separate from
/// <see cref="FamilyTreeAssemblerTests"/> so the unfiltered contract and the filtering rules can
/// each be read on their own.
/// </summary>
public class FamilyTreeAssemblerFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(string name, Guid? parentId = null, bool isDeceased = false) =>
        FamilyMember.Create(
            TenantId, TreeId, parentId, name, Now,
            dateOfBirth: null, dateOfDeath: null, isDeceased: isDeceased, contact: default);

    /// <summary>
    /// داوود → سليمان → فارس → محمود, with خالد beside فارس and عمر → يوسف beside سليمان. The
    /// same shape MemberDerivationTests uses, so the two files can be read against each other.
    /// </summary>
    private sealed record Example(
        FamilyMember Dawood,
        FamilyMember Suleiman,
        FamilyMember Faris,
        FamilyMember Mahmoud,
        FamilyMember Khaled,
        FamilyMember Omar,
        FamilyMember Yousef)
    {
        public IReadOnlyList<FamilyMember> All =>
            [Dawood, Suleiman, Faris, Mahmoud, Khaled, Omar, Yousef];
    }

    private static Example WorkedExample()
    {
        var dawood = Member("داوود");
        var suleiman = Member("سليمان", dawood.Id);
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);
        var khaled = Member("خالد", suleiman.Id);
        var omar = Member("عمر", dawood.Id);
        var yousef = Member("يوسف", omar.Id);

        return new Example(dawood, suleiman, faris, mahmoud, khaled, omar, yousef);
    }

    private static IEnumerable<FamilyTreeNodeResponse> Flatten(
        IEnumerable<FamilyTreeNodeResponse> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));

    [Fact]
    public void The_unfiltered_path_marks_every_node_as_a_match()
    {
        // MemberFilter.None must not change the shape of the response, only stamp the flag.
        var roots = FamilyTreeAssembler.Assemble(WorkedExample().All, MemberFilter.None, null);

        Flatten(roots).Should().HaveCount(7).And.OnlyContain(node => node.Matches);
    }

    [Fact]
    public void A_matching_member_keeps_their_whole_ancestor_chain_dimmed()
    {
        // The ancestor rule: dropping the chain would detach the subtree and render the outline
        // as garbage.
        var roots = FamilyTreeAssembler.Assemble(
            WorkedExample().All, MemberFilter.None with { Search = "محمود" }, null);

        var nodes = Flatten(roots).ToList();
        nodes.Select(n => n.Name).Should().Equal("داوود", "سليمان", "فارس", "محمود");
        nodes.Where(n => n.Matches).Select(n => n.Name).Should().Equal("محمود");
    }

    [Fact]
    public void A_subtree_with_no_match_is_dropped_entirely()
    {
        // The rule keeps ancestors OF A MATCH, not every member.
        var roots = FamilyTreeAssembler.Assemble(
            WorkedExample().All, MemberFilter.None with { Search = "محمود" }, null);

        Flatten(roots).Select(n => n.Name).Should().NotContain(["عمر", "يوسف", "خالد"]);
    }

    [Fact]
    public void A_matching_members_non_matching_children_are_dropped()
    {
        // A descendant carries no structural obligation — only ancestors do.
        var roots = FamilyTreeAssembler.Assemble(
            WorkedExample().All, MemberFilter.None with { Search = "فارس" }, null);

        var nodes = Flatten(roots).ToList();
        nodes.Select(n => n.Name).Should().Equal("داوود", "سليمان", "فارس");
        nodes.Should().NotContain(n => n.Name == "محمود");
    }

    [Fact]
    public void A_filter_matching_nobody_returns_an_empty_tree() =>
        FamilyTreeAssembler
            .Assemble(WorkedExample().All, MemberFilter.None with { Search = "لا أحد" }, null)
            .Should().BeEmpty();

    [Fact]
    public void The_generation_filter_is_root_relative_while_the_reported_generation_is_absolute()
    {
        // The two halves of design spec §1.2, asserted together: asserting either alone would
        // pass with the other wrong. Rooted at فارس — absolute generation 3 — generation=1 picks
        // out فارس's children, and محمود still reports its absolute 4.
        var example = WorkedExample();

        var roots = FamilyTreeAssembler.Assemble(
            example.All,
            MemberFilter.None with { RootId = example.Faris.Id, Generation = 1 },
            null);

        var nodes = Flatten(roots).ToList();
        nodes.Select(n => n.Name).Should().Equal("فارس", "محمود");
        nodes.Single(n => n.Name == "محمود").Should().Match<FamilyTreeNodeResponse>(
            n => n.Matches && n.Generation == 4);
        nodes.Single(n => n.Name == "فارس").Should().Match<FamilyTreeNodeResponse>(
            n => !n.Matches && n.Generation == 3);
    }

    [Fact]
    public void The_selected_root_is_generation_zero_to_the_filter_and_absolute_in_the_response()
    {
        var example = WorkedExample();

        var roots = FamilyTreeAssembler.Assemble(
            example.All,
            MemberFilter.None with { RootId = example.Suleiman.Id, Generation = 0 },
            null);

        var node = roots.Should().ContainSingle().Subject;
        node.Name.Should().Be("سليمان");
        node.Matches.Should().BeTrue();
        node.Generation.Should().Be(2);
        node.Children.Should().BeEmpty();
    }

    [Fact]
    public void A_depth_limit_and_a_filter_apply_together()
    {
        var roots = FamilyTreeAssembler.Assemble(
            WorkedExample().All, MemberFilter.None with { Search = "محمود" }, 2);

        var root = roots.Should().ContainSingle().Subject;
        root.Name.Should().Be("داوود");
        var suleiman = root.Children.Should().ContainSingle().Subject;
        suleiman.Name.Should().Be("سليمان");

        // فارس survived the filter, so the expander is honest about there being more below.
        suleiman.HasMoreChildren.Should().BeTrue();
        suleiman.Children.Should().BeEmpty();
    }

    [Fact]
    public void Has_more_children_ignores_children_the_filter_dropped()
    {
        // فارس's only child محمود does not match, so at the depth limit there is nothing left
        // below فارس to expand into. Counting the raw children would light an expander that
        // opens onto nothing.
        var roots = FamilyTreeAssembler.Assemble(
            WorkedExample().All, MemberFilter.None with { Search = "فارس" }, 3);

        var faris = Flatten(roots).Single(n => n.Name == "فارس");
        faris.HasMoreChildren.Should().BeFalse();
        faris.Children.Should().BeEmpty();
    }

    [Fact]
    public void A_root_whose_subtree_holds_no_match_returns_an_empty_tree()
    {
        var example = WorkedExample();

        FamilyTreeAssembler
            .Assemble(
                example.All,
                MemberFilter.None with { RootId = example.Omar.Id, Search = "محمود" },
                null)
            .Should().BeEmpty();
    }

    [Fact]
    public void The_status_filter_keeps_a_living_ancestor_of_a_deceased_member()
    {
        var dawood = Member("داوود");
        var suleiman = Member("سليمان", dawood.Id);
        var faris = Member("فارس", suleiman.Id, isDeceased: true);

        var roots = FamilyTreeAssembler.Assemble(
            [dawood, suleiman, faris],
            MemberFilter.None with { Status = MemberStatusFilter.Deceased },
            null);

        var nodes = Flatten(roots).ToList();
        nodes.Select(n => n.Name).Should().Equal("داوود", "سليمان", "فارس");
        nodes.Where(n => n.Matches).Select(n => n.Name).Should().Equal("فارس");
    }

    [Fact]
    public void A_country_filter_keeps_the_chain_above_the_match()
    {
        const int palestine = 165;
        var dawood = Member("داوود");
        var suleiman = Member("سليمان", dawood.Id);
        var faris = FamilyMember.Create(
            TenantId, TreeId, suleiman.Id, "فارس", Now,
            dateOfBirth: null, dateOfDeath: null, isDeceased: false,
            contact: new ContactDetails(null, null, null, palestine));

        var roots = FamilyTreeAssembler.Assemble(
            [dawood, suleiman, faris],
            MemberFilter.None with { CountryId = palestine },
            null);

        Flatten(roots).Where(n => n.Matches).Select(n => n.Name).Should().Equal("فارس");
    }

    [Fact]
    public void A_branch_filter_keeps_the_root_above_the_branch()
    {
        // Filtering to a branch keeps the root itself, dimmed: it is the branch's ancestor, and
        // it belongs to no branch of its own.
        var example = WorkedExample();

        var roots = FamilyTreeAssembler.Assemble(
            example.All, MemberFilter.None with { BranchId = example.Omar.Id }, null);

        var nodes = Flatten(roots).ToList();
        nodes.Select(n => n.Name).Should().Equal("داوود", "عمر", "يوسف");
        nodes.Single(n => n.Name == "داوود").Matches.Should().BeFalse();
        nodes.Where(n => n.Matches).Select(n => n.Name).Should().Equal("عمر", "يوسف");
    }
}
