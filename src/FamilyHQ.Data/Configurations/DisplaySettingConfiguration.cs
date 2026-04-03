using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class DisplaySettingConfiguration : IEntityTypeConfiguration<DisplaySetting>
{
    public void Configure(EntityTypeBuilder<DisplaySetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.SurfaceMultiplier).IsRequired();
        builder.Property(x => x.ThemeSelection).IsRequired().HasMaxLength(16).HasDefaultValue("auto");
        builder.Property(x => x.TransitionDurationSecs).IsRequired();
        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}
