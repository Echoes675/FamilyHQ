using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCalendarEventCalendarWithEventMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarEventCalendar_Calendars_CalendarsId",
                table: "CalendarEventCalendar");

            migrationBuilder.DropForeignKey(
                name: "FK_CalendarEventCalendar_Events_EventsId",
                table: "CalendarEventCalendar");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CalendarEventCalendar",
                table: "CalendarEventCalendar");

            migrationBuilder.DropColumn(
                name: "IsExternallyOwned",
                table: "Events");

            migrationBuilder.RenameTable(
                name: "CalendarEventCalendar",
                newName: "EventMembers");

            migrationBuilder.RenameColumn(
                name: "EventsId",
                table: "EventMembers",
                newName: "MembersId");

            migrationBuilder.RenameColumn(
                name: "CalendarsId",
                table: "EventMembers",
                newName: "CalendarEventId");

            migrationBuilder.RenameIndex(
                name: "IX_CalendarEventCalendar_EventsId",
                table: "EventMembers",
                newName: "IX_EventMembers_MembersId");

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "Calendars",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "Calendars",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_EventMembers",
                table: "EventMembers",
                columns: new[] { "CalendarEventId", "MembersId" });

            migrationBuilder.AddForeignKey(
                name: "FK_EventMembers_Calendars_MembersId",
                table: "EventMembers",
                column: "MembersId",
                principalTable: "Calendars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EventMembers_Events_CalendarEventId",
                table: "EventMembers",
                column: "CalendarEventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventMembers_Calendars_MembersId",
                table: "EventMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_EventMembers_Events_CalendarEventId",
                table: "EventMembers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EventMembers",
                table: "EventMembers");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "Calendars");

            migrationBuilder.DropColumn(
                name: "IsShared",
                table: "Calendars");

            migrationBuilder.RenameTable(
                name: "EventMembers",
                newName: "CalendarEventCalendar");

            migrationBuilder.RenameColumn(
                name: "MembersId",
                table: "CalendarEventCalendar",
                newName: "EventsId");

            migrationBuilder.RenameColumn(
                name: "CalendarEventId",
                table: "CalendarEventCalendar",
                newName: "CalendarsId");

            migrationBuilder.RenameIndex(
                name: "IX_EventMembers_MembersId",
                table: "CalendarEventCalendar",
                newName: "IX_CalendarEventCalendar_EventsId");

            migrationBuilder.AddColumn<bool>(
                name: "IsExternallyOwned",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_CalendarEventCalendar",
                table: "CalendarEventCalendar",
                columns: new[] { "CalendarsId", "EventsId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarEventCalendar_Calendars_CalendarsId",
                table: "CalendarEventCalendar",
                column: "CalendarsId",
                principalTable: "Calendars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarEventCalendar_Events_EventsId",
                table: "CalendarEventCalendar",
                column: "EventsId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
