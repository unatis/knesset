using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSessionAt",
                table: "Bills",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    KnessetSessionId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StatusId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillSessions_Bills_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillSessions_BillId_Kind_KnessetSessionId",
                table: "BillSessions",
                columns: new[] { "BillId", "Kind", "KnessetSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillSessions_BillId_StartDate",
                table: "BillSessions",
                columns: new[] { "BillId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BillSessions_StartDate",
                table: "BillSessions",
                column: "StartDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillSessions");

            migrationBuilder.DropColumn(
                name: "FirstSessionAt",
                table: "Bills");
        }
    }
}
