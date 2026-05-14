using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.Simulator.State;

/// <summary>
/// Builds Google-shaped error responses for injected calendar API failures.
/// Returning <c>null</c> means no failure has been injected and the controller
/// should proceed with its normal response.
/// </summary>
internal static class SyncFailureResponse
{
    public static IActionResult? TryBuild(SyncFailureMode mode) => mode switch
    {
        SyncFailureMode.CalendarApi403 => new ObjectResult(new
        {
            error = new
            {
                code = 403,
                message = "Request had insufficient authentication scopes.",
                errors = new[]
                {
                    new
                    {
                        domain = "global",
                        reason = "insufficientPermissions",
                        message = "Insufficient Permission"
                    }
                }
            }
        })
        { StatusCode = 403 },
        SyncFailureMode.CalendarApi401 => new ObjectResult(new
        {
            error = new
            {
                code = 401,
                message = "Invalid Credentials",
                errors = new[]
                {
                    new
                    {
                        domain = "global",
                        reason = "authError",
                        message = "Invalid Credentials"
                    }
                }
            }
        })
        { StatusCode = 401 },
        _ => null
    };
}
