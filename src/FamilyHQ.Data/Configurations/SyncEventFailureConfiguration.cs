using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class SyncEventFailureConfiguration : IEntityTypeConfiguration<SyncEventFailure>
{
    public void Configure(EntityTypeBuilder<SyncEventFailure> builder)
    {
        builder.ToTable("SyncEventFailures");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(f => f.GoogleEventId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(f => f.EventTitle)
            .HasMaxLength(256);

        builder.Property(f => f.FailureReason)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(f => f.ExceptionType)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(f => f.FailedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);

        builder.Property(f => f.Resolved)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasOne(f => f.CalendarInfo)
            .WithMany()
            .HasForeignKey(f => f.CalendarInfoId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(f => new { f.UserId, f.FailedAt })
            .HasDatabaseName("IX_SyncEventFailures_UserId_FailedAt")
            .IsDescending(false, true);
    }
}
