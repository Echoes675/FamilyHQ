namespace FamilyHQ.E2E.Common.Helpers;

/// <summary>
/// Polls <paramref name="condition"/> until true or the timeout elapses, then throws a
/// TimeoutException carrying <paramref name="failMessage"/>. Use this for COMPOUND/COMPUTED
/// conditions that are not a single locator (e.g. "the visible events list contains X",
/// "count >= N"). For a single locator, prefer Playwright web-first Assertions.Expect(...).
/// </summary>
public static class Polling
{
    public static async Task UntilAsync(Func<Task<bool>> condition, string failMessage,
        int timeoutMs = 5000, int intervalMs = 250)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(intervalMs);
        }
        throw new TimeoutException(failMessage);
    }
}
