using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GTiff2Tiles.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddStoragePresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoragePresets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LocalRootPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    S3BucketName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    S3Region = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    S3AccessKeyId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    S3SecretAccessKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    S3EndpointUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    S3LocalCacheRoot = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoragePresets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoragePresets_Name",
                table: "StoragePresets",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoragePresets");
        }
    }
}
