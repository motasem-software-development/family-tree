using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Fixtures;

[Collection("postgres")]
public abstract class DatabaseTestBase(PostgresFixture fixture) : IAsyncLifetime
{
    protected static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A context scoped to one tenant. Passing Guid.Empty models an unauthenticated caller,
    /// which must see nothing.
    /// </summary>
    protected ApplicationDbContext ContextFor(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options, new StubTenantContext(tenantId, Guid.CreateVersion7()));
    }

    public async Task InitializeAsync()
    {
        await using var context = ContextFor(Guid.Empty);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
