using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private const int DefaultSyncFailureLimit = 100;
    private const int MaxSyncFailureLimit = 500;
    private const int MinSyncFailureLimit = 1;

    private readonly ICalendarRepository _calendarRepository;
    private readonly ITokenStore _tokenStore;
    private readonly ISyncFailureRepository _syncFailureRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(
        ICalendarRepository calendarRepository,
        ITokenStore tokenStore,
        ISyncFailureRepository syncFailureRepository,
        ICurrentUserService currentUser,
        ILogger<DiagnosticsController> logger)
    {
        _calendarRepository = calendarRepository;
        _tokenStore = tokenStore;
        _syncFailureRepository = syncFailureRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("connection-status")]
    public async Task<IActionResult> GetConnectionStatus(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var auth = await _tokenStore.GetAuthStatusAsync(userId, ct);
        var statusText = auth.Status == TokenAuthStatus.NeedsReauth ? "needs_reauth" : "active";

        var calendars = await _calendarRepository.GetCalendarsByUserIdAsync(userId, ct);
        var calendarPayloads = new List<object>(calendars.Count);
        foreach (var cal in calendars)
        {
            var syncState = await _calendarRepository.GetSyncStateAsync(cal.Id, ct);
            calendarPayloads.Add(new
            {
                calendarId = cal.Id,
                displayName = cal.DisplayName,
                lastSyncedAt = syncState?.LastSyncedAt
            });
        }

        return Ok(new
        {
            status = statusText,
            lastError = auth.LastError,
            since = auth.Since,
            calendars = calendarPayloads
        });
    }

    [HttpGet("sync-failures")]
    public async Task<IActionResult> GetSyncFailures([FromQuery] int limit = DefaultSyncFailureLimit, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var clamped = Math.Clamp(limit, MinSyncFailureLimit, MaxSyncFailureLimit);
        var failures = await _syncFailureRepository.GetRecentAsync(userId, clamped, ct);

        IReadOnlyList<SyncFailureDto> dtos = failures
            .Select(f => new SyncFailureDto(
                f.Id,
                f.CalendarInfoId,
                f.GoogleEventId,
                f.EventTitle,
                f.FailureReason,
                f.ExceptionType,
                f.FailedAt,
                f.Resolved))
            .ToList();

        return Ok(dtos);
    }
}
