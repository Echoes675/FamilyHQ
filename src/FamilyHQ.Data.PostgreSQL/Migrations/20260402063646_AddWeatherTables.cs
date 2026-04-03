using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddWeatherTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeatherDataPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocationSettingId = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Condition = table.Column<int>(type: "integer", nullable: false),
                    TemperatureCelsius = table.Column<double>(type: "double precision", nullable: false),
                    HighCelsius = table.Column<double>(type: "double precision", nullable: true),
                    LowCelsius = table.Column<double>(type: "double precision", nullable: true),
                    WindSpeedKmh = table.Column<double>(type: "double precision", nullable: false),
                    IsWindy = table.Column<bool>(type: "boolean", nullable: false),
                    DataType = table.Column<int>(type: "integer", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherDataPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeatherDataPoints_LocationSettings_LocationSettingId",
                        column: x => x.LocationSettingId,
                        principalTable: "LocationSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeatherSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    PollIntervalMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    TemperatureUnit = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    WindThresholdKmh = table.Column<double>(type: "double precision", nullable: false, defaultValue: 30.0),
                    ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherDataPoints_LocationSettingId_DataType_Timestamp",
                table: "WeatherDataPoints",
                columns: new[] { "LocationSettingId", "DataType", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeatherDataPoints");

            migrationBuilder.DropTable(
                name: "WeatherSettings");
        }
    }
}
