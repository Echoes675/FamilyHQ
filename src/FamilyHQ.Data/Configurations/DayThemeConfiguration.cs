using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class DayThemeConfiguration : IEntityTypeConfiguration<DayTheme>
{
    public void Configure(EntityTypeBuilder<DayTheme> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Date).IsRequired();
        builder.HasIndex(x => x.Date).IsUnique();
        builder.Property(x => x.MorningStart).IsRequired();
        builder.Property(x => x.DaytimeStart).IsRequired();
        builder.Property(x => x.EveningStart).IsRequired();
        builder.Property(x => x.NightStart).IsRequired();
    }
}
