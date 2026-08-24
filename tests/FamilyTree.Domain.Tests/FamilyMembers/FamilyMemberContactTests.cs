using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Domain.Tests.FamilyMembers;

public class FamilyMemberContactTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember AMember() =>
        FamilyMember.Create(TenantId, TreeId, null, "سليمان", Now);

    private static ContactDetails Contact(
        string? nationalId = null,
        string? mobile = null,
        string? whatsApp = null,
        int? countryId = null) => new(nationalId, mobile, whatsApp, countryId);

    [Fact]
    public void A_new_member_has_no_contact_details()
    {
        var member = AMember();

        member.NationalId.Should().BeNull();
        member.MobileNumber.Should().BeNull();
        member.WhatsAppNumber.Should().BeNull();
        member.CountryId.Should().BeNull();
    }

    [Fact]
    public void Update_stores_the_contact_details()
    {
        var member = AMember();

        member.Update(
            "سليمان", null, null, false,
            Contact("123456789", "+970599123456", "+201012345678", 3), Now);

        member.NationalId.Should().Be("123456789");
        member.MobileNumber.Should().Be("+970599123456");
        member.WhatsAppNumber.Should().Be("+201012345678");
        member.CountryId.Should().Be(3);
    }

    [Fact]
    public void Update_carrying_life_and_contact_details_bumps_the_version_exactly_once()
    {
        var member = AMember();
        var before = member.Version;

        member.Update(
            "سليمان", new DateOnly(1950, 1, 1), null, false,
            Contact(nationalId: "123456789", mobile: "+970599123456"), Now);

        member.Version.Should().Be(before + 1);
    }

    [Theory]
    [InlineData("12345678")]     // eight digits
    [InlineData("1234567890")]   // ten digits
    [InlineData("12345ABC9")]    // letters
    [InlineData("12345 678")]    // space
    public void Update_rejects_a_national_id_that_is_not_nine_digits(string nationalId)
    {
        var member = AMember();

        var act = () => member.Update("سليمان", null, null, false, Contact(nationalId), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_NATIONAL_ID_INVALID");
    }

    [Fact]
    public void Update_accepts_a_national_id_with_a_leading_zero_and_preserves_it()
    {
        var member = AMember();

        member.Update("سليمان", null, null, false, Contact("012345678"), Now);

        member.NationalId.Should().Be("012345678");
    }

    [Theory]
    [InlineData("0599123456")]        // no international prefix
    [InlineData("+0599123456")]       // leading zero after the plus
    [InlineData("+97059")]            // too short
    [InlineData("+9705991234567890")] // too long
    [InlineData("+97059912a456")]     // letters
    public void Update_rejects_a_phone_number_that_is_not_e164(string phone)
    {
        var member = AMember();

        var act = () => member.Update("سليمان", null, null, false, Contact(mobile: phone), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_PHONE_INVALID");
    }

    [Fact]
    public void Update_validates_the_whatsapp_number_the_same_way_as_the_mobile()
    {
        var member = AMember();

        var act = () => member.Update("سليمان", null, null, false, Contact(whatsApp: "0599123456"), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEMBER_PHONE_INVALID");
    }

    [Fact]
    public void Update_normalizes_spaces_and_dashes_out_of_a_phone_number()
    {
        var member = AMember();

        member.Update("سليمان", null, null, false, Contact(mobile: "+970 599-123 456"), Now);

        member.MobileNumber.Should().Be("+970599123456");
    }

    [Fact]
    public void Update_treats_a_blank_contact_field_as_cleared()
    {
        var member = AMember();
        member.Update("سليمان", null, null, false, Contact("123456789", "+970599123456"), Now);

        member.Update("سليمان", null, null, false, Contact("   ", "  "), Now);

        member.NationalId.Should().BeNull();
        member.MobileNumber.Should().BeNull();
    }

    [Fact]
    public void A_rejected_contact_edit_leaves_the_member_untouched()
    {
        var member = AMember();
        member.Update("سليمان", null, null, false, Contact("123456789"), Now);
        var versionBefore = member.Version;

        // The name and the national ID are both fine; the mobile is not. Nothing may change.
        var act = () => member.Update(
            "داوود", null, null, false, Contact("987654321", "0599123456"), Now);

        act.Should().Throw<DomainException>();
        member.Version.Should().Be(versionBefore);
        member.Name.Should().Be("سليمان");
        member.NationalId.Should().Be("123456789");
    }

    [Fact]
    public void Rename_preserves_the_contact_details()
    {
        var member = AMember();
        member.Update("سليمان", null, null, false, Contact("123456789", "+970599123456", null, 1), Now);

        member.Rename("داوود", Now);

        member.NationalId.Should().Be("123456789");
        member.MobileNumber.Should().Be("+970599123456");
        member.CountryId.Should().Be(1);
    }
}
