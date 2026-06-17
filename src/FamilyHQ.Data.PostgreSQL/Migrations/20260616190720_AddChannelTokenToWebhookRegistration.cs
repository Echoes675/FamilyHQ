using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelTokenToWebhookRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChannelToken",
                table: "WebhookRegistrations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChannelToken",
                table: "WebhookRegistrations");
        }
    }
}
