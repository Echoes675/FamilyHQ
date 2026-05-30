namespace FamilyHQ.Core.DTOs;

/// <summary>Current sync-queue depth for a user: count of Pending + InProgress jobs.</summary>
public record SyncQueueDepthDto(int Active);
