using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Api.Migrations
{
    /// <inheritdoc />
    public partial class _0005_FixCatalogFKs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stores_StoreCategories_CategoryId",
                schema: "SHOP",
                table: "Stores");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId1",
                schema: "SHOP",
                table: "Stores",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_CategoryId1",
                schema: "SHOP",
                table: "Stores",
                column: "CategoryId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_StoreCategories_CategoryId",
                schema: "SHOP",
                table: "Stores",
                column: "CategoryId",
                principalSchema: "SHOP",
                principalTable: "StoreCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_StoreCategories_CategoryId1",
                schema: "SHOP",
                table: "Stores",
                column: "CategoryId1",
                principalSchema: "SHOP",
                principalTable: "StoreCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stores_StoreCategories_CategoryId",
                schema: "SHOP",
                table: "Stores");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_StoreCategories_CategoryId1",
                schema: "SHOP",
                table: "Stores");

            migrationBuilder.DropIndex(
                name: "IX_Stores_CategoryId1",
                schema: "SHOP",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "CategoryId1",
                schema: "SHOP",
                table: "Stores");

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_StoreCategories_CategoryId",
                schema: "SHOP",
                table: "Stores",
                column: "CategoryId",
                principalSchema: "SHOP",
                principalTable: "StoreCategories",
                principalColumn: "Id");
        }
    }
}
