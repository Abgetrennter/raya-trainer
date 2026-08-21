using RayaTrainer.Core.Manifest;

namespace RayaTrainer.Core.Features;

public sealed record SecretProtocolQueueEntry(SecretProtocolEntry Protocol);

public enum SecretProtocolQueueItemStatus
{
    Pending,
    Executing,
    Executed,
    TimedOut,
    Failed,

    /// <summary>
    /// The batch was aborted because the game thread stayed paused past the
    /// grace period; this item was never attempted (or never completed).
    /// </summary>
    AbortedDueToPause
}

public sealed record SecretProtocolQueueResult(
    SecretProtocolQueueEntry Entry,
    SecretProtocolQueueItemStatus Status,
    string Message);

public static class SecretProtocolQueueRunner
{
    public static async Task<IReadOnlyList<SecretProtocolQueueResult>> ExecuteAsync(
        IEnumerable<SecretProtocolQueueEntry> entries,
        ITrainerFeatureController controller,
        TrainerFeature grantFeature,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default,
        TimeSpan? pausedGracePeriod = null,
        Action<DispatchWaitStatus>? onWaitStatusChanged = null)
    {
        var results = new List<SecretProtocolQueueResult>();
        var batchAbortedDueToPause = false;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Once the game has stayed paused past the grace period, remaining
            // items are not attempted — the game-thread dispatcher isn't being consumed, so
            // running them would just burn the active-timeout per item.
            if (batchAbortedDueToPause)
            {
                results.Add(new SecretProtocolQueueResult(
                    entry,
                    SecretProtocolQueueItemStatus.AbortedDueToPause,
                    "游戏保持暂停状态，已跳过本批次剩余条目。"));
                continue;
            }

            try
            {
                if (!entry.Protocol.CanGrant)
                {
                    results.Add(new SecretProtocolQueueResult(entry, SecretProtocolQueueItemStatus.Failed, "该条目只有 SpecialPower 名称，需要补充对应 PlayerTech 或 Upgrade 后才能授予。"));
                    continue;
                }

                controller.WriteSecretProtocolGrantSettings(entry.Protocol.ToGrantSettings());
                var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                    grantFeature,
                    timeout: timeout,
                    pollInterval: pollInterval,
                    cancellationToken: cancellationToken,
                    pausedGracePeriod: pausedGracePeriod,
                    onWaitStatusChanged: onWaitStatusChanged);
                results.Add(new SecretProtocolQueueResult(entry, ToStatus(result), ToMessage(entry.Protocol, result)));
                if (result == ActionDispatchResult.AbortedDueToPause)
                {
                    batchAbortedDueToPause = true;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Transport-level cancellation (e.g. Agent single-instance pipe
                // contention while the game is paused). Treat as a pause abort
                // and short-circuit the batch so we don't burn the timeout on
                // every remaining entry.
                results.Add(new SecretProtocolQueueResult(
                    entry,
                    SecretProtocolQueueItemStatus.AbortedDueToPause,
                    "通信通道超时（游戏可能已暂停），已放弃本条及剩余条目。"));
                batchAbortedDueToPause = true;
            }
            catch (Exception ex)
            {
                results.Add(new SecretProtocolQueueResult(entry, SecretProtocolQueueItemStatus.Failed, ex.Message));
            }
        }

        return results;
    }

    private static SecretProtocolQueueItemStatus ToStatus(ActionDispatchResult result)
    {
        return result switch
        {
            ActionDispatchResult.Consumed => SecretProtocolQueueItemStatus.Executed,
            ActionDispatchResult.TimedOut => SecretProtocolQueueItemStatus.TimedOut,
            ActionDispatchResult.AbortedDueToPause => SecretProtocolQueueItemStatus.AbortedDueToPause,
            _ => SecretProtocolQueueItemStatus.Failed
        };
    }

    private static string ToMessage(SecretProtocolEntry protocol, ActionDispatchResult result)
    {
        return result switch
        {
            ActionDispatchResult.Consumed when protocol.PlayerTechId == 0 => "已授予 Upgrade。",
            ActionDispatchResult.Consumed when protocol.UpgradeId == 0 => "已授予 PlayerTech。",
            ActionDispatchResult.Consumed => "已授予 PlayerTech，并补发关联 Upgrade。",
            ActionDispatchResult.TimedOut => "动作已写入但尚未被游戏循环消费。",
            _ => "秘密协议授予功能不是可分发动作。"
        };
    }
}
