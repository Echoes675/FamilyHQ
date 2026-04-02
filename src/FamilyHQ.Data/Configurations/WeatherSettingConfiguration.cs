namespace FamilyHQ.Data.Configurations;

using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class WeatherSettingConfiguration : IEntityTypeConfiguration<WeatherSetting>
{
    public void Configure(EntityTypeBuilder<WeatherSetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Enabled).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.PollIntervalMinutes).IsRequired().HasDefaultValue(30);
        builder.Property(x => x.TemperatureUnit).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.WindThresholdKmh).IsRequired().HasDefaultValue(30.0);
        builder.Property(x => x.ApiKey).HasMaxLength(500);
    }
}
