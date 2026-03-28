using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.ToTable("UserPreferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.EventDensity).HasDefaultValue(2);
        builder.Property(x => x.CalendarColumnOrder).HasMaxLength(4000).IsRequired(false);
        builder.Property(x => x.CalendarColorOverrides).HasMaxLength(4000).IsRequired(false);
        builder.Property(x => x.LastModified).IsRequired();
        
        // One preferences record per user
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}
