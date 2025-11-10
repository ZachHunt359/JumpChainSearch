using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovedTagRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovedTagRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GoogleDriveFileId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DocumentName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TagName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TagCategory = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RuleType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ApprovalSource = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TagSuggestionId = table.Column<int>(type: "INTEGER", nullable: true),
                    TagRemovalRequestId = table.Column<int>(type: "INTEGER", nullable: true),
                    VotesInFavor = table.Column<int>(type: "INTEGER", nullable: true),
                    TotalVotes = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastAppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TimesApplied = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovedTagRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovedTagRules_TagRemovalRequests_TagRemovalRequestId",
                        column: x => x.TagRemovalRequestId,
                        principalTable: "TagRemovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApprovedTagRules_TagSuggestions_TagSuggestionId",
                        column: x => x.TagSuggestionId,
                        principalTable: "TagSuggestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedTagRules_ApprovalSource",
                table: "ApprovedTagRules",
                column: "ApprovalSource");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedTagRules_GoogleDriveFileId",
                table: "ApprovedTagRules",
                column: "GoogleDriveFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedTagRules_GoogleDriveFileId_TagName_TagCategory_RuleType",
                table: "ApprovedTagRules",
                columns: new[] { "GoogleDriveFileId", "TagName", "TagCategory", "RuleType" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedTagRules_IsActive",
                table: "ApprovedTagRules",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedTagRules_RuleType",
                table: "ApprovedTagRules",
                column: "RuleType");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedTagRules_TagRemovalRequestId",
                table: "ApprovedTagRules",
                column: "TagRemovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedTagRules_TagSuggestionId",
                table: "ApprovedTagRules",
                column: "TagSuggestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovedTagRules");
        }
    }
}
