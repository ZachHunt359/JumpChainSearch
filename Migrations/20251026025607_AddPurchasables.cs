using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentPurchasables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JumpDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    CostsJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PrimaryCost = table.Column<int>(type: "INTEGER", nullable: true),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    CharacterPosition = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPurchasables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentPurchasables_JumpDocuments_JumpDocumentId",
                        column: x => x.JumpDocumentId,
                        principalTable: "JumpDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPurchasables_Category",
                table: "DocumentPurchasables",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPurchasables_Category_Name",
                table: "DocumentPurchasables",
                columns: new[] { "Category", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPurchasables_JumpDocumentId",
                table: "DocumentPurchasables",
                column: "JumpDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPurchasables_Name",
                table: "DocumentPurchasables",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPurchasables_PrimaryCost",
                table: "DocumentPurchasables",
                column: "PrimaryCost");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentPurchasables");
        }
    }
}
