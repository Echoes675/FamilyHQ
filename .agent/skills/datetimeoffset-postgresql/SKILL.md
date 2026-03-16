---
name: datetimeoffset-postgresql
description: Guidelines for working with DateTimeOffset values in PostgreSQL using automatic UTC conversion at the data layer. Used when adding or modifying entities with DateTimeOffset properties.
---

# DateTimeOffset and PostgreSQL Handling

## The Problem

PostgreSQL requires all timestamp values to be stored in UTC. When using `DateTimeOffset` in C# with non-zero offsets (e.g., local time zones), PostgreSQL will reject the values or produce unexpected results. Manual UTC conversion scattered throughout the codebase is error-prone and difficult to maintain.

## The Solution

FamilyHQ implements **automatic UTC conversion at the data layer** using two complementary approaches:

1. **SaveChanges Override** - Converts all `DateTimeOffset` values to UTC before writing to the database
2. **Value Converters** - Ensures UTC conversion during EF Core's query translation

This eliminates the need for manual `.ToUniversalTime()` calls in business logic, controllers, or services.

## Implementation Details

### SaveChanges Override in DbContext

The [`FamilyHqDbContext`](../../src/FamilyHQ.Data/FamilyHqDbContext.cs) overrides all `SaveChanges` methods to automatically convert `DateTimeOffset` values to UTC:

```csharp
public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    ConvertDateTimeOffsetsToUtc();
    return base.SaveChangesAsync(cancellationToken);
}

private void ConvertDateTimeOffsetsToUtc()
{
    var entries = ChangeTracker.Entries()
        .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

    foreach (var entry in entries)
    {
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.ClrType == typeof(DateTimeOffset))
            {
                if (property.CurrentValue is DateTimeOffset value && value.Offset != TimeSpan.Zero)
                {
                    property.CurrentValue = value.ToUniversalTime();
                }
            }
            else if (property.Metadata.ClrType == typeof(DateTimeOffset?))
            {
                if (property.CurrentValue is DateTimeOffset value && value.Offset != TimeSpan.Zero)
                {
                    property.CurrentValue = value.ToUniversalTime();
                }
            }
        }
    }
}
```

**How it works:**
- Intercepts all save operations (sync and async)
- Scans the Change Tracker for Added or Modified entities
- Converts any `DateTimeOffset` or `DateTimeOffset?` properties with non-zero offsets to UTC
- Preserves values already in UTC (offset = 0)

### Value Converters in Entity Configurations

Entity configurations use Value Converters to ensure UTC conversion during query translation. Example from [`CalendarEventConfiguration`](../../src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs):

```csharp
public class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        // ... other configuration ...
        
        // Value Converters for UTC conversion
        builder.Property(e => e.Start)
            .HasConversion(
                v => v.ToUniversalTime(),  // To database
                v => v);                    // From database
                
        builder.Property(e => e.End)
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);
    }
}
```

**How it works:**
- Converts values to UTC when writing to the database
- Reads values as-is from the database (already UTC)
- Ensures consistency in LINQ queries and raw SQL

## Usage Guidelines

### When to Use DateTimeOffset vs DateTime

| Use Case | Type | Reason |
|----------|------|--------|
| Event times, appointments | `DateTimeOffset` | Preserves original time zone context |
| Audit timestamps (Created, Modified) | `DateTimeOffset` | Tracks when something happened in absolute time |
| Recurring patterns, time-of-day | `TimeSpan` or `DateTime` (Kind.Unspecified) | Time zone independent |
| Date-only values | `DateOnly` (.NET 6+) | No time component needed |

### Adding DateTimeOffset Properties to Entities

When adding a new `DateTimeOffset` property to an entity:

1. **Add the property to the model:**
   ```csharp
   public class MyEntity
   {
       public DateTimeOffset ScheduledAt { get; set; }
       public DateTimeOffset? CompletedAt { get; set; }  // Nullable if optional
   }
   ```

