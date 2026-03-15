using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class EventMultipleCalendars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Calendars_CalendarInfoId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_CalendarInfoId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_GoogleEventId_CalendarInfoId",
                table: "Events");

            migrationBuilder.CreateTable(
                name: "CalendarEventCalendar",
                columns: table => new
                {
                    CalendarsId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEventCalendar", x => new { x.CalendarsId, x.EventsId });
                    table.ForeignKey(
                        name: "FK_CalendarEventCalendar_Calendars_CalendarsId",
                        column: x => x.CalendarsId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalendarEventCalendar_Events_EventsId",
                        column: x => x.EventsId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // BEFORE dropping the column, migrate data to join table:
            migrationBuilder.Sql(@"
                INSERT INTO ""CalendarEventCalendar"" (""CalendarsId"", ""EventsId"")
                SELECT ""CalendarInfoId"", ""Id"" FROM ""Events""
                WHERE ""CalendarInfoId"" IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "CalendarInfoId",
                table: "Events");

            migrationBuilder.CreateIndex(
                name: "IX_Events_GoogleEventId",
                table: "Events",
                column: "GoogleEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEventCalendar_EventsId",
                table: "CalendarEventCalendar",
                column: "EventsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarEventCalendar");

            migrationBuilder.DropIndex(
                name: "IX_Events_GoogleEventId",
                table: "Events");

            migrationBuilder.AddColumn<Guid>(
                name: "CalendarInfoId",
                table: "Events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Events_CalendarInfoId",
                table: "Events",
                column: "CalendarInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_GoogleEventId_CalendarInfoId",
                table: "Events",
                columns: new[] { "GoogleEventId", "CalendarInfoId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Calendars_CalendarInfoId",
                table: "Events",
                column: "CalendarInfoId",
                principalTable: "Calendars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
