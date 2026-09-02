namespace RayaTrainer.Core.Agent;

/// <summary>
/// Product Control Plane v1 client surface (Agent commands 57-61). Each method frames its
/// request with <see cref="ProductControlWireCodec"/>, dispatches the capability-gated
/// command over the same pipe transport as the other Agent commands, and decodes the
/// response body. Raw bytes never leak past this layer; callers consume the decoded records.
/// The transport does not interpret the Product Control body — a transport-level
/// <see cref="AgentStatusCode"/> of Ok does not imply the product feature was applied; that
/// distinction lives in the decoded record (acceptance / admission / execution / effect).
/// </summary>
public sealed partial class AgentNamedPipeClient : IProductControlClient
{
    public Task<QueryContextResponse> QueryMatchContextAsync(
        int processId,
        QueryContextRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.QueryMatchContext,
            ProductControlWireCodec.EncodeQueryContextRequest(request),
            timeout,
            payload => ProductControlWireCodec.DecodeQueryContextResponse(payload.Span),
            cancellationToken);
    }

    public Task<SubmitIntentResponse> SubmitProductIntentAsync(
        int processId,
        SubmitIntentRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.SubmitProductIntent,
            ProductControlWireCodec.EncodeSubmitIntentRequest(request),
            timeout,
            payload => ProductControlWireCodec.DecodeSubmitIntentResponse(payload.Span),
            cancellationToken);
    }

    public Task<ProductResult> GetProductResultAsync(
        int processId,
        GetResultRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.GetProductResult,
            ProductControlWireCodec.EncodeGetResultRequest(request),
            timeout,
            payload => ProductControlWireCodec.DecodeProductResult(payload.Span),
            cancellationToken);
    }

    public Task<GetDesiredResponse> GetDesiredIntentsAsync(
        int processId,
        GetDesiredRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.GetDesiredIntents,
            ProductControlWireCodec.EncodeGetDesiredRequest(request),
            timeout,
            payload => ProductControlWireCodec.DecodeGetDesiredResponse(payload.Span),
            cancellationToken);
    }

    public Task<ApplyPolicyResponse> ApplyDurableProductPolicyAsync(
        int processId,
        ApplyPolicyRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.ApplyDurableProductPolicy,
            ProductControlWireCodec.EncodeApplyPolicyRequest(request),
            timeout,
            payload => ProductControlWireCodec.DecodeApplyPolicyResponse(payload.Span),
            cancellationToken);
    }
}
