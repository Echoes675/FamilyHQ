using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToDisplaySetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add as nullable first
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "DisplaySettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // Remove orphaned rows that have no owner
            migrationBuilder.Sql("DELETE FROM \"DisplaySettings\" WHERE \"UserId\" IS NULL;");

            // Make non-nullable
            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "DisplaySettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisplaySettings_UserId",
                table: "DisplaySettings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DisplaySettings_UserId",
                table: "DisplaySettings");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "DisplaySettings");
        }
    }
}
