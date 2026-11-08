using RayaTrainer.Core.Agent;

namespace RayaTrainer.Core.Errors;

/// <summary>
/// One mapping entry of the unified error vocabulary (design doc §4.1): a wire protocol
/// enum member resolved to its origin domain, stable string id, retry hint and diagnostic
/// stage attribution. String ids use the <c>&lt;DOMAIN&gt;.&lt;NAME&gt;</c> identity form; the
/// diagnostic event stream presents them in lowercase dotted form (see
/// <see cref="ToEventCode"/>).
/// </summary>
public readonly record struct TrainerErrorMapping(
    ErrorDomain Domain,
    string Code,
    RetryHint RetryHint,
    string Stage);

/// <summary>
/// Central wire-enum → unified-vocabulary mapping (design doc §4.1). Lives in Core so the
/// mapping is consumable by every layer (Core diagnostics, Host classification, App, Web);
/// the Host-side classification workflow stays in Host. Exhaustiveness is anchored by
/// vocabulary contract tests: every wire enum member must resolve, ids must be unique, and
/// non-error members resolve to <c>null</c>.
/// </summary>
public static class TrainerErrorVocabulary
{
    // Diagnostic stage ids must stay aligned with TrainerDiagnosticState stage ids and the
    // product-plane layered stages (design doc §6.2 attribution table).
    public const string StageTarget = "target";
    public const string StageAgent = "agent";
    public const string StageSignature = "signature";
    public const string StagePatch = "patch";
    public const string StageGame = "game";
    public const string StageAdmission = "admission";
    public const string StageExecution = "execution";
    public const string StageEffect = "effect";
    public const string StageCompensation = "compensation";

    /// <summary>Event-stream presentation form: lowercase dotted, same vocabulary as <see cref="TrainerErrorMapping.Code"/>.</summary>
    public static string ToEventCode(string code) => code.ToLowerInvariant();

    /// <summary>
    /// Maps a transport-level <see cref="AgentStatusCode"/>. <see cref="AgentStatusCode.Ok"/>,
    /// <see cref="AgentStatusCode.Pending"/> and <see cref="AgentStatusCode.Consumed"/> are not
    /// failures and resolve to <c>null</c>.
    /// </summary>
    public static TrainerErrorMapping? Map(AgentStatusCode status) => status switch
    {
        AgentStatusCode.Ok or AgentStatusCode.Pending or AgentStatusCode.Consumed => null,
        AgentStatusCode.TimedOut => new(ErrorDomain.Execution, "EXEC.AGENT_TIMED_OUT", RetryHint.Retryable, StageAgent),
        AgentStatusCode.VersionMismatch => new(ErrorDomain.Contract, "CONTRACT.AGENT_VERSION_MISMATCH", RetryHint.NotRetryable, StageAgent),
        AgentStatusCode.PatchMismatch => new(ErrorDomain.Contract, "CONTRACT.PATCH_MISMATCH", RetryHint.NotRetryable, StageSignature),
        AgentStatusCode.InvalidCommand => new(ErrorDomain.Fault, "FAULT.INVALID_COMMAND", RetryHint.NotRetryable, StageAgent),
        AgentStatusCode.InternalError => new(ErrorDomain.Fault, "FAULT.AGENT_INTERNAL", RetryHint.NotRetryable, StageAgent),
        AgentStatusCode.UnsupportedTarget => new(ErrorDomain.Contract, "CONTRACT.UNSUPPORTED_TARGET", RetryHint.NotRetryable, StageTarget),
        _ => new(ErrorDomain.Fault, "FAULT.UNKNOWN_AGENT_STATUS", RetryHint.NotRetryable, StageAgent),
    };

