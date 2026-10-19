using System.Diagnostics;

namespace RayaTrainer.Core.Runtime;

public interface ITargetProcessState
{
    bool IsRunning(int? processId);
}

public sealed class TargetProcessState : ITargetProcessState
{
    public static TargetProcessState Instance { get; } = new();

    private TargetProcessState()
    {
    }

    public bool IsRunning(int? processId)
    {
        if (processId is null)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            process.Refresh();
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
