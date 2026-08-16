using System.Text;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Common;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Persistence;

/// <summary>
/// Pins the invariant that every entity implementing <see cref="ITenantOwned"/> has a global
/// query filter registered on it in <c>ApplicationDbContext.OnModelCreating</c>. The filters
/// themselves are a hand-maintained list; this test is the signal that stops a future entity
/// (e.g. a Phase 2 <c>FamilyMember : ITenantOwned</c>) from shipping unfiltered and silently
/// leaking rows across tenants.
/// </summary>
public sealed class QueryFilterInvariantTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Every_tenant_owned_entity_has_a_query_filter()
    {
        await using var context = ContextFor(Guid.Empty);
        var model = context.Model;

        var offenders = new StringBuilder();

        foreach (var entityType in model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (!typeof(ITenantOwned).IsAssignableFrom(clrType)) continue;

            if (entityType.GetDeclaredQueryFilters().Count == 0)
                offenders.AppendLine($"{clrType.FullName} implements ITenantOwned but has no HasQueryFilter().");
        }

        offenders.Length.Should().Be(0, offenders.ToString());
    }
}