    /// <summary>
    /// Maps a product-control <see cref="ProductErrorCode"/>. <see cref="ProductErrorCode.None"/>
    /// is not a failure and resolves to <c>null</c>. Retired wire values (4/10/12) fall through
    /// the default arm and must never be reintroduced.
    /// </summary>
    public static TrainerErrorMapping? Map(ProductErrorCode code) => code switch
    {
        ProductErrorCode.None => null,
        ProductErrorCode.InvalidRequest => new(ErrorDomain.Request, "REQ.INVALID_REQUEST", RetryHint.NotRetryable, StageAdmission),
        ProductErrorCode.CapabilityUnavailable => new(ErrorDomain.Contract, "CONTRACT.CAPABILITY_UNAVAILABLE", RetryHint.NotRetryable, StageAgent),
        ProductErrorCode.ContextUnavailable => new(ErrorDomain.Request, "REQ.CONTEXT_UNAVAILABLE", RetryHint.UserAction, StageAdmission),
        ProductErrorCode.IntentConflict => new(ErrorDomain.Request, "REQ.INTENT_CONFLICT", RetryHint.UserAction, StageAdmission),
        ProductErrorCode.QueueFull => new(ErrorDomain.Request, "REQ.QUEUE_FULL", RetryHint.Retryable, StageAdmission),
        ProductErrorCode.ResultExpired => new(ErrorDomain.Request, "REQ.RESULT_EXPIRED", RetryHint.Retryable, StageEffect),
        ProductErrorCode.ProductUnavailable => new(ErrorDomain.Request, "REQ.PRODUCT_UNAVAILABLE", RetryHint.UserAction, StageAdmission),
        ProductErrorCode.SchemaMismatch => new(ErrorDomain.Contract, "CONTRACT.PRODUCT_SCHEMA_MISMATCH", RetryHint.NotRetryable, StageAgent),
        ProductErrorCode.TargetRevisionMismatch => new(ErrorDomain.Request, "REQ.TARGET_REVISION_MISMATCH", RetryHint.Retryable, StageAdmission),
        ProductErrorCode.PolicyStale => new(ErrorDomain.Contract, "CONTRACT.POLICY_STALE", RetryHint.Retryable, StageAdmission),
        ProductErrorCode.ExecutionFault => new(ErrorDomain.Execution, "EXEC.PRODUCT_EXECUTION_FAULT", RetryHint.NotRetryable, StageExecution),
        ProductErrorCode.UnsupportedBinding => new(ErrorDomain.Request, "REQ.UNSUPPORTED_BINDING", RetryHint.NotRetryable, StageAdmission),
        ProductErrorCode.Superseded => new(ErrorDomain.Request, "REQ.SUPERSEDED", RetryHint.Retryable, StageAdmission),
        ProductErrorCode.InternalError => new(ErrorDomain.Fault, "FAULT.PRODUCT_INTERNAL", RetryHint.NotRetryable, StageExecution),
        _ => new(ErrorDomain.Fault, "FAULT.UNKNOWN_PRODUCT_ERROR", RetryHint.NotRetryable, StageExecution),
    };

    /// <summary>
    /// Maps an <see cref="AgentOverlayError"/>. <see cref="AgentOverlayError.None"/> is not a
    /// failure and resolves to <c>null</c>.
    /// </summary>
    public static TrainerErrorMapping? Map(AgentOverlayError error) => error switch
    {
        AgentOverlayError.None => null,
        AgentOverlayError.UnsupportedTarget => new(ErrorDomain.Contract, "CONTRACT.OVERLAY_UNSUPPORTED_TARGET", RetryHint.NotRetryable, StageTarget),
        AgentOverlayError.NativeCatalogUnavailable => new(ErrorDomain.Contract, "CONTRACT.OVERLAY_NATIVE_CATALOG_UNAVAILABLE", RetryHint.NotRetryable, StageSignature),
        AgentOverlayError.LogicFreezeHookUnavailable => new(ErrorDomain.Execution, "EXEC.OVERLAY_LOGIC_FREEZE_HOOK_UNAVAILABLE", RetryHint.NotRetryable, StagePatch),
        AgentOverlayError.D3D9Unavailable => new(ErrorDomain.Execution, "EXEC.OVERLAY_D3D9_UNAVAILABLE", RetryHint.NotRetryable, StageGame),
        AgentOverlayError.ProbeWindowFailed => new(ErrorDomain.Execution, "EXEC.OVERLAY_PROBE_WINDOW_FAILED", RetryHint.Retryable, StageGame),
        AgentOverlayError.ProbeDeviceFailed => new(ErrorDomain.Execution, "EXEC.OVERLAY_PROBE_DEVICE_FAILED", RetryHint.Retryable, StageGame),
        AgentOverlayError.InvalidVtableAddress => new(ErrorDomain.Fault, "FAULT.OVERLAY_INVALID_VTABLE_ADDRESS", RetryHint.NotRetryable, StageGame),
        AgentOverlayError.HookInitializationFailed => new(ErrorDomain.Execution, "EXEC.OVERLAY_HOOK_INITIALIZATION_FAILED", RetryHint.NotRetryable, StagePatch),
        AgentOverlayError.HookInstallationFailed => new(ErrorDomain.Execution, "EXEC.OVERLAY_HOOK_INSTALLATION_FAILED", RetryHint.NotRetryable, StagePatch),
        AgentOverlayError.WindowProcHookFailed => new(ErrorDomain.Execution, "EXEC.OVERLAY_WINDOW_PROC_HOOK_FAILED", RetryHint.NotRetryable, StagePatch),
        AgentOverlayError.ImGuiInitializationFailed => new(ErrorDomain.Execution, "EXEC.OVERLAY_IMGUI_INITIALIZATION_FAILED", RetryHint.NotRetryable, StageGame),
        AgentOverlayError.RenderThreadCleanupTimedOut => new(ErrorDomain.Execution, "EXEC.OVERLAY_RENDER_THREAD_CLEANUP_TIMED_OUT", RetryHint.Retryable, StageGame),
        _ => new(ErrorDomain.Fault, "FAULT.UNKNOWN_OVERLAY_ERROR", RetryHint.NotRetryable, StageGame),
    };

