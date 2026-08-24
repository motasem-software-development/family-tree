using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.Countries;

namespace FamilyTree.Domain.Tests.Countries;

public class CountryTests
{
    [Fact]
    public void Create_uppercases_the_code_and_keeps_the_names()
    {
        var country = Country.Create("ps", "فلسطين", "Palestine", "+970");

        country.Code.Should().Be("PS");
        country.NameAr.Should().Be("فلسطين");
        country.NameEn.Should().Be("Palestine");
        country.DialCode.Should().Be("+970");
    }

    [Theory]
    [InlineData("P")]
    [InlineData("PSE")]
    [InlineData("P1")]
    [InlineData("")]
    public void Create_rejects_a_code_that_is_not_two_letters(string code)
    {
        var act = () => Country.Create(code, "فلسطين", "Palestine", "+970");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("COUNTRY_CODE_INVALID");
    }

    [Theory]
    [InlineData("970")]
    [InlineData("+")]
    [InlineData("+0")]
    [InlineData("+97a")]
    public void Create_rejects_a_dial_code_that_is_not_plus_digits(string dialCode)
    {
        var act = () => Country.Create("PS", "فلسطين", "Palestine", dialCode);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("COUNTRY_DIAL_CODE_INVALID");
    }

    [Fact]
    public void Create_rejects_a_missing_name()
    {
        var act = () => Country.Create("PS", "  ", "Palestine", "+970");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("COUNTRY_NAME_REQUIRED");
    }
}
