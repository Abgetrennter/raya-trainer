using RayaTrainer.App.Services;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class GameApiCommandQueueTests
{
    [Fact]
    public async Task RunAsyncSerializesConcurrentCommands()
    {
        var queue = new GameApiCommandQueue();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();

        var first = queue.RunAsync(async _ =>
        {
            events.Add("first-start");
            firstEntered.SetResult();
            await releaseFirst.Task;
            events.Add("first-end");
            return 1;
        });

        await firstEntered.Task;

        var second = queue.RunAsync(_ =>
        {
            events.Add("second");
            return Task.FromResult(2);
        });

        await Task.Delay(50);
        Assert.Equal(["first-start"], events);

        releaseFirst.SetResult();

        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
        Assert.Equal(["first-start", "first-end", "second"], events);
    }

    [Fact]
    public async Task RunAsyncPropagatesOperationExceptions()
    {
        var queue = new GameApiCommandQueue();
        var expected = new TimeoutException("game paused");

        var actual = await Assert.ThrowsAsync<TimeoutException>(() =>
            queue.RunAsync<int>(_ => throw expected));

        Assert.Same(expected, actual);
    }
}
