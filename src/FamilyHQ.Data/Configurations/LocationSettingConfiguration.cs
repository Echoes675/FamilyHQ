using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class LocationSettingConfiguration : IEntityTypeConfiguration<LocationSetting>
{
    public void Configure(EntityTypeBuilder<LocationSetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PlaceName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}
