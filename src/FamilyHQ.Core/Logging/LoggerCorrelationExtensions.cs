namespace FamilyHQ.Core.Logging;

using FamilyHQ.Core.Constants;
using Microsoft.Extensions.Logging;

public static class LoggerCorrelationExtensions
{
    /// <summary>
    /// Opens a logging scope carrying a fresh <c>CorrelationId</c> (a new GUID), so every log
    /// event emitted within the returned scope's lifetime shares one id — mirroring the HTTP
    /// request correlation (FHQ-65). Open one at the start of each background-task invocation:
    /// <c>using (logger.BeginCorrelationScope()) { ... }</c>.
    /// </summary>
    public static IDisposable? BeginCorrelationScope(this ILogger logger) =>
        logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationConstants.CorrelationIdLogProperty] = Guid.NewGuid().ToString()
        });
}
