namespace FamilyTree.Contracts.Reports;

/// <summary>
/// A stand-in for audit history, not a substitute for it: this reads the current state of a
/// row's timestamps, so it cannot show deletions, cannot show who made a change, and shows
/// only the most recent edit of several. The real fix is the AuditLog entity, which does not
/// yet exist (design §9).
/// </summary>
public sealed record ActivityReport(
    int WindowDays,
    int AddedCount,
    int EditedCount,
    IReadOnlyList<ActivityEntry> Added,
    IReadOnlyList<ActivityEntry> Edited);

public sealed record ActivityEntry(MemberRef Member, DateTimeOffset At);
