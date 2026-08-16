using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_family_trees_id_tenant_id",
                table: "family_trees",
                columns: new[] { "id", "tenant_id" });

            migrationBuilder.CreateTable(
                name: "family_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_tree_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_members", x => x.id);
                    table.UniqueConstraint("ak_family_members_id_family_tree_id", x => new { x.id, x.family_tree_id });
                    table.ForeignKey(
                        name: "fk_family_members_family_trees_family_tree_id_tenant_id",
                        columns: x => new { x.family_tree_id, x.tenant_id },
                        principalTable: "family_trees",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_family_members_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_family_members_family_tree_id",
                table: "family_members",
                column: "family_tree_id");

            migrationBuilder.CreateIndex(
                name: "ix_family_members_family_tree_id_name",
                table: "family_members",
                columns: new[] { "family_tree_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_family_members_family_tree_id_parent_id",
                table: "family_members",
                columns: new[] { "family_tree_id", "parent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_family_members_family_tree_id_tenant_id",
                table: "family_members",
                columns: new[] { "family_tree_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_family_members_parent_id",
                table: "family_members",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_family_members_tenant_id",
                table: "family_members",
                column: "tenant_id");

            // Design spec §3.3 — the composite self-foreign-key that makes a cross-tree parent
            // link physically unrepresentable. Added as raw DDL rather than through the fluent
            // API: EF Core's change-tracker fixup nulls a tracked dependent's optional ParentId
            // when its tracked principal is marked for deletion, even with OnDelete(Restrict)
            // configured, which silently defeats the "cannot delete a parent with children"
            // guarantee before SaveChanges ever reaches the database. See
            // FamilyMemberConfiguration for the full explanation.
            migrationBuilder.Sql(@"
                ALTER TABLE family_members
                    ADD CONSTRAINT fk_member_parent
                    FOREIGN KEY (parent_id, family_tree_id)
                    REFERENCES family_members (id, family_tree_id)
                    ON DELETE RESTRICT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE family_members
                    DROP CONSTRAINT fk_member_parent;");

            migrationBuilder.DropTable(
                name: "family_members");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_family_trees_id_tenant_id",
                table: "family_trees");
        }
    }
}
