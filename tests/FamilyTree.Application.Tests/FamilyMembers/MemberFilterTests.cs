using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.FamilyMembers;

public class MemberFilterTests
{
    private static MemberFilterRequest Request(
        string? search = null,
        string? status = null,
        Guid? branchId = null,
        int? generation = null,
        int? countryId = null,
        Guid? rootId = null) =>
        new(search, status, branchId, generation, countryId, rootId);

    private static MemberFilter Create(MemberFilterRequest request)
    {
        MemberFilter.TryCreate(request, out var filter).Should().BeTrue();
        return filter;
    }

    [Fact]
    public void An_empty_request_is_the_empty_filter()
    {
        var filter = Create(Request());

        filter.Should().Be(MemberFilter.None);
        filter.IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData("all", MemberStatusFilter.All)]
    [InlineData("ALIVE", MemberStatusFilter.Alive)]
    [InlineData("Deceased", MemberStatusFilter.Deceased)]
    [InlineData("aLiVe", MemberStatusFilter.Alive)]
    public void Status_parses_case_insensitively(string status, MemberStatusFilter expected) =>
        Create(Request(status: status)).Status.Should().Be(expected);

    [Fact]
    public void An_unrecognised_status_is_rejected()
    {
        // The caller turns this into a 400 FILTER_INVALID_STATUS rather than silently
        // defaulting, per design §5.1 and the precedent EXPORT_INVALID_STYLE set.
        MemberFilter.TryCreate(Request(status: "dead"), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_status_and_an_empty_one_both_mean_all(string? status)
    {
        // Both arrive identically over the wire — ?status= and no status at all — so they must
        // not diverge, and neither counts as a filter.
        var filter = Create(Request(status: status));

        filter.Status.Should().Be(MemberStatusFilter.All);
        filter.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_blank_search_is_not_a_filter() =>
        Create(Request(search: "   ")).Should().Match<MemberFilter>(f => f.Search == null && f.IsEmpty);

    [Fact]
    public void A_search_term_is_trimmed() =>
        Create(Request(search: " فارس ")).Search.Should().Be("فارس");

    [Fact]
    public void A_search_term_makes_the_filter_non_empty() =>
        Create(Request(search: "فارس")).IsEmpty.Should().BeFalse();

    [Fact]
    public void A_branch_makes_the_filter_non_empty() =>
        Create(Request(branchId: Guid.CreateVersion7())).IsEmpty.Should().BeFalse();

    [Fact]
    public void A_generation_makes_the_filter_non_empty() =>
        Create(Request(generation: 2)).IsEmpty.Should().BeFalse();

    [Fact]
    public void A_country_makes_the_filter_non_empty() =>
        Create(Request(countryId: 1)).IsEmpty.Should().BeFalse();

    [Fact]
    public void A_status_of_alive_makes_the_filter_non_empty() =>
        Create(Request(status: "alive")).IsEmpty.Should().BeFalse();

    [Fact]
    public void A_root_alone_leaves_the_filter_empty()
    {
        // RootId selects what branch and generation are measured from; it removes nobody.
        // Counting it as a filter would send an unfiltered subtree view down the expensive
        // path and report it to the user as filtered.
        var root = Guid.CreateVersion7();
        var filter = Create(Request(rootId: root));

        filter.RootId.Should().Be(root);
        filter.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void An_unmatchable_generation_is_a_filter_not_an_error()
    {
        // A generation nothing sits at returns an empty list. It is not malformed, and the
        // spec has no code for it.
        var filter = Create(Request(generation: -1));

        filter.Generation.Should().Be(-1);
        filter.IsEmpty.Should().BeFalse();
    }
}
