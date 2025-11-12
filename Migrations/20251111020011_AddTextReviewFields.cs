using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddTextReviewFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TextLastEditedAt",
                table: "JumpDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextLastEditedBy",
                table: "JumpDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TextNeedsReview",
                table: "JumpDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TextReviewFlaggedAt",
                table: "JumpDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextReviewFlaggedBy",
                table: "JumpDocuments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TextLastEditedAt",
                table: "JumpDocuments");

            migrationBuilder.DropColumn(
                name: "TextLastEditedBy",
                table: "JumpDocuments");

            migrationBuilder.DropColumn(
                name: "TextNeedsReview",
                table: "JumpDocuments");

            migrationBuilder.DropColumn(
                name: "TextReviewFlaggedAt",
                table: "JumpDocuments");

            migrationBuilder.DropColumn(
                name: "TextReviewFlaggedBy",
                table: "JumpDocuments");
        }
    }
}
