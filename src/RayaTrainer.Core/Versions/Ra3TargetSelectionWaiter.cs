namespace RayaTrainer.Core.Versions;

public static class Ra3TargetSelectionWaiter
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    public static async Task<TargetSelectionResult> WaitForDefaultAsync(
        Func<TargetSelectionResult> selectDefault,
        TimeSpan timeout,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        delay ??= Task.Delay;
        utcNow ??= () => DateTimeOffset.UtcNow;
        var deadline = utcNow() + timeout;
        var lastResult = selectDefault();
        while (ShouldKeepPolling(lastResult))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - utcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return lastResult;
            }

            await delay(Min(DefaultPollInterval, remaining), cancellationToken);
            lastResult = selectDefault();
        }

        return lastResult;
    }

    private static bool ShouldKeepPolling(TargetSelectionResult result)
    {
        return result.Status is
            TargetSelectionStatus.NoCandidate or
            TargetSelectionStatus.NoInstallableCandidate;
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second)
    {
        return first <= second ? first : second;
    }
}
