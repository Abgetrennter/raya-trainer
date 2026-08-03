namespace RayaTrainer.App.Services;

public sealed class GameApiCommandQueue : IGameApiCommandQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await command(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
