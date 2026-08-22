namespace FamilyTree.Application.Reports;

public static class AnniversaryOccurrence
{
    /// <summary>
    /// The next time this anniversary comes round, on or after <paramref name="today"/>.
    /// Inclusive of today: a birthday should not disappear on the morning of it.
    /// </summary>
    public static DateOnly Next(DateOnly anniversary, DateOnly today)
    {
        var thisYear = InYear(anniversary, today.Year);
        return thisYear >= today ? thisYear : InYear(anniversary, today.Year + 1);
    }

    /// <summary>
    /// 29 February is observed on 1 March in a common year. Chosen over skipping it, so the
    /// person never silently vanishes from a 30-day window, and over 28 February, so an
    /// observance never lands before its own anniversary date (design §6).
    /// </summary>
    private static DateOnly InYear(DateOnly anniversary, int year) =>
        anniversary is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(year)
            ? new DateOnly(year, 3, 1)
            : new DateOnly(year, anniversary.Month, anniversary.Day);
}
