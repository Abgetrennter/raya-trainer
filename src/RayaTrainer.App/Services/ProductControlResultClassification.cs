using RayaTrainer.Core.Agent;

namespace RayaTrainer.App.Services;

/// <summary>
/// Canonical, layered result classification for a Product Intent submission. This is the
/// single semantic vocabulary the whole app agrees on: the Overlay (U1) renders it as a short
/// label while the WPF management surface (U2) renders the full layered detail, but both must
/// resolve the SAME classification for the same session outcome. The classification is derived
/// only from the structured <see cref="ProductControlStatus"/> / <see cref="ProductErrorCode"/>
/// / layered <see cref="ProductResult"/> — never from UI booleans and never from a reassembled
/// status string.
/// </summary>
internal enum ProductControlResultClassification
{
    /// <summary>Accepted, executed and its effect was observed (or does not apply).</summary>
    Ok,

    /// <summary>Target pipe unreachable (game exited / not attached).</summary>
    Offline,

    /// <summary>The Agent never advertised MatchContext + ProductControlPlane.</summary>
    CapabilityUnavailable,

    /// <summary>The Agent build fingerprint disagrees with the one this App speaks.</summary>
    FingerprintMismatch,

    /// <summary>Context / product-control schema version disagreement.</summary>
    SchemaMismatch,

    /// <summary>The durable policy revision is stale relative to the Agent registry.</summary>
    PolicyStale,

    /// <summary>No usable match context (not in a match / lifecycle not Ready).</summary>
    ContextUnavailable,

    /// <summary>The current match is not a proven single-player match.</summary>
    NotSinglePlayer,

    /// <summary>The Agent intent queue is full; retry after the next tick.</summary>
    QueueFull,

    /// <summary>A newer intent superseded this one before it took effect.</summary>
    Superseded,

    /// <summary>Accepted and dispatched, but the effect could not be confirmed.</summary>
    EffectUnknown,

    /// <summary>Accepted but still pending / result not yet observable.</summary>
    Pending,

    /// <summary>The requested result has left the bounded retention window.</summary>
    ResultExpired,

    /// <summary>The Agent has never observed the requested IntentId.</summary>
    UnknownIntent,

    /// <summary>The execution layer reported a failure.</summary>
    ExecutionFailed,

    /// <summary>Execution completed, but read-back proved that the effect was not applied.</summary>
    EffectNotObserved,

    /// <summary>The explicit compensation layer failed.</summary>
    CompensationFailed,

    /// <summary>Rejected for another product-defined reason.</summary>
    Rejected,

    /// <summary>An unexpected fault (malformed payload / internal error).</summary>
    Faulted,
}

/// <summary>
/// Maps the structured submit + result outcomes onto the single shared
/// <see cref="ProductControlResultClassification"/> vocabulary. Precedence follows the layered
/// model: the submit transport/negotiation gate first, then admission acceptance / error code,
/// then the layered <see cref="ProductResult"/> (Superseded, EffectUnknown, error). Accepted,
/// dispatched and EffectUnknown never collapse into <see cref="ProductControlResultClassification.Ok"/>.
/// </summary>
internal static class ProductControlResultClassifier
{
    public static ProductControlResultClassification Classify(
        ProductControlOutcome<SubmitIntentResponse> submit,
        ProductControlOutcome<ProductResult>? result)
    {
        ArgumentNullException.ThrowIfNull(submit);

        // 1. Transport / negotiation gate on the submit itself.
        if (submit.Status != ProductControlStatus.Ok || submit.Value is not { } response)
        {
            return FromStatus(submit.Status);
        }

        // 2. Admission decision: a rejection (or any product error code) is authoritative.
        if (response.Acceptance == ProductAcceptance.Rejected ||
            response.ErrorCode != ProductErrorCode.None)
        {
            return FromErrorCode(response.ErrorCode);
        }

        // 3. Accepted: the layered result decides. No result yet means still pending.
        if (result is null)
        {
            return ProductControlResultClassification.Pending;
        }

        if (result.Status != ProductControlStatus.Ok || result.Value is not { } layered)
        {
            return FromStatus(result.Status);
        }

        return FromResult(layered);
    }

    public static ProductControlResultClassification FromStatus(ProductControlStatus status) => status switch
    {
        ProductControlStatus.Ok => ProductControlResultClassification.Ok,
        ProductControlStatus.TargetOffline => ProductControlResultClassification.Offline,
        ProductControlStatus.CapabilityUnavailable => ProductControlResultClassification.CapabilityUnavailable,
        ProductControlStatus.FingerprintMismatch => ProductControlResultClassification.FingerprintMismatch,
        ProductControlStatus.SchemaMismatch => ProductControlResultClassification.SchemaMismatch,
        ProductControlStatus.StaleRevision => ProductControlResultClassification.PolicyStale,
        _ => ProductControlResultClassification.Faulted,
    };

