using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    GoogleDriveFileId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ResourceKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SubmitterName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    JumpDocumentId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentSubmissions_JumpDocuments_JumpDocumentId",
                        column: x => x.JumpDocumentId,
                        principalTable: "JumpDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSubmissions_JumpDocumentId",
                table: "DocumentSubmissions",
                column: "JumpDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSubmissions_PendingFileId",
                table: "DocumentSubmissions",
                column: "GoogleDriveFileId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSubmissions_Status",
                table: "DocumentSubmissions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSubmissions");
        }
    }
}
