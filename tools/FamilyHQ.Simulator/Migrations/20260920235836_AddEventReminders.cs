using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Simulator.Migrations
{
    /// <inheritdoc />
    public partial class AddEventReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemindersJson",
                table: "SimulatedEvents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultRemindersJson",
                table: "SimulatedCalendars",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemindersJson",
                table: "SimulatedEvents");

            migrationBuilder.DropColumn(
                name: "DefaultRemindersJson",
                table: "SimulatedCalendars");
        }
    }
}
