using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHQ.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class RescaleSurfaceMultiplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rescale existing SurfaceMultiplier values from 0–2.0 to 0–1.0
            migrationBuilder.Sql(
                "UPDATE \"DisplaySettings\" SET \"SurfaceMultiplier\" = LEAST(\"SurfaceMultiplier\" / 2.0, 1.0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Undo rescaling (approximate — multiply back by 2)
            migrationBuilder.Sql(
                "UPDATE \"DisplaySettings\" SET \"SurfaceMultiplier\" = \"SurfaceMultiplier\" * 2.0;");
        }
    }
}
