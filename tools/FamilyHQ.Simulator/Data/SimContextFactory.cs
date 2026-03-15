namespace FamilyHQ.Simulator.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class SimContextFactory : IDesignTimeDbContextFactory<SimContext>
{
    public SimContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseNpgsql("Host=localhost;Database=familyhq_simulator_design;Username=postgres;Password=dbadmin123")
            .Options;
        return new SimContext(options);
    }
}
