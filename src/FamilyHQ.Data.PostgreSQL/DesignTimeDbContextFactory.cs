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
        builder.UseNpgsql("Host=localhost;Database=FamilyHqDb_Design;Username=postgres;Password=postgres",
            x => x.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.FullName));

        return new FamilyHqDbContext(builder.Options);
    }
}
