using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Api.Migrations
{
    /// <inheritdoc />
    public partial class _0007_StoreCategorySortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                schema: "SHOP",
                table: "StoreCategories",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                schema: "SHOP",
                table: "StoreCategories");
        }
    }
}
