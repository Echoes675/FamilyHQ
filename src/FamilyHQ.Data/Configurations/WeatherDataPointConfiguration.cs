namespace FamilyHQ.Data.Configurations;

using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class WeatherDataPointConfiguration : IEntityTypeConfiguration<WeatherDataPoint>
{
    public void Configure(EntityTypeBuilder<WeatherDataPoint> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LocationSettingId).IsRequired();
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.Condition).IsRequired();
        builder.Property(x => x.TemperatureCelsius).IsRequired();
        builder.Property(x => x.WindSpeedKmh).IsRequired();
        builder.Property(x => x.IsWindy).IsRequired();
        builder.Property(x => x.DataType).IsRequired();
        builder.Property(x => x.RetrievedAt).IsRequired();

        builder.HasIndex(x => new { x.LocationSettingId, x.DataType, x.Timestamp });

        builder.HasOne<LocationSetting>()
            .WithMany()
            .HasForeignKey(x => x.LocationSettingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
