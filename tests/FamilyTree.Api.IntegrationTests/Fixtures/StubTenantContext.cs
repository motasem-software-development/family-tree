using FamilyTree.Application.Common;

namespace FamilyTree.Api.IntegrationTests.Fixtures;

public sealed class StubTenantContext(Guid tenantId, Guid userId) : ITenantContext
{
    public Guid TenantId { get; } = tenantId;
    public Guid UserId { get; } = userId;
    public bool IsAuthenticated => TenantId != Guid.Empty;
}
