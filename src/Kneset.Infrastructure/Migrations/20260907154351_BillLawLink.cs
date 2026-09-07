using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillLawLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IsraelLawId",
                table: "Bills",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LawMatch",
                table: "Bills",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bills_IsraelLawId",
                table: "Bills",
                column: "IsraelLawId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bills_IsraelLaws_IsraelLawId",
                table: "Bills",
                column: "IsraelLawId",
                principalTable: "IsraelLaws",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bills_IsraelLaws_IsraelLawId",
                table: "Bills");

            migrationBuilder.DropIndex(
                name: "IX_Bills_IsraelLawId",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "IsraelLawId",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "LawMatch",
                table: "Bills");
        }
    }
}
