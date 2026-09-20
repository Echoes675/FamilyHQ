using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddEventReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemindersSyncedAt",
                table: "SyncStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reminders",
                table: "Events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultReminders",
                table: "Calendars",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemindersSyncedAt",
                table: "SyncStates");

            migrationBuilder.DropColumn(
                name: "Reminders",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "DefaultReminders",
                table: "Calendars");
        }
    }
}
