using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Authorization;

/// <summary>A system-level capability definition. Not tenant-owned — the catalog is global.</summary>
public sealed class Permission : Entity
{
    private Permission() { }

    public string Code { get; private set; } = null!;
    public string? Description { get; private set; }

    public static Permission Create(string code, string? description, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("PERMISSION_CODE_REQUIRED", "Permission code is required.");

        var permission = new Permission { Code = code.Trim(), Description = description };
        permission.InitializeTimestamps(now);
        return permission;
    }
}
