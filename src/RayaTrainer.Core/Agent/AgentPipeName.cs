namespace RayaTrainer.Core.Agent;

public static class AgentPipeName
{
    public const string Prefix = "RayaTrainer.Agent.";

    public static string ForProcessId(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process id must be positive.");
        }

        return Prefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
