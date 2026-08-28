using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ILocationSettingRepository
{
    /// <summary>Used by authenticated controller operations — scoped to the given user.</summary>
    Task<LocationSetting?> GetAsync(string userId, CancellationToken ct = default);
    Task<LocationSetting> UpsertAsync(string userId, LocationSetting locationSetting, CancellationToken ct = default);
    Task DeleteAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// FHQ-177: the user ids of every kiosk that has a saved location — the set the day-theme
    /// scheduler iterates. It is a background service with no HTTP context, so it cannot ask
    /// <c>ICurrentUserService</c> who it is working for.
    /// <para>
    /// Deliberately unfiltered, and deliberately returns **ids only** — never coordinates. A kiosk
    /// with no saved location is absent from the result and gets no theme row, which is the intended
    /// outcome: server-side IP geolocation reports the hosting datacentre, so guessing is choosing a
    /// known-wrong answer rather than a rough one.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> GetUserIdsWithLocationAsync(CancellationToken ct = default);
}
