using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddTagHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovedTagRules_TagRemovalRequests_TagRemovalRequestId",
                table: "ApprovedTagRules");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovedTagRules_TagSuggestions_TagSuggestionId",
                table: "ApprovedTagRules");

            migrationBuilder.DropForeignKey(
                name: "FK_TagVotes_TagRemovalRequests_TagRemovalRequestId",
                table: "TagVotes");

            migrationBuilder.DropForeignKey(
                name: "FK_TagVotes_TagSuggestions_TagSuggestionId",
                table: "TagVotes");

            migrationBuilder.AddColumn<int>(
                name: "TagRemovalRequestId1",
                table: "TagVotes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TagSuggestionId1",
                table: "TagVotes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TagHierarchies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentTagName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ChildTagName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagHierarchies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagVotes_TagRemovalRequestId1",
                table: "TagVotes",
                column: "TagRemovalRequestId1");

            migrationBuilder.CreateIndex(
                name: "IX_TagVotes_TagSuggestionId1",
                table: "TagVotes",
                column: "TagSuggestionId1");

            migrationBuilder.CreateIndex(
                name: "IX_TagVotes_UserId",
                table: "TagVotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TagHierarchies_ChildTagName",
                table: "TagHierarchies",
                column: "ChildTagName");

            migrationBuilder.CreateIndex(
                name: "IX_TagHierarchies_ParentTagName",
                table: "TagHierarchies",
                column: "ParentTagName");

            migrationBuilder.CreateIndex(
                name: "IX_TagHierarchies_ParentTagName_ChildTagName",
                table: "TagHierarchies",
                columns: new[] { "ParentTagName", "ChildTagName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedTagRules_TagRemovalRequests_TagRemovalRequestId",
                table: "ApprovedTagRules",
                column: "TagRemovalRequestId",
                principalTable: "TagRemovalRequests",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedTagRules_TagSuggestions_TagSuggestionId",
                table: "ApprovedTagRules",
                column: "TagSuggestionId",
                principalTable: "TagSuggestions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TagVotes_TagRemovalRequests_TagRemovalRequestId",
                table: "TagVotes",
                column: "TagRemovalRequestId",
                principalTable: "TagRemovalRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TagVotes_TagRemovalRequests_TagRemovalRequestId1",
                table: "TagVotes",
                column: "TagRemovalRequestId1",
                principalTable: "TagRemovalRequests",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TagVotes_TagSuggestions_TagSuggestionId",
                table: "TagVotes",
                column: "TagSuggestionId",
                principalTable: "TagSuggestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TagVotes_TagSuggestions_TagSuggestionId1",
                table: "TagVotes",
                column: "TagSuggestionId1",
                principalTable: "TagSuggestions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovedTagRules_TagRemovalRequests_TagRemovalRequestId",
                table: "ApprovedTagRules");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovedTagRules_TagSuggestions_TagSuggestionId",
                table: "ApprovedTagRules");

            migrationBuilder.DropForeignKey(
                name: "FK_TagVotes_TagRemovalRequests_TagRemovalRequestId",
                table: "TagVotes");

            migrationBuilder.DropForeignKey(
                name: "FK_TagVotes_TagRemovalRequests_TagRemovalRequestId1",
                table: "TagVotes");

            migrationBuilder.DropForeignKey(
                name: "FK_TagVotes_TagSuggestions_TagSuggestionId",
                table: "TagVotes");

            migrationBuilder.DropForeignKey(
                name: "FK_TagVotes_TagSuggestions_TagSuggestionId1",
                table: "TagVotes");

            migrationBuilder.DropTable(
                name: "TagHierarchies");

            migrationBuilder.DropIndex(
                name: "IX_TagVotes_TagRemovalRequestId1",
                table: "TagVotes");

            migrationBuilder.DropIndex(
                name: "IX_TagVotes_TagSuggestionId1",
                table: "TagVotes");

            migrationBuilder.DropIndex(
                name: "IX_TagVotes_UserId",
                table: "TagVotes");

            migrationBuilder.DropColumn(
                name: "TagRemovalRequestId1",
                table: "TagVotes");

            migrationBuilder.DropColumn(
                name: "TagSuggestionId1",
                table: "TagVotes");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedTagRules_TagRemovalRequests_TagRemovalRequestId",
                table: "ApprovedTagRules",
                column: "TagRemovalRequestId",
                principalTable: "TagRemovalRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedTagRules_TagSuggestions_TagSuggestionId",
                table: "ApprovedTagRules",
                column: "TagSuggestionId",
                principalTable: "TagSuggestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TagVotes_TagRemovalRequests_TagRemovalRequestId",
                table: "TagVotes",
                column: "TagRemovalRequestId",
                principalTable: "TagRemovalRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TagVotes_TagSuggestions_TagSuggestionId",
                table: "TagVotes",
                column: "TagSuggestionId",
                principalTable: "TagSuggestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
