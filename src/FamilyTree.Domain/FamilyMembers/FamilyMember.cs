using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.FamilyMembers;

/// <summary>
/// A person in the family hierarchy. Per BR-003 the root family is NOT a member — it is the
/// <c>family_trees</c> row — so a first-generation member has <c>ParentId = null</c>
/// (technical specification §10).
/// </summary>
public sealed class FamilyMember : Entity, ITenantOwned
{
    public const int MaxNameLength = 200;

    private FamilyMember() { }

    public Guid TenantId { get; private set; }
    public Guid FamilyTreeId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>Calendar date, Gregorian. Null when unknown — which is the norm for imported records.</summary>
    public DateOnly? DateOfBirth { get; private set; }

    /// <summary>Calendar date, Gregorian. Null when unknown, including for a member known to have died.</summary>
    public DateOnly? DateOfDeath { get; private set; }

    /// <summary>
    /// Deliberately an explicit flag rather than <c>DateOfDeath is not null</c>. Genealogy
    /// records routinely establish that someone has died while the date itself is lost, and
    /// deriving the status would silently record every such ancestor as still living.
    /// </summary>
    public bool IsDeceased { get; private set; }

    /// <summary>
    /// Application-managed optimistic concurrency token (design spec §3.1). Mapped as an EF
    /// concurrency token, so a stale update fails loudly instead of silently overwriting a
    /// concurrent edit (technical specification §43).
    /// </summary>
    public int Version { get; private set; }

    public static FamilyMember Create(
        Guid tenantId, Guid familyTreeId, Guid? parentId, string name, DateTimeOffset now,
        DateOnly? dateOfBirth = null, DateOnly? dateOfDeath = null, bool isDeceased = false)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("MEMBER_TENANT_REQUIRED", "A family member must belong to a tenant.");
        if (familyTreeId == Guid.Empty)
            throw new DomainException("MEMBER_TREE_REQUIRED", "A family member must belong to a family tree.");

        var member = new FamilyMember
        {
            TenantId = tenantId,
            FamilyTreeId = familyTreeId,
            // Guid.Empty is never a real member id, so treat it as "no parent" rather than
            // letting it reach the database and fail a foreign key at insert time.
            ParentId = parentId == Guid.Empty ? null : parentId,
            Version = 1
        };
        member.Name = ValidateName(name);
        member.ApplyLifeDetails(dateOfBirth, dateOfDeath, isDeceased, now);
        member.InitializeTimestamps(now);
        return member;
    }

    /// <summary>
    /// The single edit command behind the update endpoint. Name and life details move together
    /// because one form submission is one edit: bumping <see cref="Version"/> twice for a
    /// single save would leave the version returned to the client already stale against its
    /// own write.
    /// </summary>
    public void Update(
        string name, DateOnly? dateOfBirth, DateOnly? dateOfDeath, bool isDeceased, DateTimeOffset now)
    {
        // Validate everything before mutating anything: a rejected update must leave the
        // entity exactly as it was, version included.
        var validatedName = ValidateName(name);
        var life = ValidateLifeDetails(dateOfBirth, dateOfDeath, isDeceased, now);

        Name = validatedName;
        DateOfBirth = life.DateOfBirth;
        DateOfDeath = life.DateOfDeath;
        IsDeceased = life.IsDeceased;
        Version++;
        Touch(now);
    }

    /// <summary>
    /// Changes only the name, leaving the life details as they are. A delegate to
    /// <see cref="Update"/> rather than its own validate-then-mutate block, so there is exactly
    /// one path through the member's write rules and no way for the two to drift apart.
    /// </summary>
    public void Rename(string name, DateTimeOffset now) =>
        Update(name, DateOfBirth, DateOfDeath, IsDeceased, now);

    /// <summary>
    /// Re-parents the member. A null <paramref name="newParentId"/> promotes them to first
    /// generation, attached to the family tree rather than to a member (BR-003).
    ///
    /// Only the self-loop is caught here. A deeper cycle needs the ancestor chain, which this
    /// entity cannot see — Infrastructure's recursive CTE owns those (design §3.1). Validation
    /// precedes mutation, so a refused move leaves the member exactly as it was.
    /// </summary>
    public void MoveTo(Guid? newParentId, DateTimeOffset now)
    {
        // Same normalization as Create: Guid.Empty is never a real member id, and letting it
        // through would fail a foreign key at write time instead of recording "no parent".
        var parentId = newParentId == Guid.Empty ? null : newParentId;

        if (parentId == Id)
            throw new DomainException(
                "MOVE_CREATES_CYCLE", "A member cannot be their own parent.");

        ParentId = parentId;
        Version++;
        Touch(now);
    }

    private void ApplyLifeDetails(
        DateOnly? dateOfBirth, DateOnly? dateOfDeath, bool isDeceased, DateTimeOffset now)
    {
        var life = ValidateLifeDetails(dateOfBirth, dateOfDeath, isDeceased, now);
        DateOfBirth = life.DateOfBirth;
        DateOfDeath = life.DateOfDeath;
        IsDeceased = life.IsDeceased;
    }

    /// <summary>
    /// Validates the life details and returns the normalized triple. A death date implies the
    /// deceased flag: a caller supplying one has stated the fact, and storing the date next to
    /// "still living" would make the row contradict itself.
    /// </summary>
    private static (DateOnly? DateOfBirth, DateOnly? DateOfDeath, bool IsDeceased) ValidateLifeDetails(
        DateOnly? dateOfBirth, DateOnly? dateOfDeath, bool isDeceased, DateTimeOffset now)
    {
        // "Today" in UTC. Members are recorded from many time zones and a calendar date has no
        // zone of its own, so a single server-side reference day is the only stable bound —
        // and it is inclusive, because a newborn recorded on the day of birth must be accepted.
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        if (dateOfBirth > today || dateOfDeath > today)
            throw new DomainException("MEMBER_DATE_IN_FUTURE", "A birth or death date cannot be in the future.");

        if (dateOfBirth is { } born && dateOfDeath is { } died && died < born)
            throw new DomainException(
                "MEMBER_DEATH_BEFORE_BIRTH", "A death date cannot be earlier than the birth date.");

        return (dateOfBirth, dateOfDeath, isDeceased || dateOfDeath is not null);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("MEMBER_NAME_REQUIRED", "Member name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException("MEMBER_NAME_TOO_LONG", $"Member name exceeds {MaxNameLength} characters.");
        return trimmed;
    }
}
