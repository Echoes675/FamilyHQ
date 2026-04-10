using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Simulator.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHashToEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "SimulatedEvents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "SimulatedEvents");
        }
    }
}
