using RayaTrainer.App.Web.WebSockets;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerWebSocketTests
{
    [Fact]
    public void TryReadAuthTokenAcceptsJsonFirstMessage()
    {
        var parsed = TrainerWebSocketEndpoint.TryReadAuthToken("{\"token\":\"abc123\"}", out var token);

        Assert.True(parsed);
        Assert.Equal("abc123", token);
    }

    [Fact]
    public void HeartbeatMessageUsesHeartbeatType()
    {
        var message = TrainerWebSocketEndpoint.CreateHeartbeatMessage();

        Assert.Equal("heartbeat", message.Type);
    }
}
