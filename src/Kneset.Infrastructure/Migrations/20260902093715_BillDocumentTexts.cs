using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillDocumentTexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillDocumentTexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillDocumentId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    CharCount = table.Column<int>(type: "integer", nullable: false),
                    ExtractorVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceBytes = table.Column<int>(type: "integer", nullable: false),
                    ExtractedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillDocumentTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillDocumentTexts_BillDocuments_BillDocumentId",
                        column: x => x.BillDocumentId,
                        principalTable: "BillDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillDocumentTexts_BillDocumentId",
                table: "BillDocumentTexts",
                column: "BillDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillDocumentTexts_Status_ExtractorVersion",
                table: "BillDocumentTexts",
                columns: new[] { "Status", "ExtractorVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillDocumentTexts");
        }
    }
}
