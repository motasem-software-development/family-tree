using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberContactDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "country_id",
                table: "family_members",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mobile_number",
                table: "family_members",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "national_id",
                table: "family_members",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whats_app_number",
                table: "family_members",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_family_members_country_id",
                table: "family_members",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_family_members_is_deceased",
                table: "family_members",
                column: "is_deceased");

            migrationBuilder.CreateIndex(
                name: "ux_family_members_tenant_national_id",
                table: "family_members",
                columns: new[] { "tenant_id", "national_id" },
                unique: true,
                filter: "national_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_member_national_id_digits",
                table: "family_members",
                sql: "national_id IS NULL OR national_id ~ '^[0-9]{9}$'");

            migrationBuilder.AddForeignKey(
                name: "fk_family_members_countries_country_id",
                table: "family_members",
                column: "country_id",
                principalTable: "countries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_family_members_countries_country_id",
                table: "family_members");

            migrationBuilder.DropIndex(
                name: "ix_family_members_country_id",
                table: "family_members");

            migrationBuilder.DropIndex(
                name: "ix_family_members_is_deceased",
                table: "family_members");

            migrationBuilder.DropIndex(
                name: "ux_family_members_tenant_national_id",
                table: "family_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_member_national_id_digits",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "country_id",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "mobile_number",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "national_id",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "whats_app_number",
                table: "family_members");
        }
    }
}
