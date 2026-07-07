using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ReinforcementQueueRunnerTests
{
    private static readonly TrainerFeature ReinforcementFeature =
        new("We Need Back", "呼叫战场增援", "J", [], "MustCode2+B00", "0x0C");

    [Fact]
    public async Task ExecuteAsyncRunsValidEntriesInOrder()
    {
        var controller = new ResourceWriteFeatureController();
        var entries = new[]
        {
            new ReinforcementQueueEntry("first", "0x11111111", "2", "1"),
            new ReinforcementQueueEntry("second", "0x22222222", "3", "2")
        };

        var results = await ReinforcementQueueRunner.ExecuteAsync(
            entries,
            controller,
            ReinforcementFeature,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(1));

        Assert.Equal([ReinforcementQueueItemStatus.Executed, ReinforcementQueueItemStatus.Executed], results.Select(result => result.Status));
        Assert.Equal(new uint[] { 0x11111111, 0x22222222 }, controller.ReinforcementWrites.Select(write => write.UnitId));
    }

    [Fact]
    public async Task ExecuteAsyncSkipsInvalidEntriesAndContinues()
    {
        var controller = new ResourceWriteFeatureController();
        var entries = new[]
        {
            new ReinforcementQueueEntry("bad", "0x0", "2", "1"),
            new ReinforcementQueueEntry("good", "0x22222222", "3", "2")
        };

        var results = await ReinforcementQueueRunner.ExecuteAsync(
            entries,
            controller,
            ReinforcementFeature,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(1));

        Assert.Equal(ReinforcementQueueItemStatus.Skipped, results[0].Status);
        Assert.Contains("Unit id", results[0].Message);
        Assert.Equal(ReinforcementQueueItemStatus.Executed, results[1].Status);
        Assert.Equal(new uint[] { 0x22222222 }, controller.ReinforcementWrites.Select(write => write.UnitId));
    }

    [Fact]
    public async Task ExecuteAsyncMarksTimedOutEntriesAndContinues()
    {
        var controller = new ResourceWriteFeatureController
        {
            DispatchResult = ActionDispatchResult.TimedOut
        };
        var entries = new[]
        {
            new ReinforcementQueueEntry("slow", "0x11111111", "2", "1"),
            new ReinforcementQueueEntry("also-slow", "0x22222222", "3", "2")
        };

        var results = await ReinforcementQueueRunner.ExecuteAsync(
            entries,
            controller,
            ReinforcementFeature,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(1));

        Assert.Equal([ReinforcementQueueItemStatus.TimedOut, ReinforcementQueueItemStatus.TimedOut], results.Select(result => result.Status));
    }

}
