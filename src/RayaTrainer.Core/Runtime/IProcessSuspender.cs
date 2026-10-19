namespace RayaTrainer.Core.Runtime;

public interface IProcessSuspender
{
    IDisposable Suspend(int processId);
}
