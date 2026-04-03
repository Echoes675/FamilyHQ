using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToWeatherSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "WeatherSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.Sql("DELETE FROM \"WeatherSettings\" WHERE \"UserId\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "WeatherSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeatherSettings_UserId",
                table: "WeatherSettings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeatherSettings_UserId",
                table: "WeatherSettings");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "WeatherSettings");
        }
    }
}
