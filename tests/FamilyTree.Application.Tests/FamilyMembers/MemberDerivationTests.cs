using FamilyTree.Application.FamilyMembers;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.FamilyMembers;

/// <summary>
/// Specification §21's worked example, reproduced as a literal table rather than paraphrased —
/// it is the clearest statement of the branch-vs-generation distinction §30 calls fundamental
/// (design spec §8). The same table is asserted against the SQL in FamilyMemberQueryTests, which
/// is what keeps the two implementations of the walk from drifting apart.
/// </summary>
public class MemberDerivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(string name, Guid? parentId = null) =>
        FamilyMember.Create(TenantId, TreeId, parentId, name, Now);

    /// <summary>
    /// داوود                     branch = (none → "Root")   generation 0
    /// ├── سليمان                branch = سليمان             generation 1
    /// │   ├── فارس              branch = سليمان             generation 2
    /// │   │   └── محمود         branch = سليمان             generation 3
    /// │   └── خالد              branch = سليمان             generation 2
    /// └── عمر                   branch = عمر                generation 1
    ///     └── يوسف              branch = عمر                generation 2
    /// </summary>
    private static IReadOnlyList<FamilyMember> WorkedExample()
    {
        var dawood = Member("داوود");
        var suleiman = Member("سليمان", dawood.Id);
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);
        var khaled = Member("خالد", suleiman.Id);
        var omar = Member("عمر", dawood.Id);
        var yousef = Member("يوسف", omar.Id);

        return [dawood, suleiman, faris, mahmoud, khaled, omar, yousef];
    }

    private static FamilyMember Named(IReadOnlyList<FamilyMember> members, string name) =>
        members.Single(m => m.Name == name);

    [Theory]
    [InlineData("داوود", null, 0)]
    [InlineData("سليمان", "سليمان", 1)]
    [InlineData("فارس", "سليمان", 2)]
    [InlineData("محمود", "سليمان", 3)]
    [InlineData("خالد", "سليمان", 2)]
    [InlineData("عمر", "عمر", 1)]
    [InlineData("يوسف", "عمر", 2)]
    public void The_worked_example(string name, string? branchName, int generation)
    {
        var members = WorkedExample();

        var placement = MemberDerivation.Derive(members, rootId: null)[Named(members, name).Id];

        placement.Generation.Should().Be(generation);
        placement.BranchId.Should().Be(branchName is null ? null : Named(members, branchName).Id);
    }

    [Fact]
    public void The_root_has_no_branch_and_generation_zero()
    {
        // Null branch renders as "Root" per specification §21. It is the absence of a branch,
        // not a branch you can select.
        var members = WorkedExample();

        MemberDerivation.Derive(members, rootId: null)[Named(members, "داوود").Id]
            .Should().Be(new MemberPlacement(null, 0));
    }

    [Fact]
    public void Every_member_is_placed_when_the_whole_tree_is_walked() =>
        MemberDerivation.Derive(WorkedExample(), rootId: null).Should().HaveCount(7);

    [Theory]
    [InlineData("سليمان", null, 0)]
    [InlineData("فارس", "فارس", 1)]
    [InlineData("خالد", "خالد", 1)]
    [InlineData("محمود", "فارس", 2)]
    public void A_selected_root_re_measures_everything_below_it(
        string name, string? branchName, int generation)
    {
        // The same member answers differently under a different root, which is the whole reason
        // the root is a parameter (design spec §1.3).
        var members = WorkedExample();

        var placement = MemberDerivation
            .Derive(members, Named(members, "سليمان").Id)[Named(members, name).Id];

        placement.Generation.Should().Be(generation);
        placement.BranchId.Should().Be(branchName is null ? null : Named(members, branchName).Id);
    }

    [Fact]
    public void A_member_outside_the_selected_subtree_is_absent()
    {
        // Absent, not present with a null placement: the tree filter prunes on absence, and a
        // sentinel placement would have to be checked for at every use.
        var members = WorkedExample();

        var placements = MemberDerivation.Derive(members, Named(members, "سليمان").Id);

        placements.Should().HaveCount(4);
        placements.ContainsKey(Named(members, "عمر").Id).Should().BeFalse();
        placements.ContainsKey(Named(members, "داوود").Id).Should().BeFalse();
    }

    [Fact]
    public void An_unknown_root_places_nobody() =>
        MemberDerivation.Derive(WorkedExample(), Guid.CreateVersion7()).Should().BeEmpty();

    [Fact]
    public void An_empty_tree_places_nobody() =>
        MemberDerivation.Derive([], rootId: null).Should().BeEmpty();

    [Fact]
    public void Several_parentless_members_are_all_roots()
    {
        // Design spec §1.3 reads as though each would be its own branch; §3's CTE — the
        // normative rule — makes each one a root with a null branch at generation 0. See the
        // plan's "Refinement of spec §1.3". The data has exactly one parentless member, so the
        // two readings differ only on a shape that does not exist; this test records which one
        // the code implements.
        var first = Member("داوود");
        var second = Member("سليمان");

        var placements = MemberDerivation.Derive([first, second], rootId: null);

        placements[first.Id].Should().Be(new MemberPlacement(null, 0));
        placements[second.Id].Should().Be(new MemberPlacement(null, 0));
    }

    [Fact]
    public void A_looping_parent_chain_terminates()
    {
        // Cycles are impossible through the move command, which validates with a recursive CTE.
        // This keeps a corrupt import from turning a request into a hang rather than an answer.
        var root = Member("داوود");
        var first = Member("سليمان", root.Id);
        var second = Member("فارس", first.Id);
        first.MoveTo(second.Id, Now);

        var placements = MemberDerivation.Derive([root, first, second], rootId: null);

        placements.Should().ContainSingle().Which.Key.Should().Be(root.Id);
    }
}
