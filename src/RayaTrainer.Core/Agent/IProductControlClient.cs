namespace RayaTrainer.Core.Agent;

/// <summary>
/// Managed client surface for the Product Control Plane v1 commands (Agent pipe commands
/// 57-61). Each method returns a decoded response record carrying the transport-level
/// <see cref="QueryContextResponse.AgentStatusCode"/> / equivalent; raw bytes never leak.
/// Implementations are responsible for framing the request payload via
/// <see cref="ProductControlWireCodec"/>, dispatching the appropriate Agent command, and
/// decoding the response. Wire integration (command enum entries 57-61 and capability
/// routing) is added by the I3 merge milestone.
/// </summary>
public interface IProductControlClient
{
    /// <summary>Command 57 — on-demand bounded match-context summary.</summary>
    Task<QueryContextResponse> QueryMatchContextAsync(
        int processId,
        QueryContextRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Command 58 — submit one product intent into the Agent control plane.</summary>
    Task<SubmitIntentResponse> SubmitProductIntentAsync(
        int processId,
        SubmitIntentRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Command 59 — read the layered product result for one intent.</summary>
    Task<ProductResult> GetProductResultAsync(
        int processId,
        GetResultRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Command 60 — page over the Agent's current Desired Intent registry.</summary>
    Task<GetDesiredResponse> GetDesiredIntentsAsync(
        int processId,
        GetDesiredRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Command 61 — import a durable product policy revision.</summary>
    Task<ApplyPolicyResponse> ApplyDurableProductPolicyAsync(
        int processId,
        ApplyPolicyRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
