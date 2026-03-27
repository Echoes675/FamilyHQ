using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class CircadianBoundariesConfiguration : IEntityTypeConfiguration<CircadianBoundaries>
{
    public void Configure(EntityTypeBuilder<CircadianBoundaries> builder)
    {
        builder.ToTable("CircadianBoundaries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Latitude).IsRequired();
        builder.Property(x => x.Longitude).IsRequired();
        builder.Property(x => x.SunriseUtc).IsRequired();
        builder.Property(x => x.SunsetUtc).IsRequired();
        builder.Property(x => x.ComputedAt).IsRequired();
        
        // One record per date per location
        builder.HasIndex(x => new { x.Date, x.Latitude, x.Longitude }).IsUnique();
    }
}
