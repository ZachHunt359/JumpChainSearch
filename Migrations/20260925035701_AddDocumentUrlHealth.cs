using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentUrlHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDead",
                table: "DocumentUrls",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHealthCheckAt",
                table: "DocumentUrls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastHealthCheckMessage",
                table: "DocumentUrls",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastHealthCheckStatus",
                table: "DocumentUrls",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReportedAt",
                table: "DocumentUrls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceKey",
                table: "DocumentUrls",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql(
                """
                INSERT OR IGNORE INTO DocumentUrls
                    (JumpDocumentId, GoogleDriveFileId, SourceDrive, FolderPath, WebViewLink, DownloadLink, LastScanned, IsDead, LastHealthCheckStatus)
                SELECT
                    Id, GoogleDriveFileId, SourceDrive, FolderPath, WebViewLink, DownloadLink, LastScanned, 0, 'Unknown'
                FROM JumpDocuments
                WHERE GoogleDriveFileId IS NOT NULL AND GoogleDriveFileId <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDead",
                table: "DocumentUrls");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckAt",
                table: "DocumentUrls");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckMessage",
                table: "DocumentUrls");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckStatus",
                table: "DocumentUrls");

            migrationBuilder.DropColumn(
                name: "LastReportedAt",
                table: "DocumentUrls");

            migrationBuilder.DropColumn(
                name: "ResourceKey",
                table: "DocumentUrls");
        }
    }
}
