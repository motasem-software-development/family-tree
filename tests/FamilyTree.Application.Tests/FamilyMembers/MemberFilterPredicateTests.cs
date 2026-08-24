using FamilyTree.Application.FamilyMembers;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.FamilyMembers;

public class MemberFilterPredicateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();
    private static readonly Guid BranchId = Guid.CreateVersion7();
    private const int Palestine = 165;

    private static FamilyMember Member(
        string name = "فارس", bool isDeceased = false, int? countryId = null) =>
        FamilyMember.Create(
            TenantId, TreeId, null, name, Now,
            dateOfBirth: null, dateOfDeath: null, isDeceased: isDeceased,
            contact: new ContactDetails(null, null, null, countryId));

    private static readonly MemberPlacement Placement = new(BranchId, 2);

    private static MemberFilter Filter(
        string? search = null,
        MemberStatusFilter status = MemberStatusFilter.All,
        Guid? branchId = null,
        int? generation = null,
        int? countryId = null) =>
        new(search, status, branchId, generation, countryId, null);

    private static bool Matches(FamilyMember member, MemberFilter filter) =>
        MemberFilterPredicate.Matches(member, Placement, filter);

    [Fact]
    public void The_empty_filter_matches_everything() =>
        Matches(Member(), MemberFilter.None).Should().BeTrue();

    [Fact]
    public void The_empty_filter_matches_a_member_with_nothing_recorded() =>
        MemberFilterPredicate
            .Matches(Member(), new MemberPlacement(null, 0), MemberFilter.None)
            .Should().BeTrue();

    [Theory]
    [InlineData("فارس", true)]
    [InlineData("ارس", true)]
    [InlineData("سليمان", false)]
    public void Search_is_a_substring_match_on_the_name(string term, bool expected) =>
        Matches(Member("فارس"), Filter(search: term)).Should().Be(expected);

    [Theory]
    [InlineData("faris", true)]
    [InlineData("FARIS", true)]
    [InlineData("Faris", true)]
    public void Search_ignores_case(string term, bool expected) =>
        Matches(Member("Faris"), Filter(search: term)).Should().Be(expected);

    [Fact]
    public void Search_does_not_reach_the_contact_details()
    {
        // A name box that silently searches national IDs and phone numbers discloses more than
        // the user asked for. Contact details are filtered by their own controls or not at all.
        var member = FamilyMember.Create(
            TenantId, TreeId, null, "فارس", Now,
            dateOfBirth: null, dateOfDeath: null, isDeceased: false,
            contact: new ContactDetails("123456789", "+970599123456", null, Palestine));

        Matches(member, Filter(search: "123456789")).Should().BeFalse();
        Matches(member, Filter(search: "599123456")).Should().BeFalse();
    }

    [Fact]
    public void Search_matches_ordinally_so_it_agrees_with_the_sql_side()
    {
        // A linguistic comparison gives tatweel (U+0640) zero weight, so "محمد" would match
        // "محمـد" here while the CTE's ILIKE would not — the tree and the members list would
        // then answer differently for one term.
        Matches(Member("محمـد"), Filter(search: "محمد")).Should().BeFalse();
        Matches(Member("محمـد"), Filter(search: "محمـد")).Should().BeTrue();
    }

    [Fact]
    public void A_zero_width_term_does_not_match_everything()
    {
        // Under a linguistic comparison a term made only of zero-weight characters matches every
        // name, which would empty the tree of meaning while the list returned almost nothing.
        Matches(Member("فارس"), Filter(search: "ـ")).Should().BeFalse();
    }

    [Theory]
    [InlineData(MemberStatusFilter.Alive, false, true)]
    [InlineData(MemberStatusFilter.Alive, true, false)]
    [InlineData(MemberStatusFilter.Deceased, true, true)]
    [InlineData(MemberStatusFilter.Deceased, false, false)]
    [InlineData(MemberStatusFilter.All, true, true)]
    [InlineData(MemberStatusFilter.All, false, true)]
    public void Status_reads_is_deceased(
        MemberStatusFilter status, bool isDeceased, bool expected) =>
        Matches(Member(isDeceased: isDeceased), Filter(status: status)).Should().Be(expected);

    [Fact]
    public void Branch_matches_the_derived_branch() =>
        Matches(Member(), Filter(branchId: BranchId)).Should().BeTrue();

    [Fact]
    public void Branch_rejects_a_different_branch() =>
        Matches(Member(), Filter(branchId: Guid.CreateVersion7())).Should().BeFalse();

    [Fact]
    public void The_root_belongs_to_no_branch()
    {
        // "Root" is a rendering of "no branch", not a branch that can be selected. A branch
        // filter naming the root's own id must therefore match nobody — including the root.
        var root = Member();

        MemberFilterPredicate
            .Matches(root, new MemberPlacement(null, 0), Filter(branchId: root.Id))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    public void Generation_matches_the_derived_generation(int generation, bool expected) =>
        Matches(Member(), Filter(generation: generation)).Should().Be(expected);

    [Fact]
    public void Country_matches_the_country_of_residence() =>
        Matches(Member(countryId: Palestine), Filter(countryId: Palestine)).Should().BeTrue();

    [Fact]
    public void Country_rejects_a_member_with_no_country() =>
        Matches(Member(countryId: null), Filter(countryId: Palestine)).Should().BeFalse();

    [Fact]
    public void All_four_axes_at_once_match_together()
    {
        // Specification §15's combinability. Asserted alongside the four negatives below,
        // because a four-way AND that is accidentally an OR passes every single-axis test.
        var member = Member("فارس", isDeceased: true, countryId: Palestine);

        Matches(member, Filter("فارس", MemberStatusFilter.Deceased, BranchId, 2, Palestine))
            .Should().BeTrue();
    }

    [Fact]
    public void Changing_any_single_axis_breaks_the_four_way_match()
    {
        var member = Member("فارس", isDeceased: true, countryId: Palestine);

        Matches(member, Filter("سليمان", MemberStatusFilter.Deceased, BranchId, 2, Palestine))
            .Should().BeFalse();
        Matches(member, Filter("فارس", MemberStatusFilter.Alive, BranchId, 2, Palestine))
            .Should().BeFalse();
        Matches(member, Filter("فارس", MemberStatusFilter.Deceased, Guid.CreateVersion7(), 2, Palestine))
            .Should().BeFalse();
        Matches(member, Filter("فارس", MemberStatusFilter.Deceased, BranchId, 3, Palestine))
            .Should().BeFalse();
        Matches(member, Filter("فارس", MemberStatusFilter.Deceased, BranchId, 2, Palestine + 1))
            .Should().BeFalse();
    }
}
