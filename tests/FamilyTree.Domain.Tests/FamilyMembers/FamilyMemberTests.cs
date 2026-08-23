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

    // ---- Life details: dates of birth and death, and the living/deceased flag ----

    private static readonly DateOnly Born = new(1920, 3, 14);
    private static readonly DateOnly Died = new(1995, 11, 2);

    [Fact]
    public void Create_defaults_to_a_living_member_with_no_dates()
    {
        // The imported tree carries names and nothing else, so "alive, dates unknown" is the
        // only honest default for a member created without life details.
        var member = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);

        member.DateOfBirth.Should().BeNull();
        member.DateOfDeath.Should().BeNull();
        member.IsDeceased.Should().BeFalse();
    }

    [Fact]
    public void Create_records_the_life_details_it_is_given()
    {
        var member = FamilyMember.Create(
            TenantId, TreeId, null, "سليمان", Now, Born, Died, isDeceased: true);

        member.DateOfBirth.Should().Be(Born);
        member.DateOfDeath.Should().Be(Died);
        member.IsDeceased.Should().BeTrue();
    }

    [Fact]
    public void Create_marks_a_member_deceased_when_a_death_date_is_given_without_the_flag()
    {
        // A caller supplying a death date has stated the fact; silently keeping the member
        // "alive" would contradict the very data being saved.
        var member = FamilyMember.Create(
            TenantId, TreeId, null, "سليمان", Now, Born, Died, isDeceased: false);

        member.IsDeceased.Should().BeTrue();
    }

    [Fact]
    public void Create_allows_a_deceased_member_with_no_known_death_date()
    {
        // The case the explicit flag exists for: genealogy records routinely say "died" while
        // the date is lost.
        var member = FamilyMember.Create(
            TenantId, TreeId, null, "سليمان", Now, Born, null, isDeceased: true);

        member.IsDeceased.Should().BeTrue();
        member.DateOfDeath.Should().BeNull();
    }

    [Fact]
    public void Create_rejects_a_death_date_before_the_birth_date()
    {
        var act = () => FamilyMember.Create(
            TenantId, TreeId, null, "سليمان", Now, Died, Born, isDeceased: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_DEATH_BEFORE_BIRTH");
    }

    [Fact]
    public void Create_accepts_a_death_date_equal_to_the_birth_date()
    {
        // An infant who died the day they were born is a real and unfortunately common record.
        var member = FamilyMember.Create(
            TenantId, TreeId, null, "سليمان", Now, Born, Born, isDeceased: true);

        member.DateOfDeath.Should().Be(Born);
    }

    [Fact]
    public void Create_rejects_a_birth_date_in_the_future()
    {
        var tomorrow = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);

        var act = () => FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now, tomorrow);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_DATE_IN_FUTURE");
    }

    [Fact]
    public void Create_rejects_a_death_date_in_the_future()
    {
        var tomorrow = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);

        var act = () => FamilyMember.Create(
            TenantId, TreeId, null, "سليمان", Now, Born, tomorrow, isDeceased: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_DATE_IN_FUTURE");
    }

    [Fact]
    public void Create_accepts_a_date_of_today()
    {
        // "Today" is the boundary: a newborn recorded the day they are born must be accepted.
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        var member = FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now, today);

        member.DateOfBirth.Should().Be(today);
    }

    [Fact]
    public void Update_changes_the_name_and_the_life_details_in_one_version_bump()
    {
        // One save is one edit: bumping the version twice for a single form submission would
        // make the client's freshly returned version stale against its own write.
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);
        var later = Now.AddDays(1);

        member.Update("فارس أحمد", Born, Died, isDeceased: true, now: later);

        member.Name.Should().Be("فارس أحمد");
        member.DateOfBirth.Should().Be(Born);
        member.DateOfDeath.Should().Be(Died);
        member.IsDeceased.Should().BeTrue();
        member.Version.Should().Be(2);
        member.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Update_can_clear_the_life_details_again()
    {
        // Correcting a mistaken death record has to be possible, or a misclick is permanent.
        var member = FamilyMember.Create(
            TenantId, TreeId, null, "فارس", Now, Born, Died, isDeceased: true);

        member.Update("فارس", null, null, isDeceased: false, now: Now.AddDays(1));

        member.DateOfBirth.Should().BeNull();
        member.DateOfDeath.Should().BeNull();
        member.IsDeceased.Should().BeFalse();
    }

    [Fact]
    public void Update_applies_the_same_date_rules_as_create()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);

        var act = () => member.Update("فارس", Died, Born, isDeceased: true, now: Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_DEATH_BEFORE_BIRTH");
    }

    [Fact]
    public void Update_leaves_the_member_untouched_when_a_date_is_rejected()
    {
        // Same guarantee Rename already makes: a rejected write must not advance the version,
        // or a client retrying after a validation error hits a spurious concurrency conflict.
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);

        var act = () => member.Update("فارس أحمد", Died, Born, isDeceased: true, now: Now);

        act.Should().Throw<DomainException>();
        member.Name.Should().Be("فارس");
        member.Version.Should().Be(1);
        member.IsDeceased.Should().BeFalse();
    }

    [Fact]
    public void Rename_preserves_the_life_details()
    {
        var member = FamilyMember.Create(
            TenantId, TreeId, null, "فارس", Now, Born, Died, isDeceased: true);

        member.Rename("فارس أحمد", Now.AddDays(1));

        member.DateOfBirth.Should().Be(Born);
        member.DateOfDeath.Should().Be(Died);
        member.IsDeceased.Should().BeTrue();
    }

    [Fact]
    public void MoveTo_reattaches_the_member_to_a_new_parent()
    {
        var member = FamilyMember.Create(TenantId, TreeId, Guid.CreateVersion7(), "فارس", Now);
        var newParent = Guid.CreateVersion7();

        member.MoveTo(newParent, Now);

        member.ParentId.Should().Be(newParent);
    }

    [Fact]
    public void MoveTo_null_promotes_the_member_to_first_generation()
    {
        var member = FamilyMember.Create(TenantId, TreeId, Guid.CreateVersion7(), "فارس", Now);

        member.MoveTo(null, Now);

        member.ParentId.Should().BeNull();
    }

    [Fact]
    public void MoveTo_treats_an_empty_guid_as_no_parent()
    {
        // Create already normalizes Guid.Empty; a move that did not would send it to the
        // database and fail a foreign key instead of recording a first-generation member.
        var member = FamilyMember.Create(TenantId, TreeId, Guid.CreateVersion7(), "فارس", Now);

        member.MoveTo(Guid.Empty, Now);

        member.ParentId.Should().BeNull();
    }

    [Fact]
    public void MoveTo_bumps_the_version_and_the_timestamp()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);
        var later = Now.AddHours(1);

        member.MoveTo(Guid.CreateVersion7(), later);

        member.Version.Should().Be(2);
        member.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void MoveTo_refuses_to_make_a_member_their_own_parent()
    {
        var member = FamilyMember.Create(TenantId, TreeId, null, "فارس", Now);

        var act = () => member.MoveTo(member.Id, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MOVE_CREATES_CYCLE");
    }

    [Fact]
    public void MoveTo_leaves_the_member_untouched_when_it_refuses()
    {
        var original = Guid.CreateVersion7();
        var member = FamilyMember.Create(TenantId, TreeId, original, "فارس", Now);

        var act = () => member.MoveTo(member.Id, Now.AddHours(1));

        act.Should().Throw<DomainException>();
        member.ParentId.Should().Be(original);
        member.Version.Should().Be(1);
        member.UpdatedAt.Should().Be(Now);
    }
}
