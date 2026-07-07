using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RayaTrainer.App.Web.Auth;
using RayaTrainer.App.Web.State;

namespace RayaTrainer.App.Web.WebSockets;

public static class TrainerWebSocketEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static async Task HandleAsync(
        HttpContext context,
        DevicePairingTokenStore tokenStore,
        IGameStateBroadcaster broadcaster,
        TrainerApiHandler handler)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var cancellationToken = context.RequestAborted;
        var authMessage = await ReceiveTextAsync(socket, cancellationToken).ConfigureAwait(false);
        if (!TryReadAuthToken(authMessage, out var token) || !tokenStore.ValidateToken(token))
        {
            await socket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    "Unauthorized",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await SendJsonAsync(
                socket,
                TrainerWebStateMessage.Status("connected", handler.GetStatus()),
                cancellationToken)
            .ConfigureAwait(false);
        await ForwardMessagesAsync(socket, broadcaster, cancellationToken).ConfigureAwait(false);
    }

    public static bool TryReadAuthToken(string? message, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var trimmed = message.Trim();
        if (!trimmed.StartsWith('{'))
        {
            token = trimmed;
            return token.Length > 0;
        }

        try
        {
            var auth = JsonSerializer.Deserialize<WebSocketAuthMessage>(trimmed, JsonOptions);
            token = auth?.Token?.Trim() ?? string.Empty;
            return token.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static TrainerWebStateMessage CreateHeartbeatMessage()
    {
        return TrainerWebStateMessage.Heartbeat();
    }

    private static async Task ForwardMessagesAsync(
        WebSocket socket,
        IGameStateBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        var reader = broadcaster.Subscribe(cancellationToken);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeat = new PeriodicTimer(HeartbeatInterval);

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var messageTask = reader.WaitToReadAsync(cancellationToken).AsTask();
                var heartbeatTask = WaitNextTickSafeAsync(heartbeat, heartbeatCts.Token);
                var completed = await Task.WhenAny(messageTask, heartbeatTask).ConfigureAwait(false);

                if (completed == messageTask)
                {
                    if (!await messageTask.ConfigureAwait(false))
                    {
                        return;
                    }

                    while (reader.TryRead(out var message))
                    {
                        await SendJsonAsync(socket, message, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (await heartbeatTask.ConfigureAwait(false))
                {
                    await SendJsonAsync(socket, CreateHeartbeatMessage(), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the connection is cancelled.
        }
        catch (WebSocketException)
        {
            // Expected when the socket is closed unexpectedly.
        }
        finally
        {
            heartbeatCts.Cancel();
        }
    }

    private static async Task<bool> WaitNextTickSafeAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<string?> ReceiveTextAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            return null;
        }

        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static Task SendJsonAsync(
        WebSocket socket,
        TrainerWebStateMessage message,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        return socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    private sealed record WebSocketAuthMessage(string? Token);
}
