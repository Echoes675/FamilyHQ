namespace FamilyHQ.Core.Models;

/// <summary>
/// Single source of truth for the source → work-type mapping shared by the enqueue coalescing
/// guard and the worker's dispatch branch (FHQ-69). The switch deliberately has no discard arm:
/// adding a <see cref="SyncJobSource"/> member without classifying it here raises CS8509, which is
/// an error under TreatWarningsAsErrors, so a new source can never be silently misclassified.
/// Only CS8524 (unnamed values, e.g. a bad cast) is suppressed — those throw
/// <see cref="System.Runtime.CompilerServices.SwitchExpressionException"/> at runtime (fail fast).
/// </summary>
public static class SyncJobSourceExtensions
{
    /// <summary>
    /// True when the worker executes this source as a reconcile-only job (placement reconciler,
    /// no Google sync); false for Google-sync sources. Enqueue coalescing groups jobs by this
    /// work type.
    /// </summary>
#pragma warning disable CS8524 // see class doc: keep CS8509 exhaustiveness for named members; unnamed values fail fast at runtime.
    public static bool IsReconcileOnly(this SyncJobSource source) => source switch
    {
        SyncJobSource.DesignationChange => true,
        SyncJobSource.Webhook or SyncJobSource.Periodic or SyncJobSource.Login => false
    };
#pragma warning restore CS8524
}
