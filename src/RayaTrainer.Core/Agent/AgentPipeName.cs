namespace RayaTrainer.Core.Agent;

public static class AgentPipeName
{
    public const string Prefix = "RayaTrainer.Agent.";
    public const string LegacyPrefix = "Ra3Trainer.Agent.";

    public static string ForProcessId(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process id must be positive.");
        }

        return Prefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string LegacyForProcessId(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process id must be positive.");
        }

        return LegacyPrefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
