namespace FamilyTree.Application.Reports;

public static class Ages
{
    /// <summary>
    /// Whole years elapsed, decremented when the anniversary has not yet come round in the
    /// target year. DateOnly.AddYears clamps 29 February to the 28th in a common year, which
    /// is what makes a leap-day birth increment on the right day.
    /// </summary>
    public static int YearsBetween(DateOnly from, DateOnly to)
    {
        var years = to.Year - from.Year;
        if (to < from.AddYears(years)) years--;
        return years;
    }
}
