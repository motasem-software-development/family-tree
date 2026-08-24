using System.Text.RegularExpressions;
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.FamilyMembers;

/// <summary>
/// A person in the family hierarchy. Per BR-003 the root family is NOT a member — it is the
/// <c>family_trees</c> row — so a first-generation member has <c>ParentId = null</c>
/// (technical specification §10).
/// </summary>
public sealed partial class FamilyMember : Entity, ITenantOwned
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
    /// Palestinian national identification number: exactly nine digits, stored as text so a
    /// leading zero survives (specification §4). Null when unknown, which is the norm for the
    /// imported tree. Uniqueness is per-tenant and enforced by a filtered database index, not
    /// here — this aggregate cannot see its siblings.
    /// </summary>
    public string? NationalId { get; private set; }

    /// <summary>Normalized E.164, dialing code included. Null when unknown.</summary>
    public string? MobileNumber { get; private set; }

    /// <summary>
    /// Normalized E.164. Deliberately independent of <see cref="MobileNumber"/>: the number a
    /// person uses for WhatsApp is often not the one they answer calls on (specification §6).
    /// </summary>
    public string? WhatsAppNumber { get; private set; }

    /// <summary>
    /// References the system-level countries table. Not a navigation property: the aggregate
    /// has no business reading country names, and the reference is resolved at the read model.
    /// </summary>
    public int? CountryId { get; private set; }

    /// <summary>
    /// Application-managed optimistic concurrency token (design spec §3.1). Mapped as an EF
    /// concurrency token, so a stale update fails loudly instead of silently overwriting a
    /// concurrent edit (technical specification §43).
    /// </summary>
    public int Version { get; private set; }

    public static FamilyMember Create(
        Guid tenantId, Guid familyTreeId, Guid? parentId, string name, DateTimeOffset now,
        DateOnly? dateOfBirth = null, DateOnly? dateOfDeath = null, bool isDeceased = false,
        ContactDetails contact = default)
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
        member.ApplyContactDetails(contact);
        member.InitializeTimestamps(now);
        return member;
    }

    /// <summary>
    /// The single edit command behind the update endpoint. Name, life details, and contact
    /// details move together because one form submission is one edit: bumping
    /// <see cref="Version"/> more than once for a single save would leave the version returned
    /// to the client already stale against its own write.
    /// </summary>
    public void Update(
        string name, DateOnly? dateOfBirth, DateOnly? dateOfDeath, bool isDeceased,
        ContactDetails contact, DateTimeOffset now)
    {
        // Validate everything before mutating anything: a rejected update must leave the
        // entity exactly as it was, version included.
        var validatedName = ValidateName(name);
        var life = ValidateLifeDetails(dateOfBirth, dateOfDeath, isDeceased, now);
        var validatedContact = ValidateContactDetails(contact);

        Name = validatedName;
        DateOfBirth = life.DateOfBirth;
        DateOfDeath = life.DateOfDeath;
        IsDeceased = life.IsDeceased;
        NationalId = validatedContact.NationalId;
        MobileNumber = validatedContact.MobileNumber;
        WhatsAppNumber = validatedContact.WhatsAppNumber;
        CountryId = validatedContact.CountryId;
        Version++;
        Touch(now);
    }

    /// <summary>
    /// Back-compatibility overload for callers that only ever knew about life details.
    /// Delegates to the six-parameter <see cref="Update"/> with the member's current contact
    /// details threaded through, so an old-style call preserves contact data instead of
    /// wiping it — the same threading pattern <see cref="Rename"/> uses.
    /// </summary>
    public void Update(
        string name, DateOnly? dateOfBirth, DateOnly? dateOfDeath, bool isDeceased, DateTimeOffset now) =>
        Update(
            name, dateOfBirth, dateOfDeath, isDeceased,
            new ContactDetails(NationalId, MobileNumber, WhatsAppNumber, CountryId), now);

    /// <summary>
    /// Changes only the name, leaving the life and contact details as they are. A delegate to
    /// <see cref="Update"/> rather than its own validate-then-mutate block, so there is exactly
    /// one path through the member's write rules and no way for the two to drift apart.
    /// </summary>
    public void Rename(string name, DateTimeOffset now) =>
        Update(
            name, DateOfBirth, DateOfDeath, IsDeceased,
            new ContactDetails(NationalId, MobileNumber, WhatsAppNumber, CountryId), now);

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

    private void ApplyContactDetails(ContactDetails contact)
    {
        var validated = ValidateContactDetails(contact);
        NationalId = validated.NationalId;
        MobileNumber = validated.MobileNumber;
        WhatsAppNumber = validated.WhatsAppNumber;
        CountryId = validated.CountryId;
    }

    /// <summary>
    /// Validates and normalizes the contact details, returning the value to store. Blank is
    /// normalized to null throughout: a form submits "" for an untouched optional field, and
    /// storing that would make "empty string" and "unknown" two different states of the same
    /// fact.
    ///
    /// Dial-code agreement is NOT checked here. It needs the country's dial code, which lives
    /// in the countries table, and this aggregate cannot read the database —
    /// FamilyMemberService applies that check and raises the same MEMBER_PHONE_INVALID code
    /// (design §5.4, refined).
    /// </summary>
    private static ContactDetails ValidateContactDetails(ContactDetails contact) => new(
        ValidateNationalId(contact.NationalId),
        ValidatePhone(contact.MobileNumber),
        ValidatePhone(contact.WhatsAppNumber),
        contact.CountryId);

    private static string? ValidateNationalId(string? nationalId)
    {
        var trimmed = nationalId?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        if (!NationalIdPattern().IsMatch(trimmed))
            throw new DomainException(
                "MEMBER_NATIONAL_ID_INVALID", "A national ID must be exactly 9 digits.");

        // Returned exactly as matched, never reformatted: specification §4.2 requires the value
        // to be preserved as entered, and a leading zero is meaningful.
        return trimmed;
    }

    private static string? ValidatePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        // Spaces, dashes and parentheses are how people write phone numbers and how a pasted
        // number arrives; E.164 has no room for them. Stripping before validating accepts the
        // human form and stores the canonical one.
        var normalized = PhoneSeparators().Replace(phone.Trim(), string.Empty);

        if (!E164Pattern().IsMatch(normalized))
            throw new DomainException(
                "MEMBER_PHONE_INVALID",
                "A phone number must be in international format, e.g. +970599123456.");

        return normalized;
    }

    [GeneratedRegex("^[0-9]{9}$")]
    private static partial Regex NationalIdPattern();

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Pattern();

    [GeneratedRegex(@"[\s\-()]")]
    private static partial Regex PhoneSeparators();
}
