using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Authorization;

public sealed class Role : Entity, ITenantOwned
{
    public const int MaxNameLength = 100;

    private Role() { }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>Seeded roles cannot be renamed or deleted, so a tenant cannot lock itself out.</summary>
    public bool IsSystem { get; private set; }

    public static Role Create(Guid tenantId, string name, string? description, DateTimeOffset now) =>
        Build(tenantId, name, description, isSystem: false, now);

    public static Role CreateSystem(Guid tenantId, string name, string? description, DateTimeOffset now) =>
        Build(tenantId, name, description, isSystem: true, now);

    public void Rename(string name, DateTimeOffset now)
    {
        EnsureNotSystem();
        Name = ValidateName(name);
        Touch(now);
    }

    /// <summary>
    /// Replaces the editable state of a custom role. Name and description are replaced
    /// together because the edit form submits both — a partial update would need a merge rule
    /// the API deliberately does not have (design spec §4.9).
    /// </summary>
    public void Update(string name, string? description, DateTimeOffset now)
    {
        EnsureNotSystem();
        Name = ValidateName(name);
        Description = description;
        Touch(now);
    }

    /// <summary>Throws when the role is system-owned. Guards all modification paths including rename and delete.</summary>
    public void EnsureNotSystem()
    {
        if (IsSystem)
            throw new DomainException("ROLE_IS_SYSTEM", "System roles cannot be modified or deleted.");
    }

    private static Role Build(Guid tenantId, string name, string? description, bool isSystem, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("ROLE_TENANT_REQUIRED", "A role must belong to a tenant.");

        var role = new Role { TenantId = tenantId, Description = description, IsSystem = isSystem };
        role.Name = ValidateName(name);
        role.InitializeTimestamps(now);
        return role;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("ROLE_NAME_REQUIRED", "Role name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException("ROLE_NAME_TOO_LONG", $"Role name exceeds {MaxNameLength} characters.");
        return trimmed;
    }
}
