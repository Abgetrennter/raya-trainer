namespace RayaTrainer.Host.Services;

/// <summary>
/// Explicit session lifecycle phases for <see cref="TrainerSessionManager"/>.
/// Legal transitions:
///   Detached  --AttachTargetAsync-->  Attaching --> Attached | PatchesInstalled (failure returns to Detached)
///   Attached/PatchesInstalled --InstallPatchesAsync--> PatchesInstalled
///   Attached/PatchesInstalled --ResetPatchesStateAsync--> Detached
///   any non-Attaching phase --MarkTargetOfflineAsync--> Offline
///   any phase --ClearSessionState/Dispose--> Detached
/// Attaching rejects Install/Reset/MarkOffline at the entry guards.
/// </summary>
internal enum SessionPhase
{
    Detached,
    Attaching,
    Attached,
    PatchesInstalled,
    Offline
}
