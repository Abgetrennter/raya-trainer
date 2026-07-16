using RayaTrainer.Core.Versions;

namespace RayaTrainer.App.Services;

public enum GameWatcherState
{
    Disabled,
    Standby,
    Attaching,
    Attached,
    Rewinding,
    AwaitingAmbiguityResolution
}

public sealed record GameTargetFoundEventArgs(DetectedRa3Target Target);

public sealed record AmbiguousCandidatesEventArgs(IReadOnlyList<DetectedRa3Target> Candidates);

public sealed record WatcherStateChangedEventArgs(GameWatcherState Previous, GameWatcherState Current);

public sealed class GameProcessWatcher : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private readonly TimeSpan _interval;
    private readonly Func<TargetSelectionResult> _selectTargets;
    private CancellationTokenSource? _cancellation;
    private GameWatcherState _state = GameWatcherState.Disabled;
    private bool _suspended;

    public GameProcessWatcher(TimeSpan? interval = null, Func<TargetSelectionResult>? selectTargets = null)
    {
        _interval = interval ?? DefaultInterval;
        if (_interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }
        _selectTargets = selectTargets ?? DefaultSelectTargets;
    }

    public event EventHandler<GameTargetFoundEventArgs>? TargetFound;
    public event EventHandler<AmbiguousCandidatesEventArgs>? AmbiguousCandidatesDetected;
    public event EventHandler<WatcherStateChangedEventArgs>? StateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _state != GameWatcherState.Disabled;
            }
        }
    }

    public GameWatcherState CurrentState
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public void Start()
    {
        (GameWatcherState Prev, GameWatcherState Next)? transition;
        lock (_sync)
        {
            if (_state != GameWatcherState.Disabled)
            {
                return; // idempotent
            }
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _ = RunAsync(token);
            transition = Transition(GameWatcherState.Standby);
        }
        if (transition.HasValue)
        {
            StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(
                transition.Value.Prev, transition.Value.Next));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? oldCts;
        (GameWatcherState Prev, GameWatcherState Next)? transition;
        lock (_sync)
        {
            oldCts = _cancellation;
            _cancellation = null;
            transition = Transition(GameWatcherState.Disabled);
        }
        oldCts?.Cancel();
        oldCts?.Dispose();
        if (transition.HasValue)
        {
            StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(
                transition.Value.Prev, transition.Value.Next));
        }
    }

    public void NotifyAttached()
    {
        (GameWatcherState Prev, GameWatcherState Next)? transition;
        lock (_sync)
        {
            if (_state == GameWatcherState.Attached)
            {
                return; // no-op: already attached
            }
            transition = Transition(GameWatcherState.Attached);
        }
        if (transition.HasValue)
        {
            StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(
                transition.Value.Prev, transition.Value.Next));
        }
    }

    public void NotifyAttachFailed()
    {
        (GameWatcherState Prev, GameWatcherState Next)? t1 = null;
        (GameWatcherState Prev, GameWatcherState Next)? t2 = null;
        lock (_sync)
        {
            if (_state == GameWatcherState.Attaching || _state == GameWatcherState.AwaitingAmbiguityResolution)
            {
                t1 = Transition(GameWatcherState.Rewinding);
                t2 = Transition(GameWatcherState.Standby);
            }
        }
        if (t1.HasValue)
        {
            StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(t1.Value.Prev, t1.Value.Next));
        }
        if (t2.HasValue)
        {
            StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(t2.Value.Prev, t2.Value.Next));
        }
    }

    public void OnSessionOffline()
    {
        (GameWatcherState Prev, GameWatcherState Next)? t1 = null;
        (GameWatcherState Prev, GameWatcherState Next)? t2 = null;
        lock (_sync)
        {
            if (_state == GameWatcherState.Attached)
            {
                t1 = Transition(GameWatcherState.Rewinding);
                t2 = Transition(GameWatcherState.Standby);
            }
        }
        if (t1.HasValue)
        {
            StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(t1.Value.Prev, t1.Value.Next));
        }
        if (t2.HasValue)
        {
            StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(t2.Value.Prev, t2.Value.Next));
        }
    }

    public void ResolveAmbiguity(DetectedRa3Target target)
    {
        (GameWatcherState Prev, GameWatcherState Next)? transition;
        lock (_sync)
        {
            if (_state != GameWatcherState.AwaitingAmbiguityResolution)
            {
                return;
            }
            transition = Transition(GameWatcherState.Attaching);
        }
        if (transition.HasValue)
        {
            StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(
                transition.Value.Prev, transition.Value.Next));
        }
        // Re-raise TargetFound so MainViewModel attaches the user-chosen target.
        TargetFound?.Invoke(this, new GameTargetFoundEventArgs(target));
    }

    public void CancelAmbiguity()
    {
        (GameWatcherState Prev, GameWatcherState Next)? transition = null;
        lock (_sync)
        {
            if (_state == GameWatcherState.AwaitingAmbiguityResolution)
            {
                transition = Transition(GameWatcherState.Standby);
            }
        }
        if (transition.HasValue)
        {
            StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(
                transition.Value.Prev, transition.Value.Next));
        }
    }

    public void Suspend()
    {
        lock (_sync)
        {
            _suspended = true;
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            _suspended = false;
        }
    }

    public void Dispose()
    {
        Stop();
        TargetFound = null;
        AmbiguousCandidatesDetected = null;
        StateChanged = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (Volatile.Read(ref _suspended))
                {
                    continue;
                }

                GameWatcherState snapshot;
                lock (_sync)
                {
                    snapshot = _state;
                }

                if (snapshot != GameWatcherState.Standby)
                {
                    continue; // only Standby ticks the probe
                }

                TargetSelectionResult selection;
                try
                {
                    selection = _selectTargets();
                }
                catch
                {
                    continue; // swallow probe errors; retry next tick
                }

                HandleSelection(selection);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void HandleSelection(TargetSelectionResult selection)
    {
        switch (selection.Status)
        {
            case TargetSelectionStatus.SingleAutoSelected:
            case TargetSelectionStatus.SingleSupportedAmongMany:
                if (selection.SelectedTarget is null)
                {
                    return;
                }
                (GameWatcherState Prev, GameWatcherState Next)? singleTransition;
                lock (_sync)
                {
                    if (_state != GameWatcherState.Standby)
                    {
                        return;
                    }
                    singleTransition = Transition(GameWatcherState.Attaching);
                }
                if (singleTransition.HasValue)
                {
                    StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(
                        singleTransition.Value.Prev, singleTransition.Value.Next));
                }
                TargetFound?.Invoke(this, new GameTargetFoundEventArgs(selection.SelectedTarget));
                break;

            case TargetSelectionStatus.AmbiguousRequiresUserChoice:
                (GameWatcherState Prev, GameWatcherState Next)? ambiguousTransition;
                lock (_sync)
                {
                    if (_state != GameWatcherState.Standby)
                    {
                        return;
                    }
                    ambiguousTransition = Transition(GameWatcherState.AwaitingAmbiguityResolution);
                }
                if (ambiguousTransition.HasValue)
                {
                    StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(
                        ambiguousTransition.Value.Prev, ambiguousTransition.Value.Next));
                }
                AmbiguousCandidatesDetected?.Invoke(
                    this,
                    new AmbiguousCandidatesEventArgs(selection.Candidates));
                break;

            case TargetSelectionStatus.NoCandidate:
            case TargetSelectionStatus.NoInstallableCandidate:
            default:
                // stay in Standby
                break;
        }
    }

    private (GameWatcherState Prev, GameWatcherState Next) Transition(GameWatcherState next)
    {
        // MUST be called under _lock
        var prev = _state;
        _state = next;
        return (prev, next);
    }

    private static TargetSelectionResult DefaultSelectTargets()
    {
        // Production wiring: MainViewModel injects _locator.SelectDefault.
        return new TargetSelectionResult(
            TargetSelectionStatus.NoCandidate,
            null,
            Array.Empty<DetectedRa3Target>(),
            "No locator configured.");
    }
}
