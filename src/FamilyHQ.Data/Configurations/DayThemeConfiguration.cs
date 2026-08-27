using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class DayThemeConfiguration : IEntityTypeConfiguration<DayTheme>
{
    public void Configure(EntityTypeBuilder<DayTheme> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Date).IsRequired();
        // FHQ-177: one row per kiosk per date. The old unique index was on Date alone, which made
        // the theme global — two kiosks in different places could not both hold a correct row.
        builder.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
        builder.Property(x => x.MorningStart).IsRequired();
        builder.Property(x => x.DaytimeStart).IsRequired();
        builder.Property(x => x.EveningStart).IsRequired();
        builder.Property(x => x.NightStart).IsRequired();
        builder.Property(x => x.IanaTimeZone).HasMaxLength(64);
    }
}