    private static ProductControlResultClassification FromResult(ProductResult result)
    {
        switch (result.Availability)
        {
            case ResultAvailability.Expired:
                return ProductControlResultClassification.ResultExpired;
            case ResultAvailability.UnknownIntent:
                return ProductControlResultClassification.UnknownIntent;
            case not ResultAvailability.Present:
                return ProductControlResultClassification.Faulted;
        }

        // CompensationFailed is more specific than the accompanying ExecutionFault.
        // Check the layered state first so a failed restore is never flattened to
        // the generic "execution failed" label.
        if (result.Compensation == CompensationState.Failed)
        {
            return ProductControlResultClassification.CompensationFailed;
        }

        if (result.ErrorCode != ProductErrorCode.None)
        {
            return FromErrorCode(result.ErrorCode);
        }

        if (result.Admission == AdmissionState.Superseded)
        {
            return ProductControlResultClassification.Superseded;
        }

        if (result.Admission == AdmissionState.Expired)
        {
            return ProductControlResultClassification.ResultExpired;
        }

        if (result.Admission == AdmissionState.Rejected)
        {
            return ProductControlResultClassification.Rejected;
        }

        if (result.Admission == AdmissionState.Pending ||
            result.Execution is ExecutionState.NotStarted or ExecutionState.Running ||
            result.Compensation == CompensationState.Pending)
        {
            return ProductControlResultClassification.Pending;
        }

        if (result.Execution == ExecutionState.Failed)
        {
            return ProductControlResultClassification.ExecutionFailed;
        }

        return result.Effect switch
        {
            EffectState.Unknown => ProductControlResultClassification.EffectUnknown,
            EffectState.NotObserved => ProductControlResultClassification.EffectNotObserved,
            EffectState.Observed => ProductControlResultClassification.Ok,
            EffectState.NotApplicable
                when result.Admission == AdmissionState.Accepted &&
                     result.Execution == ExecutionState.Executed
                => ProductControlResultClassification.Ok,
            _ => ProductControlResultClassification.Faulted,
        };
    }

    private static ProductControlResultClassification FromErrorCode(ProductErrorCode code) => code switch
    {
        ProductErrorCode.None => ProductControlResultClassification.Ok,
        ProductErrorCode.ContextUnavailable => ProductControlResultClassification.ContextUnavailable,
        ProductErrorCode.NotSinglePlayer => ProductControlResultClassification.NotSinglePlayer,
        ProductErrorCode.QueueFull => ProductControlResultClassification.QueueFull,
        ProductErrorCode.ResultExpired => ProductControlResultClassification.ResultExpired,
        ProductErrorCode.Superseded => ProductControlResultClassification.Superseded,
        ProductErrorCode.PolicyStale => ProductControlResultClassification.PolicyStale,
        ProductErrorCode.SchemaMismatch => ProductControlResultClassification.SchemaMismatch,
        ProductErrorCode.CapabilityUnavailable => ProductControlResultClassification.CapabilityUnavailable,
        ProductErrorCode.ExecutionFault => ProductControlResultClassification.ExecutionFailed,
        _ => ProductControlResultClassification.Rejected,
    };

    /// <summary>
    /// Short, novice-friendly Chinese label. The Overlay would show only this; the WPF surface
    /// pairs it with the full layered detail. Both share this exact vocabulary.
    /// </summary>
    public static string ToLabel(ProductControlResultClassification classification) => classification switch
    {
        ProductControlResultClassification.Ok => "已生效",
        ProductControlResultClassification.Offline => "未连接目标",
        ProductControlResultClassification.CapabilityUnavailable => "能力不可用",
        ProductControlResultClassification.FingerprintMismatch => "版本不一致",
        ProductControlResultClassification.SchemaMismatch => "协议版本不一致",
        ProductControlResultClassification.PolicyStale => "策略版本落后",
        ProductControlResultClassification.ContextUnavailable => "对局上下文不可用",
        ProductControlResultClassification.NotSinglePlayer => "非单人对局",
        ProductControlResultClassification.QueueFull => "队列已满",
        ProductControlResultClassification.Superseded => "已被取代",
        ProductControlResultClassification.EffectUnknown => "效果待确认",
        ProductControlResultClassification.EffectNotObserved => "效果未生效",
        ProductControlResultClassification.ExecutionFailed => "执行失败",
        ProductControlResultClassification.CompensationFailed => "恢复失败",
        ProductControlResultClassification.Pending => "等待结果",
        ProductControlResultClassification.ResultExpired => "结果已过期",
        ProductControlResultClassification.UnknownIntent => "找不到该指令",
        ProductControlResultClassification.Rejected => "已被拒绝",
        _ => "初始化失败",
    };
}
