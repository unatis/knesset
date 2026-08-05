using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSeenAt",
                table: "Bills",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAt",
                table: "Bills",
                type: "timestamp with time zone",
                nullable: true);

            // В базе уже тысячи законопроектов. Без этого они выглядели бы «новыми»,
            // и первый же прогон рассылки завалил бы каждого подписчика.
            // Первый слой защиты: правдоподобное прошлое вместо даты миграции.
            migrationBuilder.Sql(
                @"UPDATE ""Bills"" SET ""FirstSeenAt"" = ""LastUpdatedDate"";");

            // Второй слой: рассылка берёт точку отсчёта из последнего успешного прогона.
            // Отмечаем прогон завершённым прямо сейчас — история остаётся позади него.
            migrationBuilder.Sql(
                @"INSERT INTO ""SyncLogs"" (""EntityName"", ""StartedUtc"", ""FinishedUtc"", ""RecordsUpserted"")
                  VALUES ('Notifications', now() AT TIME ZONE 'utc', now() AT TIME ZONE 'utc', 0);");

            migrationBuilder.AddColumn<int>(
                name: "NotificationMode",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguage",
                table: "AspNetUsers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "ru");

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    TriggeredBy = table.Column<int>(type: "integer", nullable: false),
                    TriggerDetail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EventAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Bills_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PersonId = table.Column<int>(type: "integer", nullable: true),
                    BillId = table.Column<int>(type: "integer", nullable: true),
                    Keyword = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TargetKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationSubscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationSubscriptions_Bills_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NotificationSubscriptions_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserNotificationChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotificationChannels_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotificationId = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_NotificationId_Channel",
                table: "NotificationDeliveries",
                columns: new[] { "NotificationId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_BillId",
                table: "Notifications",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_BillId_Kind_EventAt",
                table: "Notifications",
                columns: new[] { "UserId", "BillId", "Kind", "EventAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAt",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_BillId",
                table: "NotificationSubscriptions",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_Kind_BillId",
                table: "NotificationSubscriptions",
                columns: new[] { "Kind", "BillId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_Kind_PersonId",
                table: "NotificationSubscriptions",
                columns: new[] { "Kind", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_PersonId",
                table: "NotificationSubscriptions",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_UserId_Kind_TargetKey",
                table: "NotificationSubscriptions",
                columns: new[] { "UserId", "Kind", "TargetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationChannels_UserId_Channel",
                table: "UserNotificationChannels",
                columns: new[] { "UserId", "Channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationSubscriptions");

            migrationBuilder.DropTable(
                name: "UserNotificationChannels");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropColumn(
                name: "FirstSeenAt",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "NotificationMode",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PreferredLanguage",
                table: "AspNetUsers");
        }
    }
}
