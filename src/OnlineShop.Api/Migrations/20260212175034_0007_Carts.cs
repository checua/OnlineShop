using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Api.Migrations
{
    /// <inheritdoc />
    public partial class _0007_Carts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Stores_StoreId",
                schema: "SHOP",
                table: "ProductCategories");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_ProductId_Sku",
                schema: "SHOP",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Products_StoreId_Name",
                schema: "SHOP",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductImages_ProductId_SortOrder",
                schema: "SHOP",
                table: "ProductImages");

            migrationBuilder.DropIndex(
                name: "IX_ProductCategories_StoreId_Name",
                schema: "SHOP",
                table: "ProductCategories");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "SHOP",
                table: "Stores",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<int>(
                name: "SortOrder",
                schema: "SHOP",
                table: "StoreCategories",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "SHOP",
                table: "StoreCategories",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                schema: "SHOP",
                table: "ProductVariants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Size",
                schema: "SHOP",
                table: "ProductVariants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                schema: "SHOP",
                table: "ProductVariants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "SHOP",
                table: "Products",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                schema: "SHOP",
                table: "ProductImages",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<int>(
                name: "SortOrder",
                schema: "SHOP",
                table: "ProductImages",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "SortOrder",
                schema: "SHOP",
                table: "ProductCategories",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "Carts",
                schema: "SHOP",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    GuestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Carts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "SHOP",
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                schema: "SHOP",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VariantSku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    VariantSize = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    VariantColor = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CartItems_Carts_CartId",
                        column: x => x.CartId,
                        principalSchema: "SHOP",
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItems_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalSchema: "SHOP",
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CartItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "SHOP",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategories_Name",
                schema: "SHOP",
                table: "StoreCategories",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                schema: "SHOP",
                table: "ProductVariants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId",
                schema: "SHOP",
                table: "Products",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId_IsActive",
                schema: "SHOP",
                table: "Products",
                columns: new[] { "StoreId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId",
                schema: "SHOP",
                table: "ProductImages",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_StoreId_Name",
                schema: "SHOP",
                table: "ProductCategories",
                columns: new[] { "StoreId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId_ProductId_VariantId",
                schema: "SHOP",
                table: "CartItems",
                columns: new[] { "CartId", "ProductId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_ProductId",
                schema: "SHOP",
                table: "CartItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_VariantId",
                schema: "SHOP",
                table: "CartItems",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_StoreId_GuestId",
                schema: "SHOP",
                table: "Carts",
                columns: new[] { "StoreId", "GuestId" },
                unique: true,
                filter: "[GuestId] IS NOT NULL AND [Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_StoreId_UserId",
                schema: "SHOP",
                table: "Carts",
                columns: new[] { "StoreId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL AND [Status] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_Stores_StoreId",
                schema: "SHOP",
                table: "ProductCategories",
                column: "StoreId",
                principalSchema: "SHOP",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Stores_StoreId",
                schema: "SHOP",
                table: "ProductCategories");

            migrationBuilder.DropTable(
                name: "CartItems",
                schema: "SHOP");

            migrationBuilder.DropTable(
                name: "Carts",
                schema: "SHOP");

            migrationBuilder.DropIndex(
                name: "IX_StoreCategories_Name",
                schema: "SHOP",
                table: "StoreCategories");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_ProductId",
                schema: "SHOP",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Products_StoreId",
                schema: "SHOP",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_StoreId_IsActive",
                schema: "SHOP",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductImages_ProductId",
                schema: "SHOP",
                table: "ProductImages");

            migrationBuilder.DropIndex(
                name: "IX_ProductCategories_StoreId_Name",
                schema: "SHOP",
                table: "ProductCategories");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "SHOP",
                table: "Stores",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<int>(
                name: "SortOrder",
                schema: "SHOP",
                table: "StoreCategories",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "SHOP",
                table: "StoreCategories",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                schema: "SHOP",
                table: "ProductVariants",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Size",
                schema: "SHOP",
                table: "ProductVariants",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                schema: "SHOP",
                table: "ProductVariants",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "SHOP",
                table: "Products",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                schema: "SHOP",
                table: "ProductImages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<int>(
                name: "SortOrder",
                schema: "SHOP",
                table: "ProductImages",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "SortOrder",
                schema: "SHOP",
                table: "ProductCategories",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_Sku",
                schema: "SHOP",
                table: "ProductVariants",
                columns: new[] { "ProductId", "Sku" },
                unique: true,
                filter: "[Sku] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId_Name",
                schema: "SHOP",
                table: "Products",
                columns: new[] { "StoreId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId_SortOrder",
                schema: "SHOP",
                table: "ProductImages",
                columns: new[] { "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_StoreId_Name",
                schema: "SHOP",
                table: "ProductCategories",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_Stores_StoreId",
                schema: "SHOP",
                table: "ProductCategories",
                column: "StoreId",
                principalSchema: "SHOP",
                principalTable: "Stores",
                principalColumn: "Id");
        }
    }
}
