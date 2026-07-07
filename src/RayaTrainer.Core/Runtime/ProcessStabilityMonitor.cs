using System.Diagnostics;

namespace RayaTrainer.Core.Runtime;

public sealed class ProcessStabilityMonitor
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly Func<int, ProcessStabilityObservation> _observe;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _utcNow;

    public ProcessStabilityMonitor()
        : this(ObserveProcess)
    {
    }

    public ProcessStabilityMonitor(
        Func<int, ProcessStabilityObservation> observe,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _observe = observe;
        _delay = delay ?? Task.Delay;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<ProcessStabilityResult> MonitorAsync(
        int processId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        }

        var startedAt = _utcNow();
        var deadline = startedAt + duration;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = _observe(processId);
            if (!observation.IsAlive)
            {
                return ProcessStabilityResult.Exited(
                    processId,
                    _utcNow() - startedAt,
                    observation.ExitCode,
                    observation.ExitedAt);
            }

            var remaining = deadline - _utcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return ProcessStabilityResult.Stable(processId, _utcNow() - startedAt);
            }

            await _delay(Min(DefaultPollInterval, remaining), cancellationToken);
        }
    }

    private static ProcessStabilityObservation ObserveProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                return ProcessStabilityObservation.Alive;
            }

            return ProcessStabilityObservation.Exited(process.ExitCode, process.ExitTime);
        }
        catch (ArgumentException)
        {
            return ProcessStabilityObservation.Missing;
        }
        catch (InvalidOperationException)
        {
            return ProcessStabilityObservation.Missing;
        }
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second)
    {
        return first <= second ? first : second;
    }
}

public sealed record ProcessStabilityObservation(
    bool IsAlive,
    long? ExitCode = null,
    DateTimeOffset? ExitedAt = null)
{
    public static ProcessStabilityObservation Alive { get; } = new(true);

    public static ProcessStabilityObservation Missing { get; } = new(false);

    public static ProcessStabilityObservation Exited(long? exitCode, DateTimeOffset? exitedAt = null) =>
        new(false, exitCode, exitedAt);
}

public sealed record ProcessStabilityResult(
    bool StayedAlive,
    int ProcessId,
    TimeSpan ObservedFor,
    long? ExitCode,
    DateTimeOffset? ExitedAt)
{
    public static ProcessStabilityResult Stable(int processId, TimeSpan observedFor) =>
        new(true, processId, observedFor, null, null);

    public static ProcessStabilityResult Exited(
        int processId,
        TimeSpan observedFor,
        long? exitCode,
        DateTimeOffset? exitedAt) =>
        new(false, processId, observedFor, exitCode, exitedAt);
}
