using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTokenAuthStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthStatus",
                table: "UserTokens",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthStatusChangedAt",
                table: "UserTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastAuthErrorDescription",
                table: "UserTokens",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthStatus",
                table: "UserTokens");

            migrationBuilder.DropColumn(
                name: "AuthStatusChangedAt",
                table: "UserTokens");

            migrationBuilder.DropColumn(
                name: "LastAuthErrorDescription",
                table: "UserTokens");
        }
    }
}
