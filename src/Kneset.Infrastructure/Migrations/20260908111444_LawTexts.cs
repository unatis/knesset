using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LawTexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IsraelLawTexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsraelLawId = table.Column<int>(type: "integer", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    RevisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    CharCount = table.Column<int>(type: "integer", nullable: false),
                    CleanerVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IsraelLawTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IsraelLawTexts_IsraelLaws_IsraelLawId",
                        column: x => x.IsraelLawId,
                        principalTable: "IsraelLaws",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IsraelLawTexts_IsraelLawId",
                table: "IsraelLawTexts",
                column: "IsraelLawId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IsraelLawTexts");
        }
    }
}
