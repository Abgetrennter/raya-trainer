using RayaTrainer.App.Services;
using RayaTrainer.Core.Hotkeys;

namespace RayaTrainer.App.ViewModels;

public sealed class HotkeyCoordinator : IDisposable
{
    private readonly HotkeyOrchestrator _orchestrator = new();

    public HotkeyCoordinator(Func<bool> isTargetForeground)
    {
        _orchestrator.SetForegroundChecker(isTargetForeground);
    }

    public void Start(
        IEnumerable<FeatureItemViewModel> features,
        IEnumerable<HotkeyActionBinding> actions)
    {
        var bindings = features
            .Select(item => HotkeyGesture.TryParse(item.Hotkey, out var gesture)
                ? new HotkeyActionBinding(
                    gesture,
                    item.ExecuteFromHotkey,
                    () => item.Command.CanExecute(null),
                    AllowRepeat: !item.IsToggle)
                : null)
            .Where(binding => binding is not null)
            .Cast<HotkeyActionBinding>()
            .Concat(actions)
            .ToArray();
        _orchestrator.Start(bindings);
    }

    public void Stop() => _orchestrator.Stop();

    public void Dispose() => _orchestrator.Dispose();
}
