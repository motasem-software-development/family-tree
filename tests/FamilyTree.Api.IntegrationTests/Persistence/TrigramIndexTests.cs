using FamilyTree.Api.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.IntegrationTests.Persistence;

/// <summary>
/// Design spec §3.4 and Phase 2 deviation 1. The index exists solely to serve the search
/// endpoint; asserting on it here means a migration that silently fails to create the
/// extension is caught by the test suite rather than by a slow query in production.
/// </summary>
public sealed class TrigramIndexTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var context = ContextFor(Guid.Empty);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await context.Database.OpenConnectionAsync();
        try
        {
            return (T)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task The_pg_trgm_extension_is_installed()
    {
        var count = await ScalarAsync<long>(
            "SELECT count(*) FROM pg_extension WHERE extname = 'pg_trgm';");

        count.Should().Be(1);
    }

    [Fact]
    public async Task A_gin_trigram_index_covers_the_member_name()
    {
        var definition = await ScalarAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_family_members_name_trgm';");

        definition.Should().Contain("gin").And.Contain("gin_trgm_ops").And.Contain("name");
    }
}
