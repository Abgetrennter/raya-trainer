using System.Collections.Immutable;
using RayaTrainer.Core.Agent;
using RayaTrainer.Host.Services;

namespace RayaTrainer.App.Services;

/// <summary>
/// Structured outcome of one WPF Product Intent feature submission. The classifier label is the
/// same novice-facing vocabulary the product console and Web surface render; <see cref="Message"/>
/// additionally carries the Agent's result detail when present (执行计数、拒绝原因等).
/// </summary>
internal sealed record ProductFeatureSubmission(
    bool Success,
    string Message,
    ProductControlResultClassification Classification);

/// <summary>
/// Shared submit-and-settle helper for Product Intent feature entries (unified attribute
/// modification stage D). The ContextBinding and parameter shape are derived from the generated
/// <see cref="ProductCatalogProjection"/> exactly like the product console and Web handler, so the
/// WPF grid, the Overlay and the Web surface all submit the same intent shape for the same product.
/// Accepted intents are polled until the layered result settles (or the bounded attempt budget is
/// exhausted); nothing here fabricates a result from UI state.
/// </summary>
internal static class ProductFeatureSubmitter
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResultPollInterval = TimeSpan.FromMilliseconds(50);
    private const int ResultPollAttempts = 5;

    public static async Task<ProductFeatureSubmission> SubmitAsync(
        IProductControlSession session,
        string productId,
        IReadOnlyList<ScriptValue> parameters,
        IReadOnlyList<uint>? capturedObjectIds = null,
        CancellationToken cancellationToken = default)
    {
        if (!session.Negotiation.IsReady)
        {
            return new ProductFeatureSubmission(
                false,
                "产品控制未就绪，请先连接游戏进程。",
                ProductControlResultClassification.Offline);
        }

        if (!ProductCatalogProjection.TryGetPublic(productId, out var entry))
        {
            return new ProductFeatureSubmission(
                false,
                $"产品不在可用目录中：{productId}。",
                ProductControlResultClassification.Rejected);
        }

        // Captured bindings must carry the submit-time selection; the wire codec rejects a
        // Captured intent without ObjectIDs, so fail fast with a novice-facing message.
        var binding = entry.ToContextBinding();
        if (binding.Kind == BindingKind.Captured)
        {
            if (capturedObjectIds is null || capturedObjectIds.Count == 0)
            {
                return new ProductFeatureSubmission(
                    false,
                    "该功能作用于选中目标，请先在游戏里选中单位或建筑。",
                    ProductControlResultClassification.Rejected);
            }

            binding = binding with { Captured = new CapturedTarget(capturedObjectIds.ToImmutableArray()) };
        }

        var request = new SubmitIntentRequest(entry.ProductId, binding, parameters);
        ProductControlOutcome<SubmitIntentResponse> submitOutcome;
        try
        {
            submitOutcome = await session
                .SubmitProductIntentAsync(request, OperationTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new ProductFeatureSubmission(
                false,
                $"下发产品指令失败：{exception.Message}",
                ProductControlResultClassification.Faulted);
        }

        ProductControlOutcome<ProductResult>? resultOutcome = null;
        if (submitOutcome.Value is { Acceptance: ProductAcceptance.Accepted, IntentId.IsInvalid: false } accepted)
        {
            resultOutcome = await QueryResultUntilSettledAsync(session, accepted.IntentId, cancellationToken)
                .ConfigureAwait(false);
        }

        var classification = ProductControlResultClassifier.Classify(submitOutcome, resultOutcome);
        var label = ProductControlResultClassifier.ToLabel(classification);
        var detail = resultOutcome?.Value?.Detail ?? string.Empty;
        if (detail.Length == 0 &&
            submitOutcome.Value is { Acceptance: ProductAcceptance.Rejected } rejected)
        {
            // Admission rejects carry no layered result; surface the wire error code so
            // ProductUnavailable (stale agent catalog) and InvalidRequest stay distinguishable.
            // ErrorCode may be None (agent rejected without a code) — say so explicitly.
            detail = rejected.ErrorCode == ProductErrorCode.None
                ? "拒绝码：无（Agent 未给出错误码）"
                : $"拒绝码：{rejected.ErrorCode}";
        }
        if (detail.Length == 0 && resultOutcome?.Value is { } settled &&
            classification is not (ProductControlResultClassification.Ok
                or ProductControlResultClassification.EffectUnknown))
        {
            // Settled-but-unsuccessful results with an empty Detail: surface the layered states
            // so executor-level rejects (admission accepted, execution refused) stay diagnosable.
            detail = $"准入={settled.Admission}，执行={settled.Execution}，错误码={settled.ErrorCode}";
        }
        if (detail.Length == 0 &&
            (submitOutcome.Status != ProductControlStatus.Ok ||
             resultOutcome is { Status: not ProductControlStatus.Ok }))
        {
            // Transport-level failures carry no layered result at all; the outcome Detail holds
            // the exception message or agent status explanation, which is the only evidence.
            var transport = submitOutcome.Status != ProductControlStatus.Ok
                ? submitOutcome.Detail
                : resultOutcome!.Detail;
            if (!string.IsNullOrWhiteSpace(transport))
            {
                detail = $"传输层细节：{transport}";
            }
        }
        var message = detail.Length == 0
            ? $"产品执行结果：{label}。"
            : $"产品执行结果：{label}。{detail}";

        // Ok = effect observed; EffectUnknown = accepted and executed but the product declares no
        // readback (the modifier route's count evidence still publishes). Both count as success for
        // the one-shot grid entry; Pending means the settle budget ran out before a final state.
        var success = classification is ProductControlResultClassification.Ok
            or ProductControlResultClassification.EffectUnknown;
        return new ProductFeatureSubmission(success, message, classification);
    }

    private static async Task<ProductControlOutcome<ProductResult>?> QueryResultUntilSettledAsync(
        IProductControlSession session,
        IntentId intentId,
        CancellationToken cancellationToken)
    {
        var accepted = ProductControlOutcome<SubmitIntentResponse>.Ok(
            new SubmitIntentResponse(
                ProductControlWireCodec.AgentStatusOk,
                ProductAcceptance.Accepted,
                ProductErrorCode.None,
                intentId));
        ProductControlOutcome<ProductResult>? latest = null;
        for (var attempt = 0; attempt < ResultPollAttempts; ++attempt)
        {
            latest = await session
                .GetProductResultAsync(intentId, OperationTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (ProductControlResultClassifier.Classify(accepted, latest) !=
                ProductControlResultClassification.Pending)
            {
                break;
            }

            if (attempt + 1 < ResultPollAttempts)
            {
                await Task.Delay(ResultPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        return latest;
    }
}
