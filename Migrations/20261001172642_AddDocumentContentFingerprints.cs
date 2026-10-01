using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentContentFingerprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BinaryContentHash",
                table: "JumpDocuments",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextContentHash",
                table: "JumpDocuments",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BinaryContentHash",
                table: "DocumentUrls",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateReason",
                table: "DocumentSubmissions",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JumpDocuments_BinaryContentHash",
                table: "JumpDocuments",
                column: "BinaryContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_JumpDocuments_TextContentHash",
                table: "JumpDocuments",
                column: "TextContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentUrls_BinaryContentHash",
                table: "DocumentUrls",
                column: "BinaryContentHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JumpDocuments_BinaryContentHash",
                table: "JumpDocuments");

            migrationBuilder.DropIndex(
                name: "IX_JumpDocuments_TextContentHash",
                table: "JumpDocuments");

            migrationBuilder.DropIndex(
                name: "IX_DocumentUrls_BinaryContentHash",
                table: "DocumentUrls");

            migrationBuilder.DropColumn(
                name: "BinaryContentHash",
                table: "JumpDocuments");

            migrationBuilder.DropColumn(
                name: "TextContentHash",
                table: "JumpDocuments");

            migrationBuilder.DropColumn(
                name: "BinaryContentHash",
                table: "DocumentUrls");

            migrationBuilder.DropColumn(
                name: "DuplicateReason",
                table: "DocumentSubmissions");
        }
    }
}
