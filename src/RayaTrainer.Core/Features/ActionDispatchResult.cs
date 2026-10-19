namespace RayaTrainer.Core.Features;

public enum ActionDispatchResult
{
    NotRequired,
    Consumed,
    TimedOut,

    /// <summary>
    /// The game thread was paused (heartbeat frozen) for the full grace period
    /// and never resumed; the dispatch was abandoned without being consumed.
    /// Distinct from <see cref="TimedOut"/> (game running but action never
    /// consumed) so callers can decide whether to retry or surface a pause hint.
    /// </summary>
    AbortedDueToPause
}
