using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShop.CatalogDbManager.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogItemBadge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Badge",
                table: "Catalog",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Badge",
                table: "Catalog");
        }
    }
}
