using FamilyTree.Application.Export;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.Countries;
using FamilyTree.Contracts.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public class MemberExportRowsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 16);

    private static readonly CountryResponse Palestine = new(165, "PS", "فلسطين", "Palestine", "+970");
    private static readonly IReadOnlyList<CountryResponse> Countries = [Palestine];

    private static FamilyMemberListItem Member(
        Guid id,
        string name,
        Guid? parentId = null,
        string? nationalId = null,
        string? mobile = null,
        string? whatsApp = null,
        int? countryId = null,
        Guid? branchId = null,
        string? branchName = null,
        int generation = 1,
        bool isDeceased = false,
        DateOnly? dateOfBirth = null,
        DateOnly? dateOfDeath = null) =>
        new(id, name, parentId, 1, Now, Now, dateOfBirth, dateOfDeath, isDeceased,
            nationalId, mobile, whatsApp, countryId, null, branchId, branchName, generation);

    /// <summary>
    /// Builds with the lineage derived from the same list — the unfiltered case, where the rows
    /// and the family are the same set. The filtered case has its own test below.
    /// </summary>
    private static IReadOnlyList<MemberExportRow> Build(
        IReadOnlyList<FamilyMemberListItem> members, CaptionLanguage language) =>
        MemberExportRows.Build(
            members,
            members.ToDictionary(m => m.Id, m => new NamedMember(m.Name, m.ParentId)),
            Countries,
            language,
            Today);

    [Fact]
    public void The_headers_follow_specification_19s_order_in_english() =>
        MemberExportRows.Headers(CaptionLanguage.En).Should().Equal(
            "National ID",
            "Full Name",
            "Mobile Number",
            "WhatsApp Number",
            "Country of Residence",
            "Branch",
            "Generation",
            "Status",
            "Date of Birth",
            "Age",
            "Date of Death");

    [Fact]
    public void The_headers_are_arabic_in_arabic()
    {
        var headers = MemberExportRows.Headers(CaptionLanguage.Ar);

        headers.Should().HaveCount(11);
        headers[0].Should().Be("رقم الهوية");
        headers[7].Should().Be("الحالة");
        headers[10].Should().Be("تاريخ الوفاة");
    }

    [Fact]
    public void A_populated_member_fills_every_cell()
    {
        var branchId = Guid.CreateVersion7();
        var member = Member(
            Guid.CreateVersion7(), "فارس",
            nationalId: "123456789", mobile: "+970599123456", whatsApp: "+970599999999",
            countryId: 165, branchId: branchId, branchName: "سليمان",
            generation: 2, isDeceased: true,
            dateOfBirth: new DateOnly(1940, 3, 2), dateOfDeath: new DateOnly(2011, 3, 1));

        var row = Build([member], CaptionLanguage.En).Should()
            .ContainSingle().Subject;

        row.Should().Be(new MemberExportRow(
            "123456789", "فارس", "+970599123456", "+970599999999",
            "Palestine", "سليمان", 2, "Deceased",
            new DateOnly(1940, 3, 2), 70, new DateOnly(2011, 3, 1)));
    }

    [Fact]
    public void A_living_members_age_is_measured_against_today()
    {
        var member = Member(
            Guid.CreateVersion7(), "فارس", dateOfBirth: new DateOnly(1990, 8, 16));

        Build([member], CaptionLanguage.En)[0].Age.Should().Be(36);
    }

    [Fact]
    public void An_age_does_not_count_a_birthday_that_has_not_come_round_yet()
    {
        // Today is 16 August 2026; this member turns 36 the day after.
        var member = Member(
            Guid.CreateVersion7(), "فارس", dateOfBirth: new DateOnly(1990, 8, 17));

        Build([member], CaptionLanguage.En)[0].Age.Should().Be(35);
    }

    [Fact]
    public void A_deceased_members_age_is_their_age_at_death_not_today()
    {
        // The whole point of the split: measured against today this would read 86 and grow every
        // time the file is exported again.
        var member = Member(
            Guid.CreateVersion7(), "داوود", isDeceased: true,
            dateOfBirth: new DateOnly(1940, 1, 1), dateOfDeath: new DateOnly(2000, 1, 1));

        Build([member], CaptionLanguage.En)[0].Age.Should().Be(60);
    }

    [Fact]
    public void A_deceased_member_with_no_death_date_has_no_age()
    {
        // Measuring against today would report an age they never reached.
        var member = Member(
            Guid.CreateVersion7(), "داوود", isDeceased: true,
            dateOfBirth: new DateOnly(1940, 1, 1));

        var row = Build([member], CaptionLanguage.En)[0];

        row.Age.Should().BeNull();
        row.DateOfBirth.Should().Be(new DateOnly(1940, 1, 1));
        row.DateOfDeath.Should().BeNull();
    }

    [Fact]
    public void A_member_with_no_birth_date_has_no_dates_and_no_age()
    {
        var row = Build([Member(Guid.CreateVersion7(), "فارس")], CaptionLanguage.En)[0];

        row.DateOfBirth.Should().BeNull();
        row.Age.Should().BeNull();
        row.DateOfDeath.Should().BeNull();
    }

    [Fact]
    public void A_death_date_without_a_birth_date_still_reaches_the_row()
    {
        var member = Member(
            Guid.CreateVersion7(), "داوود", isDeceased: true,
            dateOfDeath: new DateOnly(2000, 1, 1));

        var row = Build([member], CaptionLanguage.En)[0];

        row.DateOfDeath.Should().Be(new DateOnly(2000, 1, 1));
        row.Age.Should().BeNull();
    }

    [Fact]
    public void A_leap_day_birth_ages_on_the_first_of_march_in_a_common_year()
    {
        // DateOnly.AddYears clamps 29 February to the 28th, which is what makes this correct.
        var born = new DateOnly(2000, 2, 29);
        var member = Member(Guid.CreateVersion7(), "فارس", dateOfBirth: born);

        MemberExportRows.Build(
            [member],
            new Dictionary<Guid, NamedMember> { [member.Id] = new(member.Name, null) },
            Countries,
            CaptionLanguage.En,
            new DateOnly(2026, 3, 1))[0].Age.Should().Be(26);
    }

    [Fact]
    public void The_full_name_walks_the_parent_chain()
    {
        var root = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        var members = new[]
        {
            Member(root, "داوود", generation: 0),
            Member(child, "سليمان", parentId: root, generation: 1)
        };

        var rows = Build(members, CaptionLanguage.En);

        rows[0].FullName.Should().Be("داوود");
        rows[1].FullName.Should().Be("سليمان داوود");
    }

    [Fact]
    public void The_root_reads_root_rather_than_blank()
    {
        // Specification §21: the absence of a branch renders as "Root", not as an empty cell.
        var member = Member(Guid.CreateVersion7(), "داوود", generation: 0);

        var row = Build([member], CaptionLanguage.En)[0];

        row.Branch.Should().Be("Root");
        row.Generation.Should().Be(0);
    }

    [Fact]
    public void The_root_label_is_localised()
    {
        var member = Member(Guid.CreateVersion7(), "داوود", generation: 0);

        Build([member], CaptionLanguage.Ar)[0].Branch
            .Should().Be("الجذر");
    }

    [Theory]
    [InlineData(true, CaptionLanguage.En, "Deceased")]
    [InlineData(false, CaptionLanguage.En, "Alive")]
    [InlineData(true, CaptionLanguage.Ar, "متوفى")]
    [InlineData(false, CaptionLanguage.Ar, "على قيد الحياة")]
    public void Status_comes_from_is_deceased_alone(
        bool isDeceased, CaptionLanguage language, string expected)
    {
        // There is no Status column in the database and there must not be one here either
        // (design spec §2.5) — IsDeceased is the whole fact.
        var member = Member(Guid.CreateVersion7(), "فارس", isDeceased: isDeceased);

        Build([member], language)[0].Status.Should().Be(expected);
    }

    [Fact]
    public void A_member_with_no_country_gets_an_empty_cell()
    {
        // Empty, not the word "null" — a workbook cell reading "null" is worse than a blank one.
        var member = Member(Guid.CreateVersion7(), "فارس");

        Build([member], CaptionLanguage.En)[0].Country
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(CaptionLanguage.Ar, "فلسطين")]
    [InlineData(CaptionLanguage.En, "Palestine")]
    public void The_country_name_follows_the_language(CaptionLanguage language, string expected)
    {
        var member = Member(Guid.CreateVersion7(), "فارس", countryId: 165);

        Build([member], language)[0].Country.Should().Be(expected);
    }

    [Fact]
    public void An_unknown_country_id_is_an_empty_cell_rather_than_a_failure()
    {
        // The member list and the country catalog are two responses and can disagree for one
        // request. A missing name is a blank cell, not a failed export.
        var member = Member(Guid.CreateVersion7(), "فارس", countryId: 999);

        Build([member], CaptionLanguage.En)[0].Country
            .Should().BeEmpty();
    }

    [Fact]
    public void The_identifiers_stay_strings_and_keep_a_leading_zero()
    {
        // The cell type is the workbook's decision, but a row that has already lost the zero
        // cannot be rescued by it (design spec §7.3).
        var member = Member(
            Guid.CreateVersion7(), "فارس", nationalId: "012345678", mobile: "+970599123456");

        var row = Build([member], CaptionLanguage.En)[0];

        row.NationalId.Should().Be("012345678");
        row.MobileNumber.Should().Be("+970599123456");
    }

    [Fact]
    public void An_unrecorded_identifier_is_an_empty_cell()
    {
        var member = Member(Guid.CreateVersion7(), "فارس");

        var row = Build([member], CaptionLanguage.En)[0];

        row.NationalId.Should().BeEmpty();
        row.MobileNumber.Should().BeEmpty();
        row.WhatsAppNumber.Should().BeEmpty();
    }

    [Fact]
    public void The_rows_keep_the_list_order() =>
        Build([
                Member(Guid.CreateVersion7(), "أحمد"),
                Member(Guid.CreateVersion7(), "خالد"),
                Member(Guid.CreateVersion7(), "زياد")
            ], CaptionLanguage.En)
        .Select(r => r.FullName).Should().Equal("أحمد", "خالد", "زياد");

    [Fact]
    public void The_full_name_composes_through_a_father_the_filter_dropped()
    {
        // The bug this pins: composing from the filtered rows drops a father the filter excluded,
        // so a filtered export carries different names than the same rows on screen. The lineage
        // map is the whole family, deliberately, exactly as MembersPage keeps an unfiltered query
        // for its own lineage index.
        var root = Guid.CreateVersion7();
        var father = Guid.CreateVersion7();
        var son = Guid.CreateVersion7();

        var lineage = new Dictionary<Guid, NamedMember>
        {
            [root] = new("داوود", null),
            [father] = new("سليمان", root),
            [son] = new("فارس", father)
        };

        // Only the son survived the filter.
        var filtered = new[] { Member(son, "فارس", parentId: father, generation: 2) };

        var rows = MemberExportRows.Build(
            filtered, lineage, Countries, CaptionLanguage.En, Today);

        rows.Should().ContainSingle().Which.FullName.Should().Be("فارس سليمان داوود");
    }

    [Fact]
    public void An_empty_list_builds_no_rows() =>
        Build([], CaptionLanguage.En).Should().BeEmpty();
}
