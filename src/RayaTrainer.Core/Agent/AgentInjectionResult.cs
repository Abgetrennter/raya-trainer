namespace RayaTrainer.Core.Agent;

public sealed record AgentInjectionResult(
    bool Success,
    string Message,
    nint RemoteModuleHandle);
