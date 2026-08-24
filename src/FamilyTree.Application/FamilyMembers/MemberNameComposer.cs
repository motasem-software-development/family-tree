namespace FamilyTree.Application.FamilyMembers;

/// <summary>Just enough of a member to walk the chain, so the helper is usable from any shape.</summary>
public readonly record struct NamedMember(string Name, Guid? ParentId);

/// <summary>
/// A member record stores one given name (داوود, طالب); the rest of the name is the lineage,
/// which the tree already holds as the parent chain. Composing the two is what turns a row into
/// the name a family actually uses.
///
/// The server-side twin of <c>nameParts</c> in
/// <c>frontend/src/features/members/fullName.ts</c> — the export composes the same name the
/// members list shows on screen. The two are separate implementations of one rule, so a change
/// to either belongs in both.
///
/// It deliberately does <b>not</b> append the family/tree name, matching the frontend. Design
/// spec §7.3 suggests it; doing so would make every exported name differ from the name the same
/// user just read on the page.
/// </summary>
public static class MemberNameComposer
{
    /// <summary>
    /// Own name, father, grandfather, great-grandfather. Four is the customary length of an
    /// Arabic name, not a limit of the data: the walk stops there even when the tree goes deeper.
    /// </summary>
    public const int MaxParts = 4;

    /// <summary>
    /// The composed name, own name first, parts joined with a single space.
    ///
    /// Shorter than four near the root — a first-generation member has no lineage to append, and
    /// padding it would invent ancestors. Returns the empty string for an id the map does not
    /// hold.
    /// </summary>
    public static string Compose(Guid id, IReadOnlyDictionary<Guid, NamedMember> byId) =>
        string.Join(' ', Parts(id, byId));

    /// <summary>
    /// The name parts, own name first.
    ///
    /// The walk stops on a missing lookup rather than throwing: a member whose parent is absent
    /// from the map — a filtered list, a mid-flight delete — still has a name worth showing.
    /// Bounded by <see cref="MaxParts"/>, so a cyclic parent chain cannot loop here; cycles are
    /// impossible through the move command, which validates with a recursive CTE, but a corrupt
    /// import must produce an answer rather than a hung request.
    ///
    /// Each part is trimmed, which <c>nameParts</c> does not do. The aggregate's ValidateName
    /// already trims on write, so this is belt-and-braces for the one path that bypasses it —
    /// the bulk import — and it is what keeps specification §20's no-double-spaces rule true of
    /// the output rather than merely likely.
    /// </summary>
    public static IReadOnlyList<string> Parts(Guid id, IReadOnlyDictionary<Guid, NamedMember> byId)
    {
        if (!byId.TryGetValue(id, out var member)) return [];

        var parts = new List<string> { member.Name.Trim() };
        var current = member;

        while (parts.Count < MaxParts && current.ParentId is { } parentId)
        {
            if (!byId.TryGetValue(parentId, out var parent)) break;
            parts.Add(parent.Name.Trim());
            current = parent;
        }

        return parts;
    }
}
