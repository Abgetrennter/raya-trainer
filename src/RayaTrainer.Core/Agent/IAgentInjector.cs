namespace RayaTrainer.Core.Agent;

public interface IAgentInjector
{
    AgentInjectionResult Inject(int processId, string agentDllPath, TimeSpan timeout);
}
