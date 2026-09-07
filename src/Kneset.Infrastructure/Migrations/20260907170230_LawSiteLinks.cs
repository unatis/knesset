using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LawSiteLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KolZchutUrl",
                table: "IsraelLaws",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ministries",
                table: "IsraelLaws",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenBookUrl",
                table: "IsraelLaws",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SiteFetchedAt",
                table: "IsraelLaws",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KolZchutUrl",
                table: "IsraelLaws");

            migrationBuilder.DropColumn(
                name: "Ministries",
                table: "IsraelLaws");

            migrationBuilder.DropColumn(
                name: "OpenBookUrl",
                table: "IsraelLaws");

            migrationBuilder.DropColumn(
                name: "SiteFetchedAt",
                table: "IsraelLaws");
        }
    }
}
