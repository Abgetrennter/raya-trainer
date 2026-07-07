using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.Services;

public sealed record SessionInstallOutcome(
    PatchMismatchReportResult PatchResult,
    string StatusMessage);

public interface ITrainerSessionService : IDisposable
{
    ITrainerFeatureController? FeatureController { get; }

    bool ArePatchesInstalled { get; }

    int? TargetProcessId { get; }

    bool CanUseFeatures { get; }

    int InstalledHookCount { get; }

    string RemoteSymbolSummary { get; }

    AttachResult AttachTarget(TrainerManifest manifest, TrainerTarget target);

    SessionInstallOutcome InstallPatches(TrainerManifest manifest, string diagnosticsDir);

    void ResetPatchesState();

    void MarkTargetOffline();

    bool IsTargetGameForeground();

    FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature);
}
