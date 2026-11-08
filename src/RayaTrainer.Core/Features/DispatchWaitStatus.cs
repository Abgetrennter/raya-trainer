namespace RayaTrainer.Core.Features;

/// <summary>
/// Reports the lifecycle of a dispatch-consumption wait so the caller (UI / Web)
/// can surface pause-aware feedback. Emitted via the
/// <see cref="ITrainerFeatureController.TriggerActionAndWaitForConsumptionAsync"/>
/// <c>onWaitStatusChanged</c> callback.
/// </summary>
public enum DispatchWaitStatus
{
    /// <summary>
    /// Polling the action-dispatch slot during the active (non-paused) phase.
    /// </summary>
    Polling,

    /// <summary>
    /// The game thread heartbeat is frozen; the wait has entered the paused
    /// grace period and is holding for the game to resume before giving up.
    /// </summary>
    PausedWaiting,

    /// <summary>
    /// The game thread resumed advancing within the grace period; polling is
    /// resuming with a fresh active timeout.
    /// </summary>
    Resumed,

    /// <summary>
    /// The grace period elapsed without the game thread resuming; the dispatch
    /// is being abandoned as <see cref="ActionDispatchResult.AbortedDueToPause"/>.
    /// </summary>
    GraceExpired
}
