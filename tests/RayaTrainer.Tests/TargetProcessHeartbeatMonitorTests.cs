using RayaTrainer.App.Services;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TargetProcessHeartbeatMonitorTests
{
    [Fact]
    public void PolicyRequiresConsecutiveFailuresAndSuccessResetsCount()
    {
        var policy = new TargetProcessHeartbeatPolicy(failureThreshold: 3);

        Assert.False(policy.Observe(processAlive: false));
        Assert.False(policy.Observe(processAlive: false));
        Assert.Equal(2, policy.ConsecutiveFailures);

        Assert.False(policy.Observe(processAlive: true));
        Assert.Equal(0, policy.ConsecutiveFailures);

        Assert.False(policy.Observe(processAlive: false));
        Assert.False(policy.Observe(processAlive: false));
        Assert.True(policy.Observe(processAlive: false));
        Assert.Equal(3, policy.ConsecutiveFailures);
    }

    [Fact]
    public async Task MonitorRaisesOfflineAfterConfiguredFailureThreshold()
    {
        var completion = new TaskCompletionSource<TargetProcessOfflineEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCount = 0;
        using var monitor = new TargetProcessHeartbeatMonitor(
            interval: TimeSpan.FromMilliseconds(5),
            failureThreshold: 3,
            processProbe: _ =>
            {
                Interlocked.Increment(ref probeCount);
                return false;
            });
        monitor.OfflineDetected += (_, args) => completion.TrySetResult(args);

        monitor.Start(1234);
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1234, result.ProcessId);
        Assert.Equal(3, result.ConsecutiveFailures);
        Assert.True(result.Generation > 0);
        Assert.Equal(3, Volatile.Read(ref probeCount));
    }

    [Fact]
    public async Task RestartInvalidatesQueuedGenerationForSameProcessId()
    {
        var generations = new List<long>();
        using var firstProbeEntered = new ManualResetEventSlim();
        using var releaseFirstProbe = new ManualResetEventSlim();
        var probeCount = 0;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new TargetProcessHeartbeatMonitor(
            interval: TimeSpan.FromMilliseconds(5),
            failureThreshold: 1,
            processProbe: _ =>
            {
                if (Interlocked.Increment(ref probeCount) == 1)
                {
                    firstProbeEntered.Set();
                    releaseFirstProbe.Wait(TimeSpan.FromSeconds(1));
                }

                return false;
            });
        monitor.OfflineDetected += (_, args) =>
        {
            lock (generations)
            {
                generations.Add(args.Generation);
            }

            completion.TrySetResult();
        };

        monitor.Start(1234);
        Assert.True(firstProbeEntered.Wait(TimeSpan.FromSeconds(1)));
        var activeGeneration = monitor.Start(1234);
        releaseFirstProbe.Set();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(1));

        lock (generations)
        {
            Assert.All(generations, generation => Assert.Equal(activeGeneration, generation));
        }
    }
}
