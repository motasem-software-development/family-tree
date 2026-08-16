using Testcontainers.PostgreSql;

namespace FamilyTree.Api.IntegrationTests.Fixtures;

/// <summary>
/// One real PostgreSQL container shared by the whole test collection. Real Postgres, never the
/// in-memory provider — recursive CTEs, composite foreign keys, and transaction behavior do not
/// exist in a fake, and those are exactly what these tests verify.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("familytree_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
