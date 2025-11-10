using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddThumbnailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasThumbnail",
                table: "JumpDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailLink",
                table: "JumpDocuments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasThumbnail",
                table: "JumpDocuments");

            migrationBuilder.DropColumn(
                name: "ThumbnailLink",
                table: "JumpDocuments");
        }
    }
}
