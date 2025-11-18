using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderConfigurationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FolderConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FolderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentDriveId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PreferredAuthMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    FolderPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DocumentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastScanTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsAutoDiscovered = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FolderConfigurations_DriveConfigurations_ParentDriveId",
                        column: x => x.ParentDriveId,
                        principalTable: "DriveConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolderConfigurations_FolderId",
                table: "FolderConfigurations",
                column: "FolderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FolderConfigurations_FolderPath",
                table: "FolderConfigurations",
                column: "FolderPath");

            migrationBuilder.CreateIndex(
                name: "IX_FolderConfigurations_ParentDriveId",
                table: "FolderConfigurations",
                column: "ParentDriveId");

            migrationBuilder.CreateIndex(
                name: "IX_FolderConfigurations_ParentDriveId_FolderName",
                table: "FolderConfigurations",
                columns: new[] { "ParentDriveId", "FolderName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolderConfigurations");
        }
    }
}
