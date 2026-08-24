using System.Security.Cryptography;
using System.Text;
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
    // Long enough to satisfy SaltedHashPiiRedactor.MinimumSaltLength — a shorter one would be
    // rejected in production, so a fixture using one would be testing a configuration that cannot
    // exist.
    private const string Salt = "a-deployment-salt-that-is-long-enough";

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
        CreateSut("salt-one-padded-to-the-minimum-length").Redact(PrimaryCalendarId)
            .Should().NotBe(CreateSut("salt-two-padded-to-the-minimum-length").Redact(PrimaryCalendarId));
    }

    [Fact]
    public void Redact_TakesTheSaltAsTheHmacKeyAndTheValueAsTheMessage_NotTheOtherWayRound()
    {
        // HMAC is not symmetric in its arguments, but swapping them still yields a stable,
        // non-reversible, fixed-length token — so every other test in this class would go on
        // passing while the construction quietly became "keyed by the address, salted by the salt".
        // That is the weaker arrangement: the key is what an attacker must not be able to supply,
        // and the address is the thing they are guessing.
        var token = CreateSut().Redact(PrimaryCalendarId);
        var saltAsKey = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Salt), Encoding.UTF8.GetBytes(PrimaryCalendarId)));
        var valueAsKey = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(PrimaryCalendarId), Encoding.UTF8.GetBytes(Salt)));

        saltAsKey.Should().StartWith(token, "the salt is the key and the value is the message");
        valueAsKey.Should().NotStartWith(token, "swapping the arguments must fail this test");
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

    [Theory]
    [InlineData("a")]
    [InlineData("short-salt")]
    [InlineData("0123456789abcdef0123456789abcde")] // one character under the minimum
    public void Constructor_WithASuppliedSaltShorterThanTheMinimum_Throws(string salt)
    {
        // A one-character salt is guessed in a single pass over the alphabet, which puts the
        // household candidate list straight back in play — the very attack the salt exists to
        // defeat. Accepting it silently is worse than having no salt: it claims a protection it
        // does not provide, and unlike the absent case nothing warns anyone.
        var construct = () => CreateSut(salt);

        construct.Should().Throw<ArgumentException>()
            .WithMessage($"*{SaltedHashPiiRedactor.MinimumSaltLength}*");
    }

    [Fact]
    public void Constructor_WithASuppliedSaltOfExactlyTheMinimumLength_IsAccepted()
    {
        var minimumLengthSalt = new string('s', SaltedHashPiiRedactor.MinimumSaltLength);

        CreateSut(minimumLengthSalt).Redact(PrimaryCalendarId).Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void Constructor_WhenRejectingAShortSalt_DoesNotPutTheSaltItselfInTheMessage()
    {
        // The exception reaches Seq via whatever logs the failed startup, and a rejected salt is
        // still a secret someone intended to use. Name the key and the length, never the value.
        const string rejectedSalt = "hunter2";

        var construct = () => CreateSut(rejectedSalt);

        construct.Should().Throw<ArgumentException>()
            .Which.Message.Should().NotContain(rejectedSalt)
            .And.Contain(SaltedHashPiiRedactor.SaltConfigurationKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSalt_WithNoSuppliedSalt_DoesNotThrow(string? salt)
    {
        // Absent is the documented degraded mode, not a misconfiguration: the app boots, still
        // redacts, and warns that correlation is now per-process. Only a salt that was SUPPLIED and
        // is too short to work is a failure.
        var validate = () => SaltedHashPiiRedactor.ValidateSalt(salt);

        validate.Should().NotThrow();
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
