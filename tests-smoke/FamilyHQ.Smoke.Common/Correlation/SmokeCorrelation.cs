namespace FamilyHQ.Smoke.Common.Correlation;

/// <summary>
/// The one identity a smoke scenario stamps on everything it touches (FHQ-141 principles 3 and 4).
/// <para>
/// Smoke runs against a live environment whose events are <b>kept</b> after the run, so the run
/// before last is still there. Two things follow, and this type is both of them:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <see cref="DescriptionMarker"/> goes into every event this scenario creates, and is how the
///     event is located and confirmed in Google before any assertion is made about it. A title match
///     alone would happily find last week's event.
///   </description></item>
///   <item><description>
///     <see cref="ShortId"/> goes into every event <i>title</i>, because a kiosk-side assertion sees
///     only the title. Six hex characters is 16.7 million values against a handful of retained runs
///     per calendar — the collision that matters is "an earlier run's event", and a per-run id rules
///     that out regardless of width.
///   </description></item>
/// </list>
/// <para>
/// The same value travels as <c>X-Correlation-Id</c> on every call the suite makes to preprod, and is
/// seeded into the kiosk's session-correlation slot, so a failing scenario has one id to grep for in
/// Seq across the browser's requests and the suite's own.
/// </para>
/// </summary>
public sealed class SmokeCorrelation
{
    /// <summary>The description line that marks an event as belonging to this scenario.</summary>
    public const string DescriptionMarkerPrefix = "smoke-correlation: ";

    private const int ShortIdLength = 6;

    private SmokeCorrelation(string id)
    {
        Id = id;
    }

    /// <summary>The full correlation id — a GUID, one per scenario.</summary>
    public string Id { get; }

    /// <summary>The first six hex characters of <see cref="Id"/>, for event titles.</summary>
    public string ShortId => Id.Replace("-", string.Empty, StringComparison.Ordinal)[..ShortIdLength];

    /// <summary>The line written into every event description created by this scenario.</summary>
    public string DescriptionMarker => DescriptionMarkerPrefix + Id;

    public static SmokeCorrelation New() => new(Guid.NewGuid().ToString());

    /// <summary>
    /// An event title carrying this scenario's short id, e.g. <c>Birthday · 7f3a9c</c>. The separator
    /// is a middle dot so the short id reads as a tag rather than as part of the name.
    /// </summary>
    public string Title(string baseTitle) => $"{baseTitle} · {ShortId}";

    /// <summary>
    /// A description carrying this scenario's marker below <paramref name="userText"/>, mirroring how
    /// FamilyHQ itself keeps user-visible text above a managed tag.
    /// </summary>
    public string Description(string userText) =>
        string.IsNullOrWhiteSpace(userText) ? DescriptionMarker : $"{userText}\n{DescriptionMarker}";

    /// <summary>True when <paramref name="description"/> carries this scenario's marker.</summary>
    public bool Marks(string? description) =>
        description is not null && description.Contains(DescriptionMarker, StringComparison.Ordinal);

    /// <summary>True when <paramref name="title"/> carries this scenario's short id.</summary>
    public bool TitleCarriesShortId(string? title) =>
        title is not null && title.Contains(ShortId, StringComparison.Ordinal);
}
