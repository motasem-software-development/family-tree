using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantForeignKeysAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "email_index",
                table: "asp_net_users");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_tenant_id",
                table: "refresh_tokens",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "email_index",
                table: "asp_net_users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_tenant_id",
                table: "asp_net_users",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_users_tenants_tenant_id",
                table: "asp_net_users",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_tenants_tenant_id",
                table: "refresh_tokens",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_users_tenants_tenant_id",
                table: "asp_net_users");

            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_tenants_tenant_id",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_tenant_id",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "email_index",
                table: "asp_net_users");

            migrationBuilder.DropIndex(
                name: "ix_asp_net_users_tenant_id",
                table: "asp_net_users");

            migrationBuilder.CreateIndex(
                name: "email_index",
                table: "asp_net_users",
                column: "normalized_email");
        }
    }
}
