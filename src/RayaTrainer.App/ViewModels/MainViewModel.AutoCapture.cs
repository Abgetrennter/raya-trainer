using System.Windows;
using RayaTrainer.App.Services;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.App.ViewModels;

public sealed partial class MainViewModel
{
    private void OnAutoCaptureTargetFound(object? sender, GameTargetFoundEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => AttachAutoCapturedTarget(e.Target)));
            return;
        }
        AttachAutoCapturedTarget(e.Target);
    }

    private void AttachAutoCapturedTarget(DetectedRa3Target target)
    {
        // Guard against: user disabled auto-capture since watcher ticked,
        // racing with a manual LaunchAndLoad (IsBusy), or an attach that
        // completed between the watcher tick and this UI-thread callback.
        // In all these cases, sync the watcher state and drop the event.
        if (!_autoCaptureEnabled || IsBusy || _sessionManager.TargetProcessId is not null)
        {
            _autoCaptureWatcher.NotifyAttached();
            return;
        }
        StatusMessage = "已检测到红色警戒3，正在自动连接…";
        AttachTarget(target.ToTrainerTarget(), autoInstall: true);
    }

    private void OnAutoCaptureAmbiguousCandidates(object? sender, AmbiguousCandidatesEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => PresentAmbiguousCandidates(e.Candidates)));
            return;
        }
        PresentAmbiguousCandidates(e.Candidates);
    }

    private void PresentAmbiguousCandidates(IReadOnlyList<DetectedRa3Target> candidates)
    {
        SelectableCandidates = candidates
            .Where(c => c.SupportStatus == TargetSupportStatus.Installable)
            .ToArray();
        StatusMessage = "自动捕获发现多个红色警戒3，请在下方列表中选择一个再连接。";
        RaiseCommandStates();
    }

    private void OnAutoCaptureStateChanged(object? sender, WatcherStateChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(RaisePrimaryActionState));
            return;
        }
        RaisePrimaryActionState();
    }

    /// <summary>
    /// Called when the user toggles the AutoCapture switch in settings.
    /// Persists and starts/stops the watcher.
    /// </summary>
    public void SetAutoCaptureEnabled(bool enabled)
    {
        if (_autoCaptureEnabled == enabled)
        {
            return;
        }
        _autoCaptureEnabled = enabled;
        if (enabled)
        {
            // Start the background loop FIRST (Disabled→Standby), THEN pin to
            // Attached if a session is already active. Order matters: if
            // NotifyAttached runs first, Start() early-returns (state≠Disabled)
            // and the loop never launches, killing auto-reconnect after game exit.
            _autoCaptureWatcher.Start();
            if (_sessionManager.TargetProcessId is not null)
            {
                _autoCaptureWatcher.NotifyAttached();
            }
        }
        else
        {
            _autoCaptureWatcher.Stop();
        }
        PersistSettings();
        OnPropertyChanged(nameof(IsAutoCaptureEnabled));
        RaiseCommandStates();
    }
}
