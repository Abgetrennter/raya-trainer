using RayaTrainer.App.Services;
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

    public SessionInstallOutcome Install(
        TrainerManifest manifest,
        string diagnosticsDirectory,
        ResourceValueSettings resourceValues)
    {
        var outcome = _session.InstallPatches(manifest, diagnosticsDirectory);
        _session.FeatureController?.WriteResourceValues(resourceValues);
        return outcome;
    }

    public void End(bool targetOffline)
    {
        if (targetOffline)
        {
            _session.MarkTargetOffline();
        }
        else
        {
            _session.ResetPatchesState();
        }
    }
}
