using System.Threading.Channels;

namespace RayaTrainer.App.Web.State;

public sealed class GameStateBroadcaster : IGameStateBroadcaster
{
    /// <summary>
    /// 每个 WebSocket 订阅者的 channel 容量上限。
    /// 状态推送是"最新值优先"语义——旧消息无价值，慢消费者不应导致内存堆积。
    /// 用 DropOldest：超出容量时丢弃最旧的消息，保留最新状态。
    /// </summary>
    private const int SubscriberChannelCapacity = 16;

    private readonly object _gate = new();
    private readonly List<Channel<TrainerWebStateMessage>> _subscribers = [];

    public ChannelReader<TrainerWebStateMessage> Subscribe(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<TrainerWebStateMessage>(
            new BoundedChannelOptions(SubscriberChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                channel.Writer.TryComplete();
                Remove(channel);
            });
        }

        return channel.Reader;
    }

    public void Publish(TrainerWebStateMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Channel<TrainerWebStateMessage>[] subscribers;
        lock (_gate)
        {
            subscribers = _subscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            if (!subscriber.Writer.TryWrite(message))
            {
                Remove(subscriber);
            }
        }
    }

    private void Remove(Channel<TrainerWebStateMessage> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }
    }

    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;

    public void StartPolling(
        Func<TrainerGameStateResponse?> gameStateProvider,
        Func<TrainerSelectedUnitResponse?> selectedUnitProvider,
        Func<TrainerFeaturesResponse?> featuresProvider)
    {
        StopPolling();
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        _pollingTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(token).ConfigureAwait(false);

                    var gameState = gameStateProvider();
                    if (gameState is not null)
                    {
                        Publish(TrainerWebStateMessage.GameStateUpdate(gameState));
                    }

                    var selectedUnit = selectedUnitProvider();
                    if (selectedUnit is not null)
                    {
                        Publish(TrainerWebStateMessage.SelectedUnitUpdate(selectedUnit));
                    }

                    var features = featuresProvider();
                    if (features is not null)
                    {
                        Publish(TrainerWebStateMessage.FeaturesUpdate(features));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // 轮询会持续读游戏内存（GameMode、SelectedUnit、开关状态），
                    // 在游戏退出、切主菜单导致指针失效、或会话被 Reset 释放了
                    // _memory 时，provider 会抛 Win32Exception / InvalidOperationException。
                    // 若不在这里吞掉，会变成"未观察的 Task 异常"，最终经 GC 把进程带崩
                    // （崩溃码 0xe0434352 = CLR 托管异常）。记录后继续轮询，等下一 tick
                    // 自愈或等 StopPolling 正常取消。
                    RayaTrainerCrashLog.Write(exception);
                }
            }
        }, token);
    }

    public void StopPolling()
    {
        var cts = _pollingCts;
        _pollingCts = null;
        cts?.Cancel();
        // 等待轮询 task 退出，避免旧轮询在 StopPolling 后继续 Publish。
        // task 内部 catch OperationCanceledException 后 break，通常很快返回。
        try
        {
            _pollingTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // 轮询 task 内部已吞掉预期异常；这里只防止 Wait 抛出残留。
        }

        cts?.Dispose();
    }
}
