using System.Text.RegularExpressions;
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Tenants;

public sealed partial class Tenant : Entity
{
    public const int MaxNameLength = 200;
    public const int MaxSlugLength = 100;

    private Tenant() { }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static Tenant Create(string name, string slug, DateTimeOffset now)
    {
        var tenant = new Tenant { Slug = ValidateSlug(slug), IsActive = true };
        tenant.Name = ValidateName(name);
        tenant.InitializeTimestamps(now);
        return tenant;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = ValidateName(name);
        Touch(now);
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        Touch(now);
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        Touch(now);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("TENANT_NAME_REQUIRED", "Tenant name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException("TENANT_NAME_TOO_LONG", $"Tenant name exceeds {MaxNameLength} characters.");
        return trimmed;
    }

    private static string ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > MaxSlugLength || !SlugPattern().IsMatch(slug))
            throw new DomainException("TENANT_SLUG_INVALID", "Tenant slug must be lowercase kebab-case.");
        return slug;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
