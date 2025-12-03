using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrQualityColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.DropColumn(
                name: "IsDownloadUrl",
                table: "DocumentUrls");

            migrationBuilder.DropColumn(
                name: "IsPublicUrl",
                table: "DocumentUrls");

            migrationBuilder.DropColumn(
                name: "Url",
                table: "DocumentUrls");

            migrationBuilder.AddColumn<double>(
                name: "OcrQuality",
                table: "JumpDocuments",
                type: "REAL",
                nullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TagVotes_TagRemovalRequests_TagRemovalRequestId",
                table: "TagVotes");

            migrationBuilder.DropForeignKey(
                name: "FK_TagVotes_TagSuggestions_TagSuggestionId",
                table: "TagVotes");

            migrationBuilder.DropColumn(
                name: "OcrQuality",
                table: "JumpDocuments");

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

            migrationBuilder.AddColumn<bool>(
                name: "IsDownloadUrl",
                table: "DocumentUrls",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublicUrl",
                table: "DocumentUrls",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Url",
                table: "DocumentUrls",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

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
    }
}
