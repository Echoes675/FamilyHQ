using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class WebhookRegistrationConfiguration : IEntityTypeConfiguration<WebhookRegistration>
{
    public void Configure(EntityTypeBuilder<WebhookRegistration> builder)
    {
        builder.ToTable("WebhookRegistrations");

        builder.HasKey(w => w.Id);

        // ChannelId - required, max length 256
        builder.Property(w => w.ChannelId)
            .IsRequired()
            .HasMaxLength(256);

        // ResourceId - required, max length 256
        builder.Property(w => w.ResourceId)
            .IsRequired()
            .HasMaxLength(256);

        // ChannelToken - required, max length 128
        builder.Property(w => w.ChannelToken)
            .IsRequired()
            .HasMaxLength(128);

        // RegisteredAddressHash (FHQ-196) - optional, 64 hex characters of SHA-256. Optional
        // because every row written before this column existed has no value; a null reads as
        // "registered for an address we never recorded" and re-registers once.
        builder.Property(w => w.RegisteredAddressHash)
            .IsRequired(false)
            .HasMaxLength(64);

        // ExpiresAt - required, convert to UTC
        builder.Property(w => w.ExpiresAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);

        // RegisteredAt - required, convert to UTC
        builder.Property(w => w.RegisteredAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);

        // Unique index on CalendarInfoId
        builder.HasIndex(w => w.CalendarInfoId)
            .IsUnique();
    }
}
