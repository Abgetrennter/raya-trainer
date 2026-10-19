namespace RayaTrainer.Core.Agent;

public enum GameApiDispatchStatus : uint
{
    Idle = 0,
    Pending = 1,
    Completed = 2,
    Disabled = 3,
    Failed = 4,
    TimedOut = 5,
    NoGameTick = 6,
    StaleRequest = 7,
    NoSelectedUnit = 8
}
