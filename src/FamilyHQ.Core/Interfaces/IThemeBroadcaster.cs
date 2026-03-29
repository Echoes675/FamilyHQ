namespace FamilyHQ.Core.Interfaces;

public interface IThemeBroadcaster
{
    Task BroadcastThemeAsync(string period, CancellationToken cancellationToken = default);
}
