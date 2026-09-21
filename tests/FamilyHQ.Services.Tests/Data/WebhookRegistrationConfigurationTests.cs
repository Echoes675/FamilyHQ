using FamilyHQ.Core.Models;
using FamilyHQ.Data.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FamilyHQ.Services.Tests.Data;

/// <summary>
/// FHQ-196. Provider-free <see cref="ModelBuilder"/> — no database, no provider, no InMemory.
/// </summary>
public class WebhookRegistrationConfigurationTests
{
    [Fact]
    public void Configure_RegisteredAddressHash_IsOptionalAndFitsASha256Digest()
    {
        var builder = new ModelBuilder();
        new WebhookRegistrationConfiguration().Configure(builder.Entity<WebhookRegistration>());

        var property = builder.Model
            .FindEntityType(typeof(WebhookRegistration))!
            .FindProperty(nameof(WebhookRegistration.RegisteredAddressHash))!;

        // Optional is not a detail: production rows written before this column existed have no
        // value, and a NOT NULL column would either fail the migration or need a made-up default
        // that lies about which address those channels were registered for.
        property.IsNullable.Should().BeTrue();
        property.GetMaxLength().Should().Be(64, "a SHA-256 digest is 64 lowercase hex characters");
    }
}
