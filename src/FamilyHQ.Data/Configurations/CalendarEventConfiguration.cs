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
            
        builder.HasIndex(e => new { e.GoogleEventId, e.CalendarInfoId }).IsUnique();
        builder.HasIndex(e => e.Start);
        builder.HasIndex(e => e.End);
    }
}
