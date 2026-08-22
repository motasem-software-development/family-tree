namespace FamilyTree.Contracts.Reports;

/// <summary>
/// <paramref name="BirthdayCount"/> and <paramref name="AnniversaryCount"/> are the untruncated
/// totals: the lists are capped like every other, and a truncation no field discloses is a lie
/// the contract tells quietly (design §5).
/// </summary>
public sealed record UpcomingReport(
    int WindowDays,
    int BirthdayCount,
    int AnniversaryCount,
    IReadOnlyList<UpcomingBirthday> Birthdays,
    IReadOnlyList<UpcomingAnniversary> Anniversaries);

/// <summary>
/// <paramref name="Occurrence"/> is the day the observance falls on this cycle, which is not
/// always the anniversary date — see the 29 February rule. <paramref name="TurningAge"/> is
/// the age reached on that day, not the age today.
/// </summary>
public sealed record UpcomingBirthday(
    MemberRef Member, DateOnly DateOfBirth, DateOnly Occurrence, int DaysAway, int TurningAge);

public sealed record UpcomingAnniversary(
    MemberRef Member, DateOnly DateOfDeath, DateOnly Occurrence, int DaysAway, int Years);
