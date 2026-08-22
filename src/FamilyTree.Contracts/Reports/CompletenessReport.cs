namespace FamilyTree.Contracts.Reports;

/// <summary>
/// A curation worklist. <paramref name="CompleteRecords"/> counts members flagged by no code
/// at all; the codes themselves are independent lists, not a partition, so a member may appear
/// under more than one (design §5).
/// </summary>
public sealed record CompletenessReport(
    int TotalMembers,
    int CompleteRecords,
    IReadOnlyList<CompletenessIssue> Issues);

/// <summary>
/// <paramref name="Count"/> is every affected member; <paramref name="Members"/> is capped at
/// ReportLimits.MaxMembersPerList. A client must render the count, never Members.Count.
/// </summary>
public sealed record CompletenessIssue(
    string Code, int Count, IReadOnlyList<MemberRef> Members);

/// <summary>
/// Stable codes, translated client-side like every other code in this API. There is
/// deliberately no orphaned-parent code: the composite self-FK on
/// (parent_id, family_tree_id) makes an unresolvable parent link unrepresentable, and an
/// issue that can never fire can never be tested either (design §6).
/// </summary>
public static class CompletenessCodes
{
    public const string MissingBirthDate = "MISSING_BIRTH_DATE";
    public const string DeceasedWithoutDeathDate = "DECEASED_WITHOUT_DEATH_DATE";
}
