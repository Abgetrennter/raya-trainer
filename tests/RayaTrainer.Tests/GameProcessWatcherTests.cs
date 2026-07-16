using RayaTrainer.App.Services;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public class GameProcessWatcherTests
{
    // Helper: builds a TargetSelectionResult carrying a fake DetectedRa3Target.
    private static DetectedRa3Target FakeTarget(int pid = 1234) => new(
        pid, "ra3_1.12", "ra3_1.12.game", @"C:\fake\ra3_1.12.game",
        (nint)0x400000, true, "1.12.0.0", null,
        TargetSupportStatus.Installable, Array.Empty<VersionEvidence>());

    private static TargetSelectionResult Single(DetectedRa3Target t) =>
        new(TargetSelectionStatus.SingleAutoSelected, t, new[] { t }, "single");

    private static TargetSelectionResult Ambiguous(DetectedRa3Target a, DetectedRa3Target b) =>
        new(TargetSelectionStatus.AmbiguousRequiresUserChoice, null, new[] { a, b }, "ambiguous");

    private static readonly TargetSelectionResult None =
        new(TargetSelectionStatus.NoCandidate, null, Array.Empty<DetectedRa3Target>(), "none");

    // Polls condition until it holds or timeout elapses — robust to background-thread
    // scheduling jitter (the watcher's PeriodicTimer ticks fire on the thread pool, so a
    // fixed Task.Delay can fire-and-miss under cold-build load). Returns the final
    // condition value so callers can Assert on it.
    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (!condition())
            {
                await Task.Delay(5, cts.Token);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            return condition();
        }
    }

    [Fact]
    public void Start_TransitionsDisabled_ToStandby()
    {
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () => None);
        watcher.Start();
        Assert.True(watcher.IsRunning);
    }

    [Fact]
    public void Stop_TransitionsToDisabled()
    {
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () => None);
        watcher.Start();
        watcher.Stop();
        Assert.False(watcher.IsRunning);
    }

    [Fact]
    public void RepeatStartStop_IsIdempotent()
    {
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () => None);
        watcher.Start(); watcher.Start();
        watcher.Stop(); watcher.Stop();
        Assert.False(watcher.IsRunning);
    }

    [Fact]
    public async Task Tick_WithSingleTarget_RaisesTargetFound()
    {
        var t = FakeTarget();
        var probe = Single(t);
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () => probe);
        DetectedRa3Target? found = null;
        watcher.TargetFound += (_, e) => found = e.Target;
        watcher.Start();
        Assert.True(await WaitForAsync(() => found is not null, TimeSpan.FromSeconds(2)),
            "TargetFound was not raised.");
        Assert.Equal(t, found);
    }

    [Fact]
    public async Task Tick_WithAmbiguous_RaisesAmbiguousEvent_OnlyOnce()
    {
        var a = FakeTarget(1); var b = FakeTarget(2);
        var probe = Ambiguous(a, b);
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () => probe);
        int fired = 0;
        watcher.AmbiguousCandidatesDetected += (_, __) => Interlocked.Increment(ref fired);
        watcher.Start();
        Assert.True(await WaitForAsync(() => Volatile.Read(ref fired) >= 1, TimeSpan.FromSeconds(2)),
            "AmbiguousCandidatesDetected was not raised.");
        Assert.Equal(1, Volatile.Read(ref fired)); // must not refire while unresolved
    }

    [Fact]
    public async Task ResolveAmbiguity_StopsRefiring_LetsCallerAttach()
    {
        var a = FakeTarget(1); var b = FakeTarget(2);
        var probe = Ambiguous(a, b);
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () => probe);
        int fired = 0;
        watcher.AmbiguousCandidatesDetected += (_, __) => Interlocked.Increment(ref fired);
        watcher.Start();
        Assert.True(await WaitForAsync(() => Volatile.Read(ref fired) >= 1, TimeSpan.FromSeconds(2)),
            "AmbiguousCandidatesDetected was not raised.");
        watcher.ResolveAmbiguity(a);   // caller picked `a`
        Assert.True(watcher.IsRunning); // still running, now in Attaching
        Assert.Equal(1, Volatile.Read(ref fired));
    }

    [Fact]
    public async Task CancelAmbiguity_ReturnsToStandby()
    {
        var a = FakeTarget(1); var b = FakeTarget(2);
        var probe = Ambiguous(a, b);
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () => probe);
        watcher.Start();
        Assert.True(await WaitForAsync(
            () => watcher.CurrentState == GameWatcherState.AwaitingAmbiguityResolution,
            TimeSpan.FromSeconds(2)));
        // Subscribe before cancel to capture re-fire from next tick.
        int fired = 0;
        watcher.AmbiguousCandidatesDetected += (_, __) => Interlocked.Increment(ref fired);
        watcher.CancelAmbiguity();
        Assert.True(await WaitForAsync(() => Volatile.Read(ref fired) >= 1, TimeSpan.FromSeconds(2)),
            "AmbiguousCandidatesDetected did not refire after cancel.");
        Assert.Equal(1, Volatile.Read(ref fired));
    }

    [Fact]
    public async Task NotifyAttached_StopsScanning()
    {
        var t = FakeTarget();
        int hits = 0;
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () =>
        {
            Interlocked.Increment(ref hits);
            return Single(t);
        });
        watcher.TargetFound += (_, __) => { }; // consume
        watcher.Start();
        Assert.True(await WaitForAsync(() => Volatile.Read(ref hits) >= 1, TimeSpan.FromSeconds(2)),
            "Probe never ticked.");
        watcher.NotifyAttached();
        var afterAttach = Volatile.Read(ref hits);
        await Task.Delay(100);
        Assert.Equal(afterAttach, Volatile.Read(ref hits)); // no more ticks while Attached
    }

    [Fact]
    public async Task OnSessionOffline_AfterAttached_ReturnsToStandbyAndRefinds()
    {
        var t = FakeTarget();
        var probe = Single(t);
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () => probe);
        watcher.Start();
        Assert.True(await WaitForAsync(
            () => watcher.CurrentState == GameWatcherState.Attaching,
            TimeSpan.FromSeconds(2)));
        watcher.NotifyAttached();
        watcher.OnSessionOffline();
        int refound = 0;
        watcher.TargetFound += (_, __) => Interlocked.Increment(ref refound);
        Assert.True(await WaitForAsync(() => Volatile.Read(ref refound) >= 1, TimeSpan.FromSeconds(2)),
            "TargetFound was not re-raised after offline.");
        Assert.Equal(1, Volatile.Read(ref refound));
    }

    [Fact]
    public async Task NotifyAttachFailed_ReturnsToStandby()
    {
        var t = FakeTarget();
        var probe = Single(t);
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () => probe);
        watcher.TargetFound += (_, __) => { };
        watcher.Start();
        Assert.True(await WaitForAsync(
            () => watcher.CurrentState == GameWatcherState.Attaching,
            TimeSpan.FromSeconds(2)));
        watcher.NotifyAttachFailed();
        Assert.True(watcher.IsRunning); // back to Standby
    }

    [Fact]
    public async Task Suspend_StopsTicks_ResumeRestarts()
    {
        var t = FakeTarget();
        int hits = 0;
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () =>
        {
            Interlocked.Increment(ref hits);
            return Single(t);
        });
        watcher.Suspend();
        watcher.Start();
        await Task.Delay(50);
        Assert.Equal(0, Volatile.Read(ref hits)); // suspended, no ticks
        watcher.Resume();
        Assert.True(await WaitForAsync(() => Volatile.Read(ref hits) > 0, TimeSpan.FromSeconds(2)),
            "Probe did not resume ticking after Resume.");
    }

    [Fact]
    public async Task Start_WhenAlreadyAttached_DoesNotScan()
    {
        var t = FakeTarget();
        int hits = 0;
        using var watcher = new GameProcessWatcher(TimeSpan.FromMilliseconds(5), () =>
        {
            Interlocked.Increment(ref hits);
            return Single(t);
        });
        watcher.NotifyAttached(); // pretend already attached
        watcher.Start();
        await Task.Delay(50);
        Assert.Equal(0, Volatile.Read(ref hits));
    }
}
