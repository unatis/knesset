using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PartySlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Существующие строки пришли без slug, и уникальный индекс лёг бы
            // на полтора десятка пустых значений. Чистим: партийный слой
            // целиком воспроизводится сидером из Seed/faction-parties.json,
            // терять тут нечего.
            migrationBuilder.Sql("DELETE FROM \"FactionParties\";");

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "FactionParties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_FactionParties_Slug",
                table: "FactionParties",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FactionParties_Slug",
                table: "FactionParties");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "FactionParties");
        }
    }
}
