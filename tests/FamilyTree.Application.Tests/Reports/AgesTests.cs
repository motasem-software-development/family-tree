using FamilyTree.Application.Reports;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class AgesTests
{
    [Fact]
    public void The_day_before_a_birthday_the_age_has_not_yet_incremented()
    {
        Ages.YearsBetween(new DateOnly(1990, 8, 22), new DateOnly(2026, 8, 21)).Should().Be(35);
    }

    [Fact]
    public void On_the_birthday_the_age_increments()
    {
        Ages.YearsBetween(new DateOnly(1990, 8, 22), new DateOnly(2026, 8, 22)).Should().Be(36);
    }

    [Fact]
    public void A_birth_earlier_in_the_same_year_counts_as_zero_years()
    {
        Ages.YearsBetween(new DateOnly(2026, 1, 5), new DateOnly(2026, 8, 22)).Should().Be(0);
    }

    /// <summary>A leap-day birth measured in a common year: DateOnly.AddYears clamps to the 28th.</summary>
    [Fact]
    public void A_leap_day_birth_increments_in_a_common_year()
    {
        Ages.YearsBetween(new DateOnly(2000, 2, 29), new DateOnly(2027, 3, 1)).Should().Be(27);
    }
}
