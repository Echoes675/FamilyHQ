using FamilyHQ.Data;
using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using Moq;

namespace FamilyHQ.Services.Tests.Fakes;

/// <summary>
/// Provider-free <see cref="FamilyHqDbContext"/> test double — no InMemory, no real DB. Each entity's
/// <see cref="DbSet{T}"/> is a MockQueryable-backed mock over a seeded list, so LINQ operators run as
/// LINQ-to-Objects. Overriding <see cref="Set{T}"/> and <see cref="SaveChangesAsync(CancellationToken)"/>
/// means the base model is never finalized, so the FHQ-146 concurrency-token convention never fires here.
/// Writes do NOT round-trip: assert inserts by interaction (Add + SaveChanges), assert updates on the
/// seeded instance. Never touch ChangeTracker/FindAsync/Entry/Model on this double — they force model build.
/// <see cref="Set{T}"/> throws for a type that was never seeded — call <see cref="Setup{T}"/> (an empty
/// seed is fine) for every entity type the test exercises before calling into the repository under test.
/// </summary>
public sealed class FakeFamilyHqDbContext : FamilyHqDbContext
{
    private readonly Dictionary<Type, object> _sets = new();
    public int SaveChangesCount { get; private set; }
    /// <summary>Return value / failure hook for SaveChangesAsync. Throw inside to simulate a save failure.</summary>
    public Func<int>? OnSaveChanges { get; set; }

    public FakeFamilyHqDbContext()
        : base(new DbContextOptionsBuilder<FamilyHqDbContext>().Options) { }

    /// <summary>Seed (or replace) the mock set for <typeparamref name="T"/> and return the Moq mock for verification.</summary>
    public Mock<DbSet<T>> Setup<T>(IEnumerable<T>? seed = null) where T : class
    {
        var mock = (seed ?? Enumerable.Empty<T>()).ToList().BuildMockDbSet();
        _sets[typeof(T)] = mock.Object;
        return mock;
    }

    public override DbSet<T> Set<T>() where T : class =>
        _sets.TryGetValue(typeof(T), out var s)
            ? (DbSet<T>)s
            : throw new InvalidOperationException(
                $"No mock DbSet was seeded for {typeof(T).Name}. Call Setup<{typeof(T).Name}>(...) " +
                "in the test (use an empty seed if the set should be empty) before exercising the repository.");

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCount++;
        return Task.FromResult(OnSaveChanges?.Invoke() ?? 0);
    }

    // The double doesn't track entities; the retry loop's reset is a no-op here (and touching the real
    // ChangeTracker would force a model build with no provider configured).
    public override void ClearTrackedEntities() { }
}
