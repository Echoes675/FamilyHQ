using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly ICalendarSyncJobQueue _syncJobQueue;
    private readonly ICurrentUserService _currentUser;
    private readonly IOptions<SyncOptions> _syncOptions;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(
        ICalendarRepository calendarRepository,
        ITokenStore tokenStore,
        ISyncFailureRepository syncFailureRepository,
        ICalendarSyncJobQueue syncJobQueue,
        ICurrentUserService currentUser,
        IOptions<SyncOptions> syncOptions,
        ILogger<DiagnosticsController> logger)
    {
        _calendarRepository = calendarRepository;
        _tokenStore = tokenStore;
        _syncFailureRepository = syncFailureRepository;
        _syncJobQueue = syncJobQueue;
        _currentUser = currentUser;
        _syncOptions = syncOptions;
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
        var calendarDtos = calendars
            .Select(c => new ConnectionStatusCalendarDto(c.Id, c.DisplayName, c.SyncState?.LastSyncedAt))
            .ToList();

        return Ok(new ConnectionStatusWithCalendarsDto(
            statusText,
            auth.LastError,
            auth.Since,
            calendarDtos));
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

    [HttpGet("failed-sync-runs")]
    public async Task<IActionResult> GetFailedSyncRuns([FromQuery] int limit = DefaultSyncFailureLimit, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var clamped = Math.Clamp(limit, MinSyncFailureLimit, MaxSyncFailureLimit);
        var runs = await _syncJobQueue.GetRecentFailuresAsync(userId, clamped, _syncOptions.Value.TerminalJobRetention, ct);

        IReadOnlyList<FailedSyncRunDto> dtos = runs
            .Select(r => new FailedSyncRunDto(
                r.Id,
                r.CalendarInfoId,
                r.AttemptCount,
                r.LastError,
                r.Source.ToString(),
                r.CompletedAt ?? r.EnqueuedAt))
            .ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// FHQ-174 existing-data check: a breakdown of the current user's stored all-day boundaries by
    /// the shape they are stored in.
    /// </summary>
    /// <remarks>
    /// Read-only by design — this ticket ships no repair. The counts are reported per boundary and
    /// per shape rather than as one total, because two unrelated legacy defects both produce a
    /// non-midnight value and only one of them is the host-offset day shift; see
    /// <see cref="AllDayBoundaryAuditDto"/> for which is which. Rows inside the sync window heal
    /// without intervention (<c>CalendarSyncService</c> overwrites Start/End from every list
    /// response), so the numbers that matter are the ones dated outside it, which is why the response
    /// carries the affected range as well as the counts.
    /// </remarks>
    [HttpGet("all-day-boundary-audit")]
    public async Task<IActionResult> GetAllDayBoundaryAudit(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_currentUser.UserId))
            return Unauthorized();

        return Ok(await _calendarRepository.GetAllDayBoundaryAuditAsync(ct));
    }

    /// <summary>
    /// Current user's sync-queue depth — the count of not-yet-terminal jobs (Pending or
    /// InProgress). Used to observe whether the durable queue has drained.
    /// </summary>
    [HttpGet("sync-queue-depth")]
    public async Task<IActionResult> GetSyncQueueDepth(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var active = await _syncJobQueue.GetActiveJobCountAsync(userId, ct);
        return Ok(new SyncQueueDepthDto(active));
    }
}
