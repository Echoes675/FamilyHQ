using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class SyncStateConfiguration : IEntityTypeConfiguration<SyncState>
{
    public void Configure(EntityTypeBuilder<SyncState> builder)
    {
        builder.ToTable("SyncStates");
        
        builder.HasKey(s => s.Id);
        
        builder.Property(s => s.SyncToken)
            .HasMaxLength(1000);
            
        builder.HasIndex(s => s.CalendarInfoId).IsUnique();
    }
}
