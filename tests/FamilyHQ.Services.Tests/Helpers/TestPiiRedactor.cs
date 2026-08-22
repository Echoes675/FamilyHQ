using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Logging;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.Services.Tests.Helpers;

/// <summary>
/// FHQ-166. The real <see cref="SaltedHashPiiRedactor"/> under a fixed salt, shared by every test
/// that has to construct a service taking an <see cref="IPiiRedactor"/>.
/// <para>
/// Deliberately the production implementation rather than a Moq stub: a stub that echoed its input
/// back would let a redaction test pass while production leaked, and a stub returning a constant
/// would hide the stability property the tokens are supposed to have. The salt is fixed so a test
/// can name the exact token it expects.
/// </para>
/// </summary>
internal static class TestPiiRedactor
{
    /// <summary>
    /// At least <see cref="SaltedHashPiiRedactor.MinimumSaltLength"/> characters, because the
    /// production type rejects a shorter one — a test fixture that could not be configured in
    /// production would be testing something production cannot do.
    /// </summary>
    internal const string Salt = "fhq-166-unit-test-salt-long-enough-to-be-a-salt";

    internal static IPiiRedactor Instance { get; } =
        new SaltedHashPiiRedactor(Salt, new Mock<ILogger<SaltedHashPiiRedactor>>().Object);

    /// <summary>The token the production redactor produces for <paramref name="value"/> under <see cref="Salt"/>.</summary>
    internal static string TokenFor(string value) => Instance.Redact(value);
}
