using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DriveConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DriveId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DriveName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastScanTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DocumentCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriveConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JumpDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GoogleDriveFileId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScanned = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceDrive = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FolderPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    WebViewLink = table.Column<string>(type: "TEXT", nullable: false),
                    DownloadLink = table.Column<string>(type: "TEXT", nullable: false),
                    ExtractedText = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JumpDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JumpDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TagCategory = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTags_JumpDocuments_JumpDocumentId",
                        column: x => x.JumpDocumentId,
                        principalTable: "JumpDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTags_JumpDocumentId_TagName",
                table: "DocumentTags",
                columns: new[] { "JumpDocumentId", "TagName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTags_TagCategory",
                table: "DocumentTags",
                column: "TagCategory");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTags_TagName",
                table: "DocumentTags",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_DriveConfigurations_DriveId",
                table: "DriveConfigurations",
                column: "DriveId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriveConfigurations_DriveName",
                table: "DriveConfigurations",
                column: "DriveName");

            migrationBuilder.CreateIndex(
                name: "IX_JumpDocuments_GoogleDriveFileId",
                table: "JumpDocuments",
                column: "GoogleDriveFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JumpDocuments_Name",
                table: "JumpDocuments",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_JumpDocuments_SourceDrive_Name",
                table: "JumpDocuments",
                columns: new[] { "SourceDrive", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTags");

            migrationBuilder.DropTable(
                name: "DriveConfigurations");

            migrationBuilder.DropTable(
                name: "JumpDocuments");
        }
    }
}
