using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("Events");
        
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.GoogleEventId)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(500);
            
        builder.Property(e => e.Location)
            .HasMaxLength(1000);
        
        // Value Converters for UTC conversion
        builder.Property(e => e.Start)
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);
                
        builder.Property(e => e.End)
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);
            
        builder.HasIndex(e => e.GoogleEventId).IsUnique();
        builder.HasIndex(e => e.Start);
        builder.HasIndex(e => e.End);

        builder.Property(e => e.OwnerCalendarInfoId)
            .IsRequired();

        builder.HasOne<CalendarInfo>()
            .WithMany()
            .HasForeignKey(e => e.OwnerCalendarInfoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.IsExternallyOwned)
            .IsRequired()
            .HasDefaultValue(false);
    }
}
