using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNameTrigramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Design spec §3.4, deferred here from Phase 2 (deviation 1) because the index
            // exists only to serve the search endpoint, which ships in this phase.
            //
            // CREATE EXTENSION requires rights beyond those of a plain application role. The
            // Testcontainers image runs as superuser so this is invisible in tests; a deployed
            // database may need a DBA to install pg_trgm out of band. IF NOT EXISTS makes the
            // pre-installed case a no-op rather than a failed deploy. See README.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // GIN over trigrams is what makes an unanchored ILIKE '%…%' indexable at all — a
            // btree index cannot serve a leading wildcard. The existing btree on
            // (family_tree_id, name) stays: it serves ordering and exact lookups, which
            // trigrams do not.
            migrationBuilder.Sql(@"
                CREATE INDEX ix_family_members_name_trgm
                    ON family_members
                    USING gin (name gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_family_members_name_trgm;");

            // The extension is deliberately NOT dropped. Another database object could depend
            // on it, and dropping a shared extension during a rollback is a wider blast radius
            // than the index this migration owns.
        }
    }
}
