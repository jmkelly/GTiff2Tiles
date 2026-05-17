using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GTiff2Tiles.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogStorageProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorageProvider",
                table: "Catalogs",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Local");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageProvider",
                table: "Catalogs");
        }
    }
}
