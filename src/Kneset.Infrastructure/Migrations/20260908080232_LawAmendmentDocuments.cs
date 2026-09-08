using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LawAmendmentDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentUrl",
                table: "LawAmendments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MagazineNumber",
                table: "LawAmendments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PageNumber",
                table: "LawAmendments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationSeries",
                table: "LawAmendments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Отметка о запросе страницы закона теперь означает больше:
            // вместе со ссылкой на текст оттуда же берётся история изменений
            // с публикациями и PDF. У законов, опрошенных до этой миграции,
            // истории нет, поэтому отметку сбрасываем — шаг спросит их заново.
            migrationBuilder.Sql("UPDATE \"IsraelLaws\" SET \"SiteFetchedAt\" = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentUrl",
                table: "LawAmendments");

            migrationBuilder.DropColumn(
                name: "MagazineNumber",
                table: "LawAmendments");

            migrationBuilder.DropColumn(
                name: "PageNumber",
                table: "LawAmendments");

            migrationBuilder.DropColumn(
                name: "PublicationSeries",
                table: "LawAmendments");
        }
    }
}
