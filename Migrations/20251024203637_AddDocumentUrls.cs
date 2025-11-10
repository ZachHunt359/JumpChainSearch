using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentUrls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JumpDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    GoogleDriveFileId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SourceDrive = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FolderPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    WebViewLink = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DownloadLink = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    LastScanned = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentUrls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentUrls_JumpDocuments_JumpDocumentId",
                        column: x => x.JumpDocumentId,
                        principalTable: "JumpDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentUrls_GoogleDriveFileId",
                table: "DocumentUrls",
                column: "GoogleDriveFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentUrls_JumpDocumentId",
                table: "DocumentUrls",
                column: "JumpDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentUrls_JumpDocumentId_SourceDrive",
                table: "DocumentUrls",
                columns: new[] { "JumpDocumentId", "SourceDrive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentUrls");
        }
    }
}
