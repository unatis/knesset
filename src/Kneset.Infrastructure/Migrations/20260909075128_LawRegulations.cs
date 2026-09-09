using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LawRegulations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SiteDataVersion",
                table: "IsraelLaws",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LawRegulations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsraelLawId = table.Column<int>(type: "integer", nullable: false),
                    KnessetActId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsIndirect = table.Column<bool>(type: "boolean", nullable: false),
                    PublicationSeries = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MagazineNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PageNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PublicationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocumentUrl = table.Column<string>(type: "text", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawRegulations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LawRegulations_IsraelLaws_IsraelLawId",
                        column: x => x.IsraelLawId,
                        principalTable: "IsraelLaws",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LawRegulations_IsraelLawId_KnessetActId",
                table: "LawRegulations",
                columns: new[] { "IsraelLawId", "KnessetActId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LawRegulations");

            migrationBuilder.DropColumn(
                name: "SiteDataVersion",
                table: "IsraelLaws");
        }
    }
}
