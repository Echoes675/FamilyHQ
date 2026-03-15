using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class CalendarInfoConfiguration : IEntityTypeConfiguration<CalendarInfo>
{
    public void Configure(EntityTypeBuilder<CalendarInfo> builder)
    {
        builder.ToTable("Calendars");
        
        builder.HasKey(c => c.Id);
        
        builder.Property(c => c.GoogleCalendarId)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(c => c.DisplayName)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(c => c.Color)
            .HasMaxLength(50);
            
        builder.Property(c => c.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(c => new { c.GoogleCalendarId, c.UserId }).IsUnique();

        builder.HasOne(c => c.SyncState)
            .WithOne(s => s.CalendarInfo)
            .HasForeignKey<SyncState>(s => s.CalendarInfoId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(c => c.Events)
            .WithMany(e => e.Calendars)
            .UsingEntity(j => j.ToTable("CalendarEventCalendar"));
    }
}
