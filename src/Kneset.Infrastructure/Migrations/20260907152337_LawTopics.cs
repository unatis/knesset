using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LawTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LawTopics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KnessetClassificationId = table.Column<int>(type: "integer", nullable: false),
                    IsraelLawId = table.Column<int>(type: "integer", nullable: false),
                    TopicId = table.Column<int>(type: "integer", nullable: false),
                    NameHe = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LawTopics_IsraelLaws_IsraelLawId",
                        column: x => x.IsraelLawId,
                        principalTable: "IsraelLaws",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LawTopics_IsraelLawId",
                table: "LawTopics",
                column: "IsraelLawId");

            migrationBuilder.CreateIndex(
                name: "IX_LawTopics_KnessetClassificationId",
                table: "LawTopics",
                column: "KnessetClassificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LawTopics_TopicId",
                table: "LawTopics",
                column: "TopicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LawTopics");
        }
    }
}
