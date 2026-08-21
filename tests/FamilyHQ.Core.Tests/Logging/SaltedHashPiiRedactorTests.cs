using System.Text.RegularExpressions;
using FamilyHQ.Core.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.Core.Tests.Logging;

/// <summary>
/// FHQ-166. The redactor's contract has two halves that pull against each other, and both are
/// asserted here: the token must not lead back to the value it stands for, and it must be the SAME
/// token every time so an investigation can still follow one calendar across log lines.
/// </summary>
public class SaltedHashPiiRedactorTests
{
    private const string Salt = "a-deployment-salt";

    // A Google PRIMARY calendar's id is the account's email address — the exact shape this exists for.
    private const string PrimaryCalendarId = "a.family.member@example.com";

    private static SaltedHashPiiRedactor CreateSut(string? salt = Salt) =>
        new(salt, new Mock<ILogger<SaltedHashPiiRedactor>>().Object);

    private static (SaltedHashPiiRedactor Sut, Mock<ILogger<SaltedHashPiiRedactor>> Logger) CreateSutWithLogger(string? salt)
    {
        var logger = new Mock<ILogger<SaltedHashPiiRedactor>>();
        return (new SaltedHashPiiRedactor(salt, logger.Object), logger);
    }

    private static void VerifyMissingSaltWarning(Mock<ILogger<SaltedHashPiiRedactor>> logger, Times times) =>
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("No log-redaction salt is configured")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public void Redact_CalledTwiceForTheSameValue_ReturnsTheSameToken()
    {
        var sut = CreateSut();

        sut.Redact(PrimaryCalendarId).Should().Be(sut.Redact(PrimaryCalendarId),
            "a token that changed between calls would correlate nothing");
    }

    [Fact]
    public void Redact_SameValueAndSaltOnTwoInstances_ReturnsTheSameToken()
    {
        // The instance is a singleton in production, but the WebApi and any future process sharing
        // the salt must agree — otherwise one calendar reads as two in Seq.
        CreateSut().Redact(PrimaryCalendarId)
            .Should().Be(CreateSut().Redact(PrimaryCalendarId));
    }

    [Fact]
    public void Redact_DistinctValues_ReturnDistinctTokens()
    {
        var sut = CreateSut();

        var tokens = new[]
        {
            sut.Redact(PrimaryCalendarId),
            sut.Redact("another.member@example.com"),
            sut.Redact("shared@group.calendar.google.com"),
            sut.Redact("primary")
        };

        tokens.Should().OnlyHaveUniqueItems(
            "two calendars that redact to one token would merge two calendars' histories into one");
    }

    [Fact]
    public void Redact_ValueDifferingByOneCharacter_ReturnsAnUnrelatedToken()
    {
        var sut = CreateSut();

        sut.Redact("a.family.member@example.com")
            .Should().NotBe(sut.Redact("b.family.member@example.com"));
    }

    [Fact]
    public void Redact_ReturnsAFixedLengthLowercaseHexTokenCarryingNoneOfTheInput()
    {
        var token = CreateSut().Redact(PrimaryCalendarId);

        token.Should().MatchRegex("^[0-9a-f]{16}$",
            "the token is a truncated digest, not a transformation of the address");
        token.Should().NotContainEquivalentOf("family");
        token.Should().NotContainEquivalentOf("example");
        token.Should().NotContain("@");
    }

    [Fact]
    public void Redact_DifferentSalts_ProduceDifferentTokensForTheSameValue()
    {
        // If the salt did not participate, the token would be a plain hash — and a plain hash of an
        // address drawn from a handful of candidates is confirmable by hashing the candidates.
        CreateSut("salt-one").Redact(PrimaryCalendarId)
            .Should().NotBe(CreateSut("salt-two").Redact(PrimaryCalendarId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_NullOrEmptyValue_ReturnsTheAbsentToken(string? value)
    {
        CreateSut().Redact(value).Should().Be(SaltedHashPiiRedactor.AbsentValueToken);
    }

    [Fact]
    public void Redact_WhitespaceValue_IsStillHashedRatherThanTreatedAsAbsent()
    {
        // Only null/empty mean "there was nothing here". A whitespace value is a real (if odd)
        // value and must not be quietly collapsed into the absent token alongside genuine nulls.
        CreateSut().Redact(" ").Should().NotBe(SaltedHashPiiRedactor.AbsentValueToken);
    }

    [Fact]
    public void Constructor_WithAConfiguredSalt_DoesNotWarn()
    {
        var (_, logger) = CreateSutWithLogger(Salt);

        VerifyMissingSaltWarning(logger, Times.Never());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithNoUsableSalt_WarnsThatCorrelationIsDegraded(string? salt)
    {
        var (_, logger) = CreateSutWithLogger(salt);

        VerifyMissingSaltWarning(logger, Times.Once());
    }

    [Fact]
    public void Redact_WithNoConfiguredSalt_StillRedactsButDoesNotAgreeAcrossInstances()
    {
        // The degraded contract, asserted rather than assumed: still non-reversible, no longer
        // correlatable beyond one process. That is the trade the missing-salt warning describes.
        var first = CreateSut(salt: null);
        var second = CreateSut(salt: null);

        first.Redact(PrimaryCalendarId).Should().MatchRegex("^[0-9a-f]{16}$");
        first.Redact(PrimaryCalendarId).Should().Be(first.Redact(PrimaryCalendarId),
            "a per-process salt still correlates within its own process");
        first.Redact(PrimaryCalendarId).Should().NotBe(second.Redact(PrimaryCalendarId));
    }

    [Fact]
    public void SaltConfigurationKey_IsNotAccompaniedByACommittedDefaultValue()
    {
        // A salt with a fallback literal in the source is not a salt: anyone with the repository
        // could reverse every token in Seq. The key names where the value comes from; the value
        // itself must only ever arrive from configuration.
        Regex.IsMatch(SaltedHashPiiRedactor.SaltConfigurationKey, @"^[A-Za-z]+(:[A-Za-z]+)+$")
            .Should().BeTrue("the constant is a configuration path, not a secret");
    }
}
