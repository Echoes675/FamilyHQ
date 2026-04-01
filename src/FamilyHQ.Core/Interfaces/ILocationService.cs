using FamilyHQ.Core.DTOs;

namespace FamilyHQ.Core.Interfaces;

public interface ILocationService
{
    Task<LocationResult> GetEffectiveLocationAsync(CancellationToken ct = default);
}
