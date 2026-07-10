using FamilyHQ.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FamilyHQ.Data.PostgreSQL;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FamilyHqDbContext>
{
    public FamilyHqDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<FamilyHqDbContext>();
        
        // This connection string is only used for design-time migrations.
        // It does not need to connect to a real database just to generate the migration files.
        // NB: the FHQ-71 NpgsqlModelCustomizer (xmin concurrency token) is intentionally NOT applied here.
        // xmin is a physical Postgres system column present on every table, so the token needs no schema
        // and no migration — keeping it out of the design-time model keeps the migrations snapshot clean
        // (has-pending-model-changes stays green) while the runtime model still enforces it via DI.
        builder.UseNpgsql("Host=localhost;Database=FamilyHqDb_Design;Username=postgres;Password=postgres",
            x => x.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.FullName));

        return new FamilyHqDbContext(builder.Options);
    }
}
