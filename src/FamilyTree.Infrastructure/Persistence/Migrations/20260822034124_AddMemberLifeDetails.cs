using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberLifeDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "family_members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_death",
                table: "family_members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deceased",
                table: "family_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_member_death_after_birth",
                table: "family_members",
                sql: "date_of_death IS NULL OR date_of_birth IS NULL OR date_of_death >= date_of_birth");

            migrationBuilder.AddCheckConstraint(
                name: "ck_member_death_date_implies_deceased",
                table: "family_members",
                sql: "date_of_death IS NULL OR is_deceased");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_member_death_after_birth",
                table: "family_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_member_death_date_implies_deceased",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "date_of_death",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "is_deceased",
                table: "family_members");
        }
    }
}
