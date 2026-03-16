using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("UserTokens");

        builder.HasKey(t => t.Id);

        // UserId - required, max length 256
        builder.Property(t => t.UserId)
            .IsRequired()
            .HasMaxLength(256);

        // Provider - required, max length 50
        builder.Property(t => t.Provider)
            .IsRequired()
            .HasMaxLength(50);

        // RefreshToken - required
        builder.Property(t => t.RefreshToken)
            .IsRequired();

        // AccessToken - nullable
        builder.Property(t => t.AccessToken)
            .HasMaxLength(4000);

        // CreatedAt - required, convert to UTC
        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);

        // UpdatedAt - required, convert to UTC
        builder.Property(t => t.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);

        // AccessTokenExpiresAt - nullable, convert to UTC if present
        builder.Property(t => t.AccessTokenExpiresAt)
            .HasConversion(
                v => v.HasValue ? v.Value.ToUniversalTime() : (DateTimeOffset?)null,
                v => v);

        // Composite unique index on UserId + Provider
        builder.HasIndex(t => new { t.UserId, t.Provider })
            .IsUnique();
    }
}
