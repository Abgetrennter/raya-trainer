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

    public Task<AttachResult> AttachAsync(TrainerManifest manifest, TrainerTarget target) =>
        _session.AttachTargetAsync(manifest, target);

    public async Task<SessionInstallOutcome> InstallAsync(
        TrainerManifest manifest,
        string diagnosticsDirectory,
        ResourceValueSettings resourceValues)
    {
        var outcome = await _session.InstallPatchesAsync(manifest, diagnosticsDirectory).ConfigureAwait(true);
        _session.FeatureController?.WriteResourceValues(resourceValues);
        return outcome;
    }

    // Explicit user-initiated shutdown (the "恢复 Patch" button): restore the Agent-owned core
    // hooks (plan §6.2 / §646). This is the ONLY path that asks the Agent to restore; after it the
    // runtime must be re-injected (game restart) to re-enable, because InstallCoreHooks only runs
    // once at Agent init.
    public Task RestoreRuntimeAsync() => _session.ResetPatchesStateAsync();

    // Ordinary teardown (re-detect / RefreshProcess, target switch, target offline, app exit):
    // never restores the Agent-owned runtime (plan §497 / §667 / §502). The Agent keeps its
    // self-installed hooks until it is ejected, so a normal disconnect just drops the App session.
    public Task EndSessionAsync() => _session.MarkTargetOfflineAsync();
}
