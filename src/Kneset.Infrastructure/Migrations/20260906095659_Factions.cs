using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Factions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Factions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    NameHe = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OriginKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GroupKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    GroupDate = table.Column<DateOnly>(type: "date", nullable: true),
                    GroupOrdinal = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    VerifiedAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Factions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FactionParties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FactionId = table.Column<int>(type: "integer", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    NameHe = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NameRu = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactionParties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactionParties_Factions_FactionId",
                        column: x => x.FactionId,
                        principalTable: "Factions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FactionParties_FactionId_NameHe",
                table: "FactionParties",
                columns: new[] { "FactionId", "NameHe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Factions_GroupKey",
                table: "Factions",
                column: "GroupKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FactionParties");

            migrationBuilder.DropTable(
                name: "Factions");
        }
    }
}
