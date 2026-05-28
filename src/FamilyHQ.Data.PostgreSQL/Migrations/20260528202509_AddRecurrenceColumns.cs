using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurrenceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleRecurringEventId",
                table: "Events",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginalStartTime",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceRule",
                table: "Events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_GoogleRecurringEventId",
                table: "Events",
                column: "GoogleRecurringEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_GoogleRecurringEventId_OriginalStartTime",
                table: "Events",
                columns: new[] { "GoogleRecurringEventId", "OriginalStartTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_GoogleRecurringEventId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_GoogleRecurringEventId_OriginalStartTime",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "GoogleRecurringEventId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "OriginalStartTime",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RecurrenceRule",
                table: "Events");
        }
    }
}
