using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class ChangeCalendarUniqueIndexToPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Calendars_GoogleCalendarId",
                table: "Calendars");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "Calendars",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_GoogleCalendarId_UserId",
                table: "Calendars",
                columns: new[] { "GoogleCalendarId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Calendars_GoogleCalendarId_UserId",
                table: "Calendars");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "Calendars",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_GoogleCalendarId",
                table: "Calendars",
                column: "GoogleCalendarId",
                unique: true);
        }
    }
}
