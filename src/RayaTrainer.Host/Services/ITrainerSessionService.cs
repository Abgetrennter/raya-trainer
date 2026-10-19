using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Host.Services;

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

    Task<AttachResult> AttachTargetAsync(TrainerManifest manifest, TrainerTarget target);

    Task<SessionInstallOutcome> InstallPatchesAsync(TrainerManifest manifest, string diagnosticsDir);

    Task ResetPatchesStateAsync();

    Task MarkTargetOfflineAsync();

    // Pure in-memory foreground probe (single Win32 call). Deliberately synchronous: the
    // hotkey layer polls it from keyboard-hook callbacks via a Func<bool> contract.
    bool IsTargetGameForeground();

    // In-game overlay visibility toggle, driven by the App F10 hotkey (works under
    // RA3's in-match DirectInput). CanToggleOverlay gates the hotkey binding.
    bool CanToggleOverlay { get; }

    Task ToggleOverlayVisibilityAsync();

    // Pure in-memory capability computation. Deliberately synchronous: UI availability
    // is read through the synchronous IFeatureHost contract (FeatureItemViewModel.Capability
    // is a plain property getter and cannot await).
    FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature);
}
