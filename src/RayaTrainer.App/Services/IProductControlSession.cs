using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;

namespace RayaTrainer.App.Services;

/// <summary>
/// Structured outcome status for a Product Control Plane operation. The session never
/// throws for these expected conditions and never reassembles them into a
/// <c>StatusMessage</c>; callers branch on the status and surface it through
/// <see cref="RayaTrainer.Core.Diagnostics.ProductControlDiagnostics"/> instead.
/// </summary>
internal enum ProductControlStatus
{
    /// <summary>Transport + product status both Ok; <c>Value</c> carries the decoded record.</summary>
    Ok,

    /// <summary>The target pipe could not be reached (game exited / not attached).</summary>
    TargetOffline,

    /// <summary>The Agent did not advertise MatchContext (0x80) + ProductControlPlane (0x100).</summary>
    CapabilityUnavailable,

    /// <summary>The Agent build fingerprint does not match the one this App speaks.</summary>
    FingerprintMismatch,

    /// <summary>Context / product-control schema version disagreement.</summary>
    SchemaMismatch,

    /// <summary>The imported durable policy revision is stale relative to the Agent registry.</summary>
    StaleRevision,

    /// <summary>An unexpected fault (malformed payload, internal error) occurred.</summary>
    Faulted,
}

/// <summary>
/// Result of the attach-time capability/schema negotiation and durable policy import. Every
/// field is populated even on failure (the capability bits are read from the Agent status
/// before the gate decision), so the diagnostics layer can always explain what happened.
/// </summary>
internal sealed record ProductControlNegotiation
{
    public ProductControlStatus Status { get; init; } = ProductControlStatus.TargetOffline;
    public bool CapabilityNegotiated { get; init; }
    public uint GrantedCapabilities { get; init; }
    public bool MatchContextCapable { get; init; }
    public bool ProductControlPlaneCapable { get; init; }
    public int ContextSchemaVersion { get; init; }
    public int ProductControlSchemaVersion { get; init; }

    /// <summary>The Agent-confirmed durable policy revision after a successful import.</summary>
    public PolicyRevision PolicyRevision { get; init; }

    /// <summary>Technical detail (never surfaced verbatim to novices).</summary>
    public string Detail { get; init; } = "";

    public bool IsReady => Status == ProductControlStatus.Ok;

    /// <summary>Negotiation result before any target is attached.</summary>
    public static ProductControlNegotiation Offline { get; } = new()
    {
        Status = ProductControlStatus.TargetOffline,
        Detail = "尚未连接目标进程。",
    };
}

/// <summary>
/// Structured result for a single Product Control operation. <typeparamref name="T"/> is a
/// decoded response record; on failure <see cref="Value"/> is <c>null</c> and the caller
/// inspects <see cref="Status"/>.
/// </summary>
internal sealed record ProductControlOutcome<T>
    where T : class
{
    private ProductControlOutcome(ProductControlStatus status, T? value, string detail)
    {
        Status = status;
        Value = value;
        Detail = detail;
    }

    public ProductControlStatus Status { get; }

    public T? Value { get; }

    public string Detail { get; }

    public bool IsOk => Status == ProductControlStatus.Ok && Value is not null;

    public static ProductControlOutcome<T> Ok(T value) =>
        new(ProductControlStatus.Ok, value, "");

    public static ProductControlOutcome<T> Failure(ProductControlStatus status, string detail) =>
        new(status, null, detail);
}

/// <summary>
/// Single managed service wrapping the five Product Control Plane v1 operations for the
/// currently attached target (see the I4 task card). Owns attach-time capability/schema
/// negotiation and durable policy import; the Agent's Desired Registry is the session truth
/// while Core only persists the Agent-confirmed <see cref="PolicyRevision"/>. All operations
/// return structured outcomes rather than throwing for expected conditions.
/// </summary>
internal interface IProductControlSession
{
    /// <summary>The latest negotiation result. <see cref="ProductControlNegotiation.Offline"/> until negotiated.</summary>
    ProductControlNegotiation Negotiation { get; }

    /// <summary>Latest accepted direct-submit IntentId in this attached session.</summary>
    IntentId LastSubmittedIntentId { get; }

    /// <summary>
    /// Verifies capability (MatchContext + ProductControlPlane) and identity/schema against
    /// the negotiated Agent status, then imports the durable product policy and records the
    /// Agent-confirmed revision. Capability/fingerprint/schema gates short-circuit without
    /// touching the pipe. Never throws for expected conditions.
    /// </summary>
    Task<ProductControlNegotiation> NegotiateAndImportPolicyAsync(
        int processId,
        AgentStatusPayload status,
        DurableProductPolicy policy,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<ProductControlOutcome<QueryContextResponse>> QueryMatchContextAsync(
        ScopeMask requestedScopeMask,
        SnapshotGeneration knownSnapshotGeneration,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<ProductControlOutcome<SubmitIntentResponse>> SubmitProductIntentAsync(
        SubmitIntentRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<ProductControlOutcome<ProductResult>> GetProductResultAsync(
        IntentId intentId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<ProductControlOutcome<GetDesiredResponse>> GetDesiredIntentsAsync(
        uint offset,
        uint limit,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<ProductControlOutcome<ApplyPolicyResponse>> ApplyDurableProductPolicyAsync(
        DurableProductPolicy policy,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Clears negotiation state when the target detaches.</summary>
    void Reset();
}
