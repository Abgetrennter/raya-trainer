namespace RayaTrainer.Core.Errors;

/// <summary>
/// Registered vocabulary of diagnostic event codes (design doc §6.1, roadmap M2.2).
/// Every event emitted into the diagnostics stream must take its Code from this class —
/// hand-written literals at call sites are forbidden so the event stream stays greppable
/// and coherent with the unified error vocabulary. Lifecycle/informational codes coexist
/// with error codes; error codes produced from wire enums come from
/// <see cref="TrainerErrorVocabulary"/> in the same lowercase dotted presentation form.
/// Codes are append-only and never renamed.
/// </summary>
public static class DiagnosticEventCodes
{
    // --- Session attach lifecycle ---
    public const string AttachStarted = "attach.started";
    public const string AttachFailed = "attach.failed";
    public const string AttachProfileUnsupported = "attach.profile_unsupported";
    public const string AttachVersionUnsupported = "attach.version_unsupported";

    // --- Agent lifecycle ---
    public const string AgentHandshake = "agent.handshake";
    public const string AgentAttached = "agent.attached";
    public const string AgentAttachFailed = "agent.attach_failed";
    public const string AgentHookMismatch = "agent.hook_mismatch";
    public const string AgentPatchsetInstallMismatch = "agent.patchset_install_mismatch";
    public const string AgentPatchsetCodeflowIpConflict = "agent.patchset_codeflow_ip_conflict";

    // --- Patch lifecycle ---
    public const string PatchInstallStarted = "patch.install_started";
    public const string PatchInstalled = "patch.installed";
    public const string PatchInstalledPartial = "patch.installed_partial";
    public const string PatchRestoreWarning = "patch.restore_warning";

    // --- Session lifecycle ---
    public const string SessionReset = "session.reset";

    // --- Runtime reads / asset loading ---
    public const string RuntimeRefreshRecovered = "runtime.refresh_recovered";
    public const string RuntimeRefreshFailed = "runtime.refresh_failed";
    public const string RuntimeAssetLoadReleasing = "runtime.asset_load_releasing";
    public const string RuntimeAssetLoadRequested = "runtime.asset_load_requested";
    public const string RuntimeAssetLoadFailed = "runtime.asset_load_failed";
    public const string RuntimeAssetDiagnostics = "runtime.asset_diagnostics";
    public const string RuntimeScriptDiagnostics = "runtime.script_diagnostics";
    public const string RuntimeMatchContextDiagnostics = "runtime.matchcontext_diagnostics";

    // --- Overlay lifecycle ---
    public const string OverlayReady = "overlay.ready";
    public const string OverlayDegraded = "overlay.degraded";

    // --- Product control plane ---
    public const string ProductControlRenegotiate = "product_control.renegotiate";
    public const string ProductControlNegotiateFailed = "product_control.negotiate_failed";

    // --- Preset projection sync ---
    public const string ReinforcementProjectionSynced = "reinforcement.projection_synced";
    public const string ReinforcementProjectionSyncFailed = "reinforcement.projection_sync_failed";
    public const string SecretProtocolProjectionSynced = "secretprotocol.projection_synced";
    public const string SecretProtocolProjectionSyncFailed = "secretprotocol.projection_sync_failed";

    // --- Target monitoring ---
    public const string TargetOffline = "target.offline";

    // --- LAA (large address aware) operations ---
    public const string LaaFound = "laa.found";
    public const string LaaMissing = "laa.missing";
    public const string LaaCheckFailed = "laa.check_failed";
    public const string LaaPatched = "laa.patched";
    public const string LaaPatchFailed = "laa.patch_failed";

    // --- Diagnostics bundle ---
    public const string DiagnosticsExported = "diagnostics.exported";
    public const string DiagnosticsExportFailed = "diagnostics.export_failed";
}
