using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GTiff2Tiles.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogStorageConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorageConfig",
                table: "Catalogs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageConfig",
                table: "Catalogs");
        }
    }
}
