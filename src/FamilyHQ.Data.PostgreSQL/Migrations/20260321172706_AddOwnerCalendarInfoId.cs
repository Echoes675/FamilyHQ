using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerCalendarInfoId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExternallyOwned",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerCalendarInfoId",
                table: "Events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE "Events" e
                SET "OwnerCalendarInfoId" = (
                    SELECT cec."CalendarsId"
                    FROM "CalendarEventCalendar" cec
                    WHERE cec."EventsId" = e."Id"
                    ORDER BY cec."CalendarsId"
                    LIMIT 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Events_OwnerCalendarInfoId",
                table: "Events",
                column: "OwnerCalendarInfoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Calendars_OwnerCalendarInfoId",
                table: "Events",
                column: "OwnerCalendarInfoId",
                principalTable: "Calendars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                ALTER TABLE "Events" ALTER COLUMN "OwnerCalendarInfoId" DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Calendars_OwnerCalendarInfoId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_OwnerCalendarInfoId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "IsExternallyOwned",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "OwnerCalendarInfoId",
                table: "Events");
        }
    }
}
