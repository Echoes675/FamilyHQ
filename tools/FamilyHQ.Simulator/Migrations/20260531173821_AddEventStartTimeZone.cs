using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Simulator.Migrations
{
    /// <inheritdoc />
    public partial class AddEventStartTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StartTimeZone",
                table: "SimulatedEvents",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartTimeZone",
                table: "SimulatedEvents");
        }
    }
}
