namespace FamilyTree.Contracts.Reports;

/// <summary>
/// A member as a report row identifies one. <paramref name="ParentId"/> is carried instead of
/// a composed full name because identity in this model comes from the lineage, and the SPA
/// already owns that rule in fullName.ts — see design §7. Composing it here would put the same
/// rule in two languages.
/// </summary>
/// <remarks>
/// No <c>From(FamilyMember)</c> factory here: <c>FamilyTree.Contracts</c> does not reference
/// <c>FamilyTree.Domain</c> (verified via the project file, which has no ProjectReference
/// entries at all), so each calculator maps a <c>FamilyMember</c> to this record itself.
/// </remarks>
public sealed record MemberRef(Guid Id, string Name, Guid? ParentId);
