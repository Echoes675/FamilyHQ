namespace FamilyHQ.Core.Interfaces;

public interface IPlacementReconciler
{
    /// Re-evaluate placement for all the current user's events in [start,end]; true if any event/series moved.
    Task<bool> ReconcileForUserAsync(System.DateTimeOffset start, System.DateTimeOffset end, System.Threading.CancellationToken ct = default);
}