2. **Configure Value Converters in the entity configuration:**
   ```csharp
   public class MyEntityConfiguration : IEntityTypeConfiguration<MyEntity>
   {
       public void Configure(EntityTypeBuilder<MyEntity> builder)
       {
           builder.Property(e => e.ScheduledAt)
               .HasConversion(
                   v => v.ToUniversalTime(),
                   v => v);
                   
           builder.Property(e => e.CompletedAt)
               .HasConversion(
                   v => v.HasValue ? v.Value.ToUniversalTime() : v,
                   v => v);
       }
   }
   ```

3. **Create and apply an EF Core migration:**
   ```bash
   cd src/FamilyHQ.Data.PostgreSQL
   dotnet ef migrations add AddScheduledAtToMyEntity
   dotnet ef database update
   ```

4. **Use the property naturally in code:**
   ```csharp
   // NO manual conversion needed!
   var entity = new MyEntity
   {
       ScheduledAt = DateTimeOffset.Now  // Will be converted to UTC automatically
   };
   
   await _context.SaveChangesAsync();
   ```

### What NOT to Do

❌ **Do NOT manually convert to UTC in business logic:**
```csharp
// WRONG - unnecessary and redundant
entity.Start = request.Start.ToUniversalTime();
```

✅ **Do this instead:**
```csharp
// RIGHT - let the data layer handle it
entity.Start = request.Start;
```

❌ **Do NOT use DateTime for time zone-aware values:**
```csharp
// WRONG - loses time zone information
public DateTime EventStart { get; set; }
```

✅ **Do this instead:**
```csharp
// RIGHT - preserves time zone context
public DateTimeOffset EventStart { get; set; }
```

## Testing Considerations

### Unit Tests

When testing entities with `DateTimeOffset` properties:

```csharp
[Fact]
public async Task SaveChanges_ConvertsDateTimeOffsetToUtc()
{
    // Arrange
    var localTime = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(-5));
    var entity = new CalendarEvent
    {
        Start = localTime,
        End = localTime.AddHours(1)
    };
    
    // Act
    _context.Events.Add(entity);
    await _context.SaveChangesAsync();
    
    // Assert
    entity.Start.Offset.Should().Be(TimeSpan.Zero);  // Converted to UTC
    entity.Start.Hour.Should().Be(19);  // 14:30 EST = 19:30 UTC
}
```

### Integration Tests

Test that values round-trip correctly through the database:

```csharp
[Fact]
public async Task DateTimeOffset_RoundTripsCorrectly()
{
    // Arrange
    var originalTime = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.FromHours(1));
    var entity = new CalendarEvent { Start = originalTime };
    
    // Act - Save
    _context.Events.Add(entity);
    await _context.SaveChangesAsync();
    var id = entity.Id;
    
    // Clear context to force database read
    _context.ChangeTracker.Clear();
    
    // Act - Retrieve
    var retrieved = await _context.Events.FindAsync(id);
    
    // Assert
    retrieved.Start.Should().Be(originalTime.ToUniversalTime());
    retrieved.Start.Offset.Should().Be(TimeSpan.Zero);
}
```

## Benefits of This Approach

1. **Consistency** - All `DateTimeOffset` values are guaranteed to be UTC in the database
2. **Safety** - Eliminates manual conversion errors
3. **Maintainability** - Centralized logic in one place
4. **Developer Experience** - Natural C# code without boilerplate
5. **PostgreSQL Compatibility** - No timestamp errors or unexpected behavior

## Affected Entities

Current entities using this pattern:

- [`CalendarEvent`](../../src/FamilyHQ.Core/Models/CalendarEvent.cs) - `Start`, `End`
- [`SyncState`](../../src/FamilyHQ.Core/Models/SyncState.cs) - `LastSyncedAt`, `SyncWindowStart`, `SyncWindowEnd`

## References

- **Architectural Design:** [`plans/datetimeoffset-utc-conversion-strategy.md`](../../plans/datetimeoffset-utc-conversion-strategy.md)
- **DbContext Implementation:** [`src/FamilyHQ.Data/FamilyHqDbContext.cs`](../../src/FamilyHQ.Data/FamilyHqDbContext.cs)
- **Entity Configuration Example:** [`src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs`](../../src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs)
