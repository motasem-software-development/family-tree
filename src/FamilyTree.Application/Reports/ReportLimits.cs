namespace FamilyTree.Application.Reports;

/// <summary>
/// The reports take no query parameters (design §4), so these are the whole of their tuning.
/// Fixed rather than caller-supplied: one cacheable response shape, and no validation surface.
/// Changing a window is a code change, which is honest for V1.
/// </summary>
public static class ReportLimits
{
    public const int UpcomingWindowDays = 30;
    public const int ActivityWindowDays = 30;

    /// <summary>
    /// Caps every member-bearing list. Each such list returns its untruncated count alongside,
    /// so a truncation is always visible in the contract (design §5).
    /// </summary>
    public const int MaxMembersPerList = 50;
}
