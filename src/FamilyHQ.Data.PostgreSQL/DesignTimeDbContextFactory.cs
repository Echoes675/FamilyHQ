using FamilyHQ.Data;
using FamilyHQ.Data.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FamilyHQ.Data.PostgreSQL;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FamilyHqDbContext>
{
    public FamilyHqDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<FamilyHqDbContext>();
        
        // This connection string is only used for design-time migrations.
        // It does not need to connect to a real database just to generate the migration files.
        // The NpgsqlModelCustomizer MUST be applied here as well as at runtime (ServiceCollectionExtensions):
        // Database.Migrate() throws PendingModelChangesWarning when the runtime model differs from the last
        // migration's snapshot, so the design-time model has to be identical to the runtime one.
        builder.UseNpgsql("Host=localhost;Database=FamilyHqDb_Design;Username=postgres;Password=postgres",
                x => x.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.FullName))
            .ReplaceService<IModelCustomizer, NpgsqlModelCustomizer>();

        return new FamilyHqDbContext(builder.Options);
    }
}
