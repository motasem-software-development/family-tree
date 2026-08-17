namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// A page of search hits plus the true match count.
/// </summary>
/// <param name="Total">
/// Every match, independent of <c>Items.Count</c>. This field exists because the client
/// previously reported the size of its own truncated list — "8 نتائج" when 39 members matched.
/// </param>
public sealed record FamilyMemberSearchResponse(int Total, IReadOnlyList<FamilyMemberSearchHit> Items);
