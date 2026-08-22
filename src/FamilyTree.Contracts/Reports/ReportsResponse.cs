namespace FamilyTree.Contracts.Reports;

/// <summary>
/// All five reports in one payload. One request, computed from a single pass over the member
/// list, which is what makes the whole screen one round trip (design §4).
/// </summary>
/// <param name="GeneratedOn">
/// The UTC reference day every date rule was evaluated against. Returned so a client renders
/// what the server measured rather than re-deriving "today" in its own time zone.
/// </param>
public sealed record ReportsResponse(
    DateOnly GeneratedOn,
    StructureReport Structure,
    LifeStatusReport LifeStatus,
    CompletenessReport Completeness,
    UpcomingReport Upcoming,
    ActivityReport Activity);
