namespace FamilyTree.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string TenantName { get; init; } = null!;
    public string TenantSlug { get; init; } = null!;
    public string FamilyTreeName { get; init; } = null!;
    public string AdminEmail { get; init; } = null!;

    /// <summary>Supplied by environment variable or user-secrets. Never committed.</summary>
    public string AdminPassword { get; init; } = null!;
}
