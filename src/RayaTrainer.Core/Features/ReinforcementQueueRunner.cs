using RayaTrainer.Core.Manifest;

namespace RayaTrainer.Core.Features;

public sealed record ReinforcementQueueEntry(
    string Name,
    string UnitIdText,
    string CountText,
    string RankText);

public enum ReinforcementQueueItemStatus
{
    Pending,
    Executing,
    Executed,
    Skipped,
    TimedOut,
    Failed,

    /// <summary>
    /// The batch was aborted because the game thread stayed paused past the
    /// grace period; this item was never attempted (or never completed).
    /// </summary>
    AbortedDueToPause
}

public sealed record ReinforcementQueueResult(
    ReinforcementQueueEntry Entry,
    ReinforcementQueueItemStatus Status,
    string Message);

public static class ReinforcementQueueRunner
{
    public static async Task<IReadOnlyList<ReinforcementQueueResult>> ExecuteAsync(
        IEnumerable<ReinforcementQueueEntry> entries,
        ITrainerFeatureController controller,
        TrainerFeature reinforcementFeature,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default,
        TimeSpan? pausedGracePeriod = null,
        Action<DispatchWaitStatus>? onWaitStatusChanged = null)
    {
        var results = new List<ReinforcementQueueResult>();
        var batchAbortedDueToPause = false;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Once the game has stayed paused past the grace period, remaining
            // items are not attempted — the game-thread dispatcher isn't being consumed, so
            // running them would just burn the active-timeout per item.
            if (batchAbortedDueToPause)
            {
                results.Add(new ReinforcementQueueResult(
                    entry,
                    ReinforcementQueueItemStatus.AbortedDueToPause,
                    "游戏保持暂停状态，已跳过本批次剩余条目。"));
                continue;
            }

            ReinforcementSettings settings;
            try
            {
                settings = ReinforcementSettings.Parse(entry.UnitIdText, entry.CountText, entry.RankText);
            }
            catch (Exception ex)
            {
                results.Add(new ReinforcementQueueResult(entry, ReinforcementQueueItemStatus.Skipped, ex.Message));
                continue;
            }

            try
            {
                var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                    reinforcementFeature,
                    settings,
                    timeout,
                    pollInterval,
                    cancellationToken: cancellationToken,
                    pausedGracePeriod: pausedGracePeriod,
                    onWaitStatusChanged: onWaitStatusChanged);
                results.Add(new ReinforcementQueueResult(entry, ToStatus(result), ToMessage(result)));
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
                results.Add(new ReinforcementQueueResult(
                    entry,
                    ReinforcementQueueItemStatus.AbortedDueToPause,
                    "通信通道超时（游戏可能已暂停），已放弃本条及剩余条目。"));
                batchAbortedDueToPause = true;
            }
            catch (Exception ex)
            {
                results.Add(new ReinforcementQueueResult(entry, ReinforcementQueueItemStatus.Failed, ex.Message));
            }
        }

        return results;
    }

    private static ReinforcementQueueItemStatus ToStatus(ActionDispatchResult result)
    {
        return result switch
        {
            ActionDispatchResult.Consumed => ReinforcementQueueItemStatus.Executed,
            ActionDispatchResult.TimedOut => ReinforcementQueueItemStatus.TimedOut,
            ActionDispatchResult.AbortedDueToPause => ReinforcementQueueItemStatus.AbortedDueToPause,
            _ => ReinforcementQueueItemStatus.Failed
        };
    }

    private static string ToMessage(ActionDispatchResult result)
    {
        return result switch
        {
            ActionDispatchResult.Consumed => "已执行",
            ActionDispatchResult.TimedOut => "动作已写入但尚未被游戏循环消费。",
            _ => "增援功能不是可分发动作。"
        };
    }
}
