using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Api.Migrations
{
    /// <inheritdoc />
    public partial class _0006_FixShadowColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Stores_StoreId",
                schema: "SHOP",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Stores_StoreId",
                schema: "SHOP",
                table: "Products");

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
                name: "FK_ProductCategories_Stores_StoreId",
                schema: "SHOP",
                table: "ProductCategories",
                column: "StoreId",
                principalSchema: "SHOP",
                principalTable: "Stores",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Stores_StoreId",
                schema: "SHOP",
                table: "Products",
                column: "StoreId",
                principalSchema: "SHOP",
                principalTable: "Stores",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Stores_StoreId",
                schema: "SHOP",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Stores_StoreId",
                schema: "SHOP",
                table: "Products");

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
                name: "FK_ProductCategories_Stores_StoreId",
                schema: "SHOP",
                table: "ProductCategories",
                column: "StoreId",
                principalSchema: "SHOP",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Stores_StoreId",
                schema: "SHOP",
                table: "Products",
                column: "StoreId",
                principalSchema: "SHOP",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_StoreCategories_CategoryId1",
                schema: "SHOP",
                table: "Stores",
                column: "CategoryId1",
                principalSchema: "SHOP",
                principalTable: "StoreCategories",
                principalColumn: "Id");
        }
    }
}
