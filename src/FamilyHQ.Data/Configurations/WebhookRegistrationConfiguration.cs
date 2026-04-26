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
