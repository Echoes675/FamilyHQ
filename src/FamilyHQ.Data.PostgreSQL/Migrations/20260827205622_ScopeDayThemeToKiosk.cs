using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class ScopeDayThemeToKiosk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FHQ-177: the theme becomes per-kiosk. The old unique index made it global — one row per
            // date, for everyone.
            migrationBuilder.DropIndex(
                name: "IX_DayThemes_Date",
                table: "DayThemes");

            // Same shape as AddUserIdToDisplaySetting / AddUserIdToWeatherSetting: nullable, delete
            // the ownerless rows, then tighten. EF's generated default of "" would instead have kept
            // every existing row under a UserId no kiosk can ever match — dead rows that nothing
            // reads and nothing cleans up.
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "DayThemes",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // Every pre-existing row is ownerless by definition. Discarding them is safe and wanted:
            // DayTheme rows are derived data, rebuilt from a kiosk's saved location on the next
            // scheduler tick. The rows in production right now are also *wrong* — they hold the
            // hosting VPS's sunrise/sunset, which is the bug this ticket fixes.
            migrationBuilder.Sql("DELETE FROM \"DayThemes\" WHERE \"UserId\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "DayThemes",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayThemes_UserId_Date",
                table: "DayThemes",
                columns: new[] { "UserId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DayThemes_UserId_Date",
                table: "DayThemes");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "DayThemes");

            // Reverting re-imposes one row per date globally, so drop the rows first: with more than
            // one kiosk the unique index below would otherwise fail on duplicate dates and leave the
            // database half-migrated. They regenerate, so there is nothing to preserve.
            migrationBuilder.Sql("DELETE FROM \"DayThemes\";");

            migrationBuilder.CreateIndex(
                name: "IX_DayThemes_Date",
                table: "DayThemes",
                column: "Date",
                unique: true);
        }
    }
}
