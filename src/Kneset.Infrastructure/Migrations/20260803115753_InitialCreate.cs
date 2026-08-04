using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KnessetBillId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NameRu = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StatusId = table.Column<int>(type: "integer", nullable: true),
                    StatusDesc = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    KnessetNum = table.Column<int>(type: "integer", nullable: false),
                    SubTypeDesc = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Number = table.Column<int>(type: "integer", nullable: true),
                    PublicationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SummaryLaw = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KnessetPersonId = table.Column<int>(type: "integer", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GenderDesc = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    FactionName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecordsUpserted = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    AnalysisJson = table.Column<string>(type: "jsonb", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false),
                    BillLastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillAnalyses_Bills_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillInitiators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    PersonId = table.Column<int>(type: "integer", nullable: false),
                    IsInitiator = table.Column<bool>(type: "boolean", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillInitiators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillInitiators_Bills_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillInitiators_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillAnalyses_BillId_IsStale",
                table: "BillAnalyses",
                columns: new[] { "BillId", "IsStale" });

            migrationBuilder.CreateIndex(
                name: "IX_BillInitiators_BillId_PersonId",
                table: "BillInitiators",
                columns: new[] { "BillId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillInitiators_PersonId",
                table: "BillInitiators",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Bills_KnessetBillId",
                table: "Bills",
                column: "KnessetBillId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bills_KnessetNum",
                table: "Bills",
                column: "KnessetNum");

            migrationBuilder.CreateIndex(
                name: "IX_Bills_LastUpdatedDate",
                table: "Bills",
                column: "LastUpdatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_KnessetPersonId",
                table: "Persons",
                column: "KnessetPersonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncLogs_EntityName_StartedUtc",
                table: "SyncLogs",
                columns: new[] { "EntityName", "StartedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillAnalyses");

            migrationBuilder.DropTable(
                name: "BillInitiators");

            migrationBuilder.DropTable(
                name: "SyncLogs");

            migrationBuilder.DropTable(
                name: "Bills");

            migrationBuilder.DropTable(
                name: "Persons");
        }
    }
}
