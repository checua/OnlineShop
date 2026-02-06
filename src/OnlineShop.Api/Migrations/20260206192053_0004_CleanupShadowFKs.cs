using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Api.Migrations
{
    /// <inheritdoc />
    public partial class _0004_CleanupShadowFKs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Stores_StoreId1",
                schema: "SHOP",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductCategories_CategoryId1",
                schema: "SHOP",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Stores_StoreId1",
                schema: "SHOP",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_CategoryId1",
                schema: "SHOP",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_StoreId1",
                schema: "SHOP",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductCategories_StoreId1",
                schema: "SHOP",
                table: "ProductCategories");

            migrationBuilder.DropColumn(
                name: "CategoryId1",
                schema: "SHOP",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StoreId1",
                schema: "SHOP",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StoreId1",
                schema: "SHOP",
                table: "ProductCategories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId1",
                schema: "SHOP",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoreId1",
                schema: "SHOP",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoreId1",
                schema: "SHOP",
                table: "ProductCategories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId1",
                schema: "SHOP",
                table: "Products",
                column: "CategoryId1");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId1",
                schema: "SHOP",
                table: "Products",
                column: "StoreId1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_StoreId1",
                schema: "SHOP",
                table: "ProductCategories",
                column: "StoreId1");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_Stores_StoreId1",
                schema: "SHOP",
                table: "ProductCategories",
                column: "StoreId1",
                principalSchema: "SHOP",
                principalTable: "Stores",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductCategories_CategoryId1",
                schema: "SHOP",
                table: "Products",
                column: "CategoryId1",
                principalSchema: "SHOP",
                principalTable: "ProductCategories",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Stores_StoreId1",
                schema: "SHOP",
                table: "Products",
                column: "StoreId1",
                principalSchema: "SHOP",
                principalTable: "Stores",
                principalColumn: "Id");
        }
    }
}
