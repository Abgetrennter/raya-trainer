using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ProcessStabilityMonitorTests
{
    [Fact]
    public async Task MonitorAsyncReturnsStableWhenProcessStaysAliveForDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var monitor = new ProcessStabilityMonitor(
            _ => ProcessStabilityObservation.Alive,
            (delay, _) =>
            {
                now += delay;
                return Task.CompletedTask;
            },
            () => now);

        var result = await monitor.MonitorAsync(1234, TimeSpan.FromSeconds(1));

        Assert.True(result.StayedAlive);
        Assert.Equal(1234, result.ProcessId);
        Assert.Null(result.ExitCode);
        Assert.True(result.ObservedFor >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MonitorAsyncReturnsExitDetailsWhenProcessExitsBeforeDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var calls = 0;
        var monitor = new ProcessStabilityMonitor(
            _ => ++calls == 3
                ? ProcessStabilityObservation.Exited(3221225477)
                : ProcessStabilityObservation.Alive,
            (delay, _) =>
            {
                now += delay;
                return Task.CompletedTask;
            },
            () => now);

        var result = await monitor.MonitorAsync(4321, TimeSpan.FromSeconds(30));

        Assert.False(result.StayedAlive);
        Assert.Equal(4321, result.ProcessId);
        Assert.Equal(3221225477, result.ExitCode);
        Assert.True(result.ObservedFor < TimeSpan.FromSeconds(30));
    }
}
