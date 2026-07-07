using RayaTrainer.App.Web.State;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class GameStateBroadcasterTests
{
    [Fact]
    public async Task PublishFansOutToAllSubscribers()
    {
        var broadcaster = new GameStateBroadcaster();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var first = broadcaster.Subscribe(cancellation.Token);
        var second = broadcaster.Subscribe(cancellation.Token);
        var message = TrainerWebStateMessage.Status("ready");

        broadcaster.Publish(message);

        Assert.Equal(message, await first.ReadAsync(cancellation.Token));
        Assert.Equal(message, await second.ReadAsync(cancellation.Token));
    }
}
