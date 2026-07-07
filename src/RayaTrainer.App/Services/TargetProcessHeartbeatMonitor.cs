using System.Diagnostics;

namespace RayaTrainer.App.Services;

public sealed class TargetProcessHeartbeatPolicy
{
    private readonly int _failureThreshold;
    private int _consecutiveFailures;

    public TargetProcessHeartbeatPolicy(int failureThreshold = 3)
    {
        if (failureThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        }

        _failureThreshold = failureThreshold;
    }

    public int ConsecutiveFailures => _consecutiveFailures;

    public bool Observe(bool processAlive)
    {
        if (processAlive)
        {
            _consecutiveFailures = 0;
            return false;
        }

        _consecutiveFailures++;
        return _consecutiveFailures >= _failureThreshold;
    }
}

public sealed record TargetProcessOfflineEventArgs(
    int ProcessId,
    int ConsecutiveFailures,
    long Generation);

public sealed class TargetProcessHeartbeatMonitor : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);
    public const int DefaultFailureThreshold = 3;

    private readonly object _sync = new();
    private readonly TimeSpan _interval;
    private readonly int _failureThreshold;
    private readonly Func<int, bool> _processProbe;
    private CancellationTokenSource? _cancellation;
    private int? _processId;
    private long _generation;

    public TargetProcessHeartbeatMonitor(
        TimeSpan? interval = null,
        int failureThreshold = DefaultFailureThreshold,
        Func<int, bool>? processProbe = null)
    {
        _interval = interval ?? DefaultInterval;
        if (_interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (failureThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        }

        _failureThreshold = failureThreshold;
        _processProbe = processProbe ?? IsProcessAlive;
    }

    public event EventHandler<TargetProcessOfflineEventArgs>? OfflineDetected;

    public long Start(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        Stop();
        var generation = Interlocked.Increment(ref _generation);
        var cancellation = new CancellationTokenSource();
        lock (_sync)
        {
            _processId = processId;
            _cancellation = cancellation;
        }

        _ = RunAsync(processId, generation, cancellation);
        return generation;
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            cancellation = _cancellation;
            _cancellation = null;
            _processId = null;
        }

        cancellation?.Cancel();
    }

    public void Dispose()
    {
        Stop();
        OfflineDetected = null;
    }

    private async Task RunAsync(
        int processId,
        long generation,
        CancellationTokenSource cancellation)
    {
        var policy = new TargetProcessHeartbeatPolicy(_failureThreshold);
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation.Token).ConfigureAwait(false))
            {
                bool alive;
                try
                {
                    alive = _processProbe(processId);
                }
                catch
                {
                    alive = false;
                }

                if (!policy.Observe(alive))
                {
                    continue;
                }

                if (generation == Volatile.Read(ref _generation))
                {
                    OfflineDetected?.Invoke(
                        this,
                        new TargetProcessOfflineEventArgs(
                            processId,
                            policy.ConsecutiveFailures,
                            generation));
                }

                return;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation) && _processId == processId)
                {
                    _cancellation = null;
                    _processId = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        using var process = Process.GetProcessById(processId);
        return !process.HasExited;
    }
}
