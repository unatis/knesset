using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsraelLaws : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IsraelLaws",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KnessetIsraelLawId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    KnessetNum = table.Column<int>(type: "integer", nullable: true),
                    IsBasicLaw = table.Column<bool>(type: "boolean", nullable: false),
                    IsBudgetLaw = table.Column<bool>(type: "boolean", nullable: false),
                    ValidityDesc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PublicationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidityStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidityFinishDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IsraelLaws", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LawAmendments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KnessetBindingId = table.Column<int>(type: "integer", nullable: false),
                    IsraelLawId = table.Column<int>(type: "integer", nullable: false),
                    KnessetLawId = table.Column<int>(type: "integer", nullable: false),
                    ActName = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ActPublicationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BindingTypeDesc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AmendmentTypeDesc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsIndirect = table.Column<bool>(type: "boolean", nullable: false),
                    IsOriginal = table.Column<bool>(type: "boolean", nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawAmendments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LawAmendments_IsraelLaws_IsraelLawId",
                        column: x => x.IsraelLawId,
                        principalTable: "IsraelLaws",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IsraelLaws_IsBasicLaw",
                table: "IsraelLaws",
                column: "IsBasicLaw");

            migrationBuilder.CreateIndex(
                name: "IX_IsraelLaws_KnessetIsraelLawId",
                table: "IsraelLaws",
                column: "KnessetIsraelLawId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LawAmendments_IsraelLawId_IsIndirect",
                table: "LawAmendments",
                columns: new[] { "IsraelLawId", "IsIndirect" });

            migrationBuilder.CreateIndex(
                name: "IX_LawAmendments_KnessetBindingId",
                table: "LawAmendments",
                column: "KnessetBindingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LawAmendments");

            migrationBuilder.DropTable(
                name: "IsraelLaws");
        }
    }
}
