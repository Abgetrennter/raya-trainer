namespace RayaTrainer.Core.Agent;

public enum AgentStatusCode : ushort
{
    Ok = 0,
    Pending = 1,
    Consumed = 2,
    TimedOut = 3,
    VersionMismatch = 4,
    PatchMismatch = 5,
    InvalidCommand = 6,
    InternalError = 7
}
