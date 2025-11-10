using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTagRemovalDeleteBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TagRemovalRequests_DocumentTags_DocumentTagId",
                table: "TagRemovalRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_TagRemovalRequests_DocumentTags_DocumentTagId",
                table: "TagRemovalRequests",
                column: "DocumentTagId",
                principalTable: "DocumentTags",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TagRemovalRequests_DocumentTags_DocumentTagId",
                table: "TagRemovalRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_TagRemovalRequests_DocumentTags_DocumentTagId",
                table: "TagRemovalRequests",
                column: "DocumentTagId",
                principalTable: "DocumentTags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
