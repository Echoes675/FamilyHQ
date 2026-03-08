using Microsoft.EntityFrameworkCore;

public class SimContext : DbContext
{
    public SimContext(DbContextOptions<SimContext> options) : base(options) { }
    public DbSet<SimulatedEvent> Events => Set<SimulatedEvent>();
}