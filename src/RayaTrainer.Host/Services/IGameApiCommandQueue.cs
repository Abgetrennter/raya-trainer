namespace RayaTrainer.Host.Services;

public interface IGameApiCommandQueue
{
    Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> command,
        CancellationToken cancellationToken = default);
}
