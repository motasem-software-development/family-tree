using FamilyTree.Application.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.FamilyMembers;

/// <summary>
/// Mirrors <c>frontend/src/features/members/fullName.test.ts</c> case for case. The two
/// implementations compose the same name — the export must produce what the members list shows.
/// </summary>
public class MemberNameComposerTests
{
    /// <summary>Builds a root-to-leaf chain and returns the ids in the order given.</summary>
    private static (Dictionary<Guid, NamedMember> ById, Guid[] Ids) Chain(params string[] names)
    {
        var ids = names.Select(_ => Guid.CreateVersion7()).ToArray();
        var byId = new Dictionary<Guid, NamedMember>();

        for (var i = 0; i < names.Length; i++)
            byId[ids[i]] = new NamedMember(names[i], i == 0 ? null : ids[i - 1]);

        return (byId, ids);
    }

    [Fact]
    public void A_root_member_composes_to_their_own_name_alone()
    {
        // Padding it to four would invent ancestors.
        var (byId, ids) = Chain("داوود");

        MemberNameComposer.Compose(ids[0], byId).Should().Be("داوود");
    }

    [Fact]
    public void Three_generations_compose_own_name_first()
    {
        var (byId, ids) = Chain("داوود", "سليمان", "فارس");

        MemberNameComposer.Compose(ids[2], byId).Should().Be("فارس سليمان داوود");
    }

    [Fact]
    public void Five_generations_compose_to_four_parts()
    {
        // The rule the frontend states and an unbounded walk would silently break.
        var (byId, ids) = Chain("داوود", "سليمان", "فارس", "محمود", "خالد");

        MemberNameComposer.Compose(ids[4], byId).Should().Be("خالد محمود فارس سليمان");
    }

    [Fact]
    public void The_walk_stops_at_a_missing_parent()
    {
        // A filtered or partial map still yields a name worth showing.
        var (byId, ids) = Chain("داوود", "سليمان", "فارس");
        byId.Remove(ids[0]);

        MemberNameComposer.Compose(ids[2], byId).Should().Be("فارس سليمان");
    }

    [Fact]
    public void An_unknown_id_composes_to_nothing()
    {
        var (byId, _) = Chain("داوود");

        MemberNameComposer.Compose(Guid.CreateVersion7(), byId).Should().BeEmpty();
    }

    [Fact]
    public void A_cyclic_parent_chain_terminates()
    {
        // Impossible through the move command, which validates with a recursive CTE, but a
        // corrupt import must produce an answer rather than a hung request.
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var byId = new Dictionary<Guid, NamedMember>
        {
            [first] = new("سليمان", second),
            [second] = new("فارس", first)
        };

        MemberNameComposer.Compose(first, byId).Should().Be("سليمان فارس سليمان فارس");
    }

    [Fact]
    public void Parts_join_with_exactly_one_space()
    {
        // Specification §20: no double spaces. Joining a list rather than concatenating with
        // separators is what makes that fall out rather than need checking.
        var (byId, ids) = Chain(" داوود ", "سليمان ");

        MemberNameComposer.Compose(ids[1], byId).Should().Be("سليمان داوود");
    }

    [Fact]
    public void Parts_are_returned_own_name_first()
    {
        var (byId, ids) = Chain("داوود", "سليمان", "فارس");

        MemberNameComposer.Parts(ids[2], byId).Should().Equal("فارس", "سليمان", "داوود");
    }

    [Fact]
    public void Four_is_the_stated_maximum() => MemberNameComposer.MaxParts.Should().Be(4);
}
