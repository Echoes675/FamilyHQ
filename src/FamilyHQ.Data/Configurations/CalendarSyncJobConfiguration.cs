using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class CalendarSyncJobConfiguration : IEntityTypeConfiguration<CalendarSyncJob>
{
    public void Configure(EntityTypeBuilder<CalendarSyncJob> builder)
    {
        builder.ToTable("CalendarSyncJobs");

        builder.HasKey(j => j.Id);

        builder.Property(j => j.UserId).IsRequired().HasMaxLength(255);
        builder.Property(j => j.ChannelId).HasMaxLength(255);
        builder.Property(j => j.LastError).HasMaxLength(1000);

        builder.Property(j => j.Status).HasConversion<int>();
        builder.Property(j => j.Source).HasConversion<int>();

        builder.Property(j => j.EnqueuedAt)
            .HasConversion(v => v.ToUniversalTime(), v => v);
        builder.Property(j => j.StartedAt)
            .HasConversion(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);
        builder.Property(j => j.CompletedAt)
            .HasConversion(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);
        builder.Property(j => j.NextAttemptAt)
            .HasConversion(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);

        // Claim ordering / lookup
        builder.HasIndex(j => new { j.Status, j.NextAttemptAt });

        // Coalescing backstop for targeted jobs: at most one Pending job per (user, calendar).
        // Sync-all jobs (null CalendarInfoId) are de-duplicated by the code-level check in
        // EnqueueAsync; Postgres treats NULLs as distinct so they are excluded from this index.
        builder.HasIndex(j => new { j.UserId, j.CalendarInfoId })
            .HasFilter("\"Status\" = 0 AND \"CalendarInfoId\" IS NOT NULL")
            .IsUnique();
    }
}
