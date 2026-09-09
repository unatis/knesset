using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisJobSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnalysisJobs_Bills_BillId",
                table: "AnalysisJobs");

            migrationBuilder.DropIndex(
                name: "IX_AnalysisJobs_BillId_Step",
                table: "AnalysisJobs");

            migrationBuilder.RenameColumn(
                name: "BillId",
                table: "AnalysisJobs",
                newName: "SubjectId");

            migrationBuilder.AddColumn<string>(
                name: "SubjectKind",
                table: "AnalysisJobs",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                // Все существующие захваты — по законопроектам: до этой
                // миграции других предметов не было. Пустое значение оставило
                // бы старые строки вне поиска, а уникальность — без силы.
                defaultValue: "bill");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisJobs_SubjectKind_SubjectId_Step",
                table: "AnalysisJobs",
                columns: new[] { "SubjectKind", "SubjectId", "Step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnalysisJobs_SubjectKind_SubjectId_Step",
                table: "AnalysisJobs");

            migrationBuilder.DropColumn(
                name: "SubjectKind",
                table: "AnalysisJobs");

            migrationBuilder.RenameColumn(
                name: "SubjectId",
                table: "AnalysisJobs",
                newName: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisJobs_BillId_Step",
                table: "AnalysisJobs",
                columns: new[] { "BillId", "Step" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisJobs_Bills_BillId",
                table: "AnalysisJobs",
                column: "BillId",
                principalTable: "Bills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
