using FamilyTree.Application.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class GenerationIndexTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(string name, Guid? parentId = null) =>
        FamilyMember.Create(TenantId, TreeId, parentId, name, Now);

    [Fact]
    public void An_empty_tree_yields_an_empty_index()
    {
        GenerationIndex.Build([]).Should().BeEmpty();
    }

    [Fact]
    public void A_parentless_member_is_generation_one()
    {
        var suleiman = Member("سليمان");

        GenerationIndex.Build([suleiman])[suleiman.Id].Should().Be(1);
    }

    [Fact]
    public void Each_step_down_the_chain_adds_a_generation()
    {
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);

        var index = GenerationIndex.Build([suleiman, faris, mahmoud]);

        index[suleiman.Id].Should().Be(1);
        index[faris.Id].Should().Be(2);
        index[mahmoud.Id].Should().Be(3);
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);

        var index = GenerationIndex.Build([mahmoud, faris, suleiman]);

        index[mahmoud.Id].Should().Be(3);
    }

    /// <summary>
    /// Spec §6: the composite self-FK makes this unrepresentable in the database, but the
    /// calculator is a pure function over whatever list it is handed and must not throw.
    /// </summary>
    [Fact]
    public void A_member_whose_parent_is_absent_is_treated_as_generation_one()
    {
        var orphan = Member("داوود", Guid.CreateVersion7());

        GenerationIndex.Build([orphan])[orphan.Id].Should().Be(1);
    }

    /// <summary>A cycle must terminate, not hang. The bound is the member count.</summary>
    [Fact]
    public void A_cyclic_parent_chain_terminates()
    {
        var a = Member("عمر");
        var b = Member("خالد", a.Id);
        Reparent(a, b.Id);

        var act = () => GenerationIndex.Build([a, b]);

        act.Should().NotThrow();
    }

    /// <summary>
    /// ParentId has a private setter and no re-parent command exists before Phase 5, so the
    /// only way to build a cycle for this test is reflection. It is confined to this test.
    /// </summary>
    private static void Reparent(FamilyMember member, Guid parentId) =>
        typeof(FamilyMember)
            .GetProperty(nameof(FamilyMember.ParentId))!
            .SetValue(member, parentId);
}
