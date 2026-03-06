using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Calendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GoogleCalendarId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Color = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GoogleEventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CalendarInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsAllDay = table.Column<bool>(type: "boolean", nullable: false),
                    Location = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Calendars_CalendarInfoId",
                        column: x => x.CalendarInfoId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncToken = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncWindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncWindowEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncStates_Calendars_CalendarInfoId",
                        column: x => x.CalendarInfoId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_GoogleCalendarId",
                table: "Calendars",
                column: "GoogleCalendarId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_CalendarInfoId",
                table: "Events",
                column: "CalendarInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_End",
                table: "Events",
                column: "End");

            migrationBuilder.CreateIndex(
                name: "IX_Events_GoogleEventId_CalendarInfoId",
                table: "Events",
                columns: new[] { "GoogleEventId", "CalendarInfoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_Start",
                table: "Events",
                column: "Start");

            migrationBuilder.CreateIndex(
                name: "IX_SyncStates_CalendarInfoId",
                table: "SyncStates",
                column: "CalendarInfoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "SyncStates");

            migrationBuilder.DropTable(
                name: "Calendars");
        }
    }
}
