using FamilyTree.Application.Reports;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class AnniversaryOccurrenceTests
{
    [Fact]
    public void An_anniversary_later_this_year_falls_this_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(1990, 9, 10), new DateOnly(2026, 8, 22))
            .Should().Be(new DateOnly(2026, 9, 10));
    }

    /// <summary>Today counts as upcoming: a birthday should not vanish on the morning of it.</summary>
    [Fact]
    public void An_anniversary_falling_today_is_today()
    {
        AnniversaryOccurrence.Next(new DateOnly(1990, 8, 22), new DateOnly(2026, 8, 22))
            .Should().Be(new DateOnly(2026, 8, 22));
    }

    [Fact]
    public void An_anniversary_already_past_this_year_rolls_to_next_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(1990, 3, 10), new DateOnly(2026, 8, 22))
            .Should().Be(new DateOnly(2027, 3, 10));
    }

    /// <summary>The year-boundary case: a December reference day reaching into January.</summary>
    [Fact]
    public void A_january_anniversary_seen_from_december_falls_in_the_following_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(1990, 1, 5), new DateOnly(2026, 12, 20))
            .Should().Be(new DateOnly(2027, 1, 5));
    }

    [Fact]
    public void A_leap_day_anniversary_falls_on_itself_in_a_leap_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(2000, 2, 29), new DateOnly(2028, 1, 1))
            .Should().Be(new DateOnly(2028, 2, 29));
    }

    /// <summary>
    /// Observed on 1 March in a common year: never dropped, and never landing before the
    /// anniversary date itself (design §6).
    /// </summary>
    [Fact]
    public void A_leap_day_anniversary_is_observed_on_the_first_of_march_in_a_common_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(2000, 2, 29), new DateOnly(2027, 1, 1))
            .Should().Be(new DateOnly(2027, 3, 1));
    }
}
