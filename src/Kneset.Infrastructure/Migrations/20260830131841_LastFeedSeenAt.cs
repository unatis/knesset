using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LastFeedSeenAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastFeedSeenAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastFeedSeenAt",
                table: "AspNetUsers");
        }
    }
}
