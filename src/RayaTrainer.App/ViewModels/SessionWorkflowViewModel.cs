using RayaTrainer.App.Services;
using RayaTrainer.Host.Services;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

public sealed class SessionWorkflowViewModel
{
    private readonly ITrainerSessionService _session;

    public SessionWorkflowViewModel(ITrainerSessionService session)
    {
        _session = session;
    }

    public AttachResult Attach(TrainerManifest manifest, TrainerTarget target) =>
        _session.AttachTarget(manifest, target);

    // Agent injection and signature resolution are synchronous at the Host boundary. Keep that
    // compatibility contract, but never make the WPF dispatcher perform the blocking work.
    public Task<AttachResult> AttachAsync(TrainerManifest manifest, TrainerTarget target) =>
        Task.Factory.StartNew(
            () => _session.AttachTarget(manifest, target),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    public SessionInstallOutcome Install(
        TrainerManifest manifest,
        string diagnosticsDirectory,
        ResourceValueSettings resourceValues)
    {
        var outcome = _session.InstallPatches(manifest, diagnosticsDirectory);
        _session.FeatureController?.WriteResourceValues(resourceValues);
        return outcome;
    }

    // Explicit user-initiated shutdown (the "恢复 Patch" button): restore the Agent-owned core
    // hooks (plan §6.2 / §646). This is the ONLY path that asks the Agent to restore; after it the
    // runtime must be re-injected (game restart) to re-enable, because InstallCoreHooks only runs
    // once at Agent init.
    public void RestoreRuntime() => _session.ResetPatchesState();

    // Ordinary teardown (re-detect / RefreshProcess, target switch, target offline, app exit):
    // never restores the Agent-owned runtime (plan §497 / §667 / §502). The Agent keeps its
    // self-installed hooks until it is ejected, so a normal disconnect just drops the App session.
    public void EndSession() => _session.MarkTargetOffline();
}
