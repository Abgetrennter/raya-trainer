using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class GameProcessWaiterTests
{
    [Fact]
    public async Task WaitForAsyncReturnsTargetWhenProcessAppearsBeforeTimeout()
    {
        var now = DateTimeOffset.UtcNow;
        var calls = 0;
        var expected = new TrainerTarget("ra3_1.12.game", 0x400000, true, true);
        var waiter = new GameProcessWaiter(
            _ => ++calls == 3 ? expected : null,
            (delay, _) =>
            {
                now += delay;
                return Task.CompletedTask;
            },
            () => now);

        var actual = await waiter.WaitForAsync("ra3_1.12.game", TimeSpan.FromSeconds(30));

        Assert.Same(expected, actual);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task WaitForAsyncReturnsNullWhenTimeoutExpires()
    {
        var now = DateTimeOffset.UtcNow;
        var waiter = new GameProcessWaiter(
            _ => null,
            (delay, _) =>
            {
                now += delay;
                return Task.CompletedTask;
            },
            () => now);

        var actual = await waiter.WaitForAsync("ra3_1.12.game", TimeSpan.FromSeconds(1));

        Assert.Null(actual);
    }
}
