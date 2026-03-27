using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/circadian")]
[Authorize]
public class CircadianController : ControllerBase
{
    private readonly FamilyHqDbContext _dbContext;

    public CircadianController(FamilyHqDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Returns the current circadian state based on today's computed boundaries.
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var boundaries = await _dbContext.CircadianBoundaries
            .Where(x => x.Date == today)
            .OrderByDescending(x => x.ComputedAt)
            .FirstOrDefaultAsync();
        
        if (boundaries is null)
        {
            // Fallback: return Day state if no boundaries computed yet
            return Ok(new
            {
                state = CircadianState.Day.ToString(),
                sunriseUtc = (string?)null,
                sunsetUtc = (string?)null,
                computedAt = (DateTimeOffset?)null
            });
        }
        
        var currentUtcTime = TimeOnly.FromDateTime(DateTime.UtcNow);
        var state = boundaries.GetStateForTime(currentUtcTime);
        
        return Ok(new
        {
            state = state.ToString(),
            sunriseUtc = boundaries.SunriseUtc.ToString("HH:mm"),
            sunsetUtc = boundaries.SunsetUtc.ToString("HH:mm"),
            computedAt = boundaries.ComputedAt
        });
    }
}