    /// <summary>
    /// Maps a Direct GameApi dispatch failure. Idle/Pending/Completed/Disabled are lifecycle
    /// states, not failures, and resolve to <c>null</c>.
    /// </summary>
    public static TrainerErrorMapping? Map(GameApiDispatchStatus status) => status switch
    {
        GameApiDispatchStatus.Idle or GameApiDispatchStatus.Pending or GameApiDispatchStatus.Completed or GameApiDispatchStatus.Disabled => null,
        GameApiDispatchStatus.Failed => new(ErrorDomain.Execution, "EXEC.GAMEAPI_DISPATCH_FAILED", RetryHint.Retryable, StageExecution),
        GameApiDispatchStatus.TimedOut => new(ErrorDomain.Execution, "EXEC.GAMEAPI_DISPATCH_TIMED_OUT", RetryHint.Retryable, StageExecution),
        GameApiDispatchStatus.NoGameTick => new(ErrorDomain.Request, "REQ.GAMEAPI_NO_GAME_TICK", RetryHint.UserAction, StageGame),
        GameApiDispatchStatus.StaleRequest => new(ErrorDomain.Request, "REQ.GAMEAPI_STALE_REQUEST", RetryHint.Retryable, StageExecution),
        GameApiDispatchStatus.NoSelectedUnit => new(ErrorDomain.Request, "REQ.GAMEAPI_NO_SELECTED_UNIT", RetryHint.UserAction, StageGame),
        _ => new(ErrorDomain.Fault, "FAULT.UNKNOWN_GAMEAPI_STATUS", RetryHint.NotRetryable, StageExecution),
    };

    // --- Web/WS endpoint-level failures (design doc §4.5). These never cross the wire to the
    // Agent; they classify HTTP-surface rejections so status codes and reason codes stay
    // unified. Append-only like every other vocabulary entry.

    public static TrainerErrorMapping WebSelectedUnitUnavailable { get; } =
        new(ErrorDomain.Request, "REQ.WEB_SELECTED_UNIT_UNAVAILABLE", RetryHint.UserAction, StageGame);

    public static TrainerErrorMapping WebCapabilityNotReady { get; } =
        new(ErrorDomain.Request, "REQ.WEB_CAPABILITY_NOT_READY", RetryHint.UserAction, StageAgent);

    public static TrainerErrorMapping WebUnitUpgradeReadFailed { get; } =
        new(ErrorDomain.Request, "REQ.WEB_UNIT_UPGRADE_READ_FAILED", RetryHint.UserAction, StageGame);

    public static TrainerErrorMapping WebNotAWebSocketRequest { get; } =
        new(ErrorDomain.Request, "REQ.WEB_NOT_WEBSOCKET", RetryHint.NotRetryable, StageAgent);
}
