namespace FamilyHQ.Core.Interfaces;

public interface IWeatherBroadcaster
{
    Task BroadcastWeatherUpdatedAsync(CancellationToken ct = default);
}
