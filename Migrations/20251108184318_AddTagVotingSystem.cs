using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddTagVotingSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentViewCounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JumpDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UniqueViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastViewed = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentViewCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentViewCounts_JumpDocuments_JumpDocumentId",
                        column: x => x.JumpDocumentId,
                        principalTable: "JumpDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagRemovalRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JumpDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentTagId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TagCategory = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagRemovalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagRemovalRequests_DocumentTags_DocumentTagId",
                        column: x => x.DocumentTagId,
                        principalTable: "DocumentTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TagRemovalRequests_JumpDocuments_JumpDocumentId",
                        column: x => x.JumpDocumentId,
                        principalTable: "JumpDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagSuggestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JumpDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TagCategory = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SuggestedByUserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagSuggestions_JumpDocuments_JumpDocumentId",
                        column: x => x.JumpDocumentId,
                        principalTable: "JumpDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTagOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    JumpDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TagCategory = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsAdded = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTagOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTagOverrides_JumpDocuments_JumpDocumentId",
                        column: x => x.JumpDocumentId,
                        principalTable: "JumpDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VotingConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MinimumVotesRequired = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiredAgreementPercentage = table.Column<double>(type: "REAL", nullable: false),
                    ScaleByPopularity = table.Column<bool>(type: "INTEGER", nullable: false),
                    PopularityScaleFactor = table.Column<double>(type: "REAL", nullable: false),
                    MaximumVotesRequired = table.Column<int>(type: "INTEGER", nullable: false),
                    VoteDecayStartDays = table.Column<int>(type: "INTEGER", nullable: false),
                    VoteDecayRatePerDay = table.Column<double>(type: "REAL", nullable: false),
                    AutoApplyEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VotingConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TagVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TagSuggestionId = table.Column<int>(type: "INTEGER", nullable: true),
                    TagRemovalRequestId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsInFavor = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Weight = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagVotes_TagRemovalRequests_TagRemovalRequestId",
                        column: x => x.TagRemovalRequestId,
                        principalTable: "TagRemovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagVotes_TagSuggestions_TagSuggestionId",
                        column: x => x.TagSuggestionId,
                        principalTable: "TagSuggestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentViewCounts_JumpDocumentId",
                table: "DocumentViewCounts",
                column: "JumpDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagRemovalRequests_DocumentTagId",
                table: "TagRemovalRequests",
                column: "DocumentTagId");

            migrationBuilder.CreateIndex(
                name: "IX_TagRemovalRequests_JumpDocumentId",
                table: "TagRemovalRequests",
                column: "JumpDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TagRemovalRequests_JumpDocumentId_TagName_TagCategory",
                table: "TagRemovalRequests",
                columns: new[] { "JumpDocumentId", "TagName", "TagCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_TagRemovalRequests_RequestedByUserId",
                table: "TagRemovalRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TagRemovalRequests_Status",
                table: "TagRemovalRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TagSuggestions_JumpDocumentId",
                table: "TagSuggestions",
                column: "JumpDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TagSuggestions_JumpDocumentId_TagName_TagCategory",
                table: "TagSuggestions",
                columns: new[] { "JumpDocumentId", "TagName", "TagCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_TagSuggestions_Status",
                table: "TagSuggestions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TagSuggestions_SuggestedByUserId",
                table: "TagSuggestions",
                column: "SuggestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TagVotes_TagRemovalRequestId",
                table: "TagVotes",
                column: "TagRemovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_TagVotes_TagSuggestionId",
                table: "TagVotes",
                column: "TagSuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TagVotes_UserId_TagRemovalRequestId",
                table: "TagVotes",
                columns: new[] { "UserId", "TagRemovalRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagVotes_UserId_TagSuggestionId",
                table: "TagVotes",
                columns: new[] { "UserId", "TagSuggestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTagOverrides_JumpDocumentId",
                table: "UserTagOverrides",
                column: "JumpDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTagOverrides_UserId",
                table: "UserTagOverrides",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTagOverrides_UserId_JumpDocumentId_TagName_TagCategory",
                table: "UserTagOverrides",
                columns: new[] { "UserId", "JumpDocumentId", "TagName", "TagCategory" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentViewCounts");

            migrationBuilder.DropTable(
                name: "TagVotes");

            migrationBuilder.DropTable(
                name: "UserTagOverrides");

            migrationBuilder.DropTable(
                name: "VotingConfigurations");

            migrationBuilder.DropTable(
                name: "TagRemovalRequests");

            migrationBuilder.DropTable(
                name: "TagSuggestions");
        }
    }
}
