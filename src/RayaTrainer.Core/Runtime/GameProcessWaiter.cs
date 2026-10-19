namespace RayaTrainer.Core.Runtime;

public sealed class GameProcessWaiter
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly Func<string, TrainerTarget?> _find;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _utcNow;

    public GameProcessWaiter()
        : this(new TrainerProcessLocator().Find)
    {
    }

    public GameProcessWaiter(
        Func<string, TrainerTarget?> find,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _find = find;
        _delay = delay ?? Task.Delay;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<TrainerTarget?> WaitForAsync(
        string processName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        var deadline = _utcNow() + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = _find(processName);
            if (target is not null)
            {
                return target;
            }

            var remaining = deadline - _utcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            await _delay(Min(DefaultPollInterval, remaining), cancellationToken);
        }
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second)
    {
        return first <= second ? first : second;
    }
}
