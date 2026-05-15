using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncEventFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncEventFailures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CalendarInfoId = table.Column<Guid>(type: "uuid", nullable: true),
                    GoogleEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EventTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExceptionType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncEventFailures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncEventFailures_Calendars_CalendarInfoId",
                        column: x => x.CalendarInfoId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncEventFailures_CalendarInfoId",
                table: "SyncEventFailures",
                column: "CalendarInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncEventFailures_UserId_FailedAt",
                table: "SyncEventFailures",
                columns: new[] { "UserId", "FailedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncEventFailures");
        }
    }
}
