using System.IO;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;

namespace RayaTrainer.Host.Services;

/// <summary>
/// Default <see cref="IProductControlSession"/> implementation. Wraps a single
/// <see cref="IProductControlClient"/> for the currently attached target, gates the plane on
/// the negotiated Agent capability/identity/schema, and imports the durable product policy.
/// Structured outcomes are returned for every expected condition; the transport
/// <see cref="AgentStatusCode"/> of Ok never implies a product effect was applied — that
/// distinction stays in the decoded record's layered states.
/// </summary>
internal sealed class ProductControlSession : IProductControlSession
{
    private readonly Func<IProductControlClient> _clientFactory;
    private IProductControlClient? _client;
    private int _processId;
    private ProductControlNegotiation _negotiation = ProductControlNegotiation.Offline;
    private IntentId _lastSubmittedIntentId;

    public ProductControlSession(Func<IProductControlClient> clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public ProductControlNegotiation Negotiation => _negotiation;

    public IntentId LastSubmittedIntentId => _lastSubmittedIntentId;

    public async Task<ProductControlNegotiation> NegotiateAndImportPolicyAsync(
        int processId,
        AgentStatusPayload status,
        DurableProductPolicy policy,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var granted = status.NativeRuntimeCapabilities;
        var matchContext = (granted & (uint)NativeRuntimeCapabilities.MatchContext) != 0;
        var productPlane = (granted & (uint)NativeRuntimeCapabilities.ProductControlPlane) != 0;

        // Baseline carries the observable negotiation facts (capability bits + the schema
        // this App speaks) so the diagnostics layer can explain a failure regardless of which
        // gate rejected the plane. These are display-only; AttachAsync already enforced identity
        // and required runtime capabilities, so the negotiation goes straight to the Agent pipe.
        var baseline = new ProductControlNegotiation
        {
            GrantedCapabilities = granted,
            MatchContextCapable = matchContext,
            ProductControlPlaneCapable = productPlane,
            ContextSchemaVersion = ProductControlWireCodec.SchemaVersion,
            ProductControlSchemaVersion = ProductControlWireCodec.SchemaVersion,
        };

        var client = _client ??= _clientFactory();
        _processId = processId;

        // The Agent Desired Registry is the session truth; Core only records the
        // Agent-confirmed revision after a successful import.
        try
        {
            var response = await client
                .ApplyDurableProductPolicyAsync(processId, ToApplyPolicyRequest(policy), timeout, cancellationToken)
                .ConfigureAwait(false);
            var mapped = MapApplyPolicyResponse(response);
            if (mapped != ProductControlStatus.Ok)
            {
                return Store(baseline with
                {
                    Status = mapped,
                    CapabilityNegotiated = true,
                    Detail = $"ApplyDurableProductPolicy agentStatus={response.AgentStatusCode}.",
                });
            }

            return Store(baseline with
            {
                Status = ProductControlStatus.Ok,
                CapabilityNegotiated = true,
                PolicyRevision = response.PolicyRevision,
                Detail =
                    $"policyRevision={response.PolicyRevision.Value}; accepted={response.AcceptedCount}; rejected={response.RejectedCount}.",
            });
        }
        catch (Exception ex)
        {
            return Store(baseline with
            {
                Status = Classify(ex),
                CapabilityNegotiated = true,
                Detail = ex.Message,
            });
        }
    }

    public Task<ProductControlOutcome<QueryContextResponse>> QueryMatchContextAsync(
        ScopeMask requestedScopeMask,
        SnapshotGeneration knownSnapshotGeneration,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (client, pid) => client.QueryMatchContextAsync(
                pid,
                new QueryContextRequest(requestedScopeMask, knownSnapshotGeneration),
                timeout,
                cancellationToken),
            response => response.AgentStatusCode);

    public async Task<ProductControlOutcome<SubmitIntentResponse>> SubmitProductIntentAsync(
        SubmitIntentRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = await ExecuteAsync(
            (client, pid) => client.SubmitProductIntentAsync(pid, request, timeout, cancellationToken),
            response => response.AgentStatusCode).ConfigureAwait(false);
        if (outcome.Value is
            {
                Acceptance: ProductAcceptance.Accepted,
                ErrorCode: ProductErrorCode.None,
                IntentId.IsInvalid: false,
            } accepted)
        {
            _lastSubmittedIntentId = accepted.IntentId;
        }
        return outcome;
    }

    public Task<ProductControlOutcome<ProductResult>> GetProductResultAsync(
        IntentId intentId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (client, pid) => client.GetProductResultAsync(
                pid, new GetResultRequest(intentId), timeout, cancellationToken),
            response => response.AgentStatusCode);

    public Task<ProductControlOutcome<GetDesiredResponse>> GetDesiredIntentsAsync(
        uint offset,
        uint limit,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (client, pid) => client.GetDesiredIntentsAsync(
                pid,
                new GetDesiredRequest(offset, limit, _negotiation.PolicyRevision),
                timeout,
                cancellationToken),
            response => response.AgentStatusCode);

    public async Task<ProductControlOutcome<ApplyPolicyResponse>> ApplyDurableProductPolicyAsync(
        DurableProductPolicy policy,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_negotiation.Status != ProductControlStatus.Ok || _client is null)
        {
            return ProductControlOutcome<ApplyPolicyResponse>.Failure(_negotiation.Status, _negotiation.Detail);
        }

        try
        {
            var response = await _client
                .ApplyDurableProductPolicyAsync(_processId, ToApplyPolicyRequest(policy), timeout, cancellationToken)
                .ConfigureAwait(false);
            var mapped = MapApplyPolicyResponse(response);
            if (mapped != ProductControlStatus.Ok)
            {
                return ProductControlOutcome<ApplyPolicyResponse>.Failure(
                    mapped, $"agentStatus={response.AgentStatusCode}.");
            }

            // Record the Agent-confirmed revision so subsequent GetDesired paging carries the
            // correct KnownPolicyRevision.
            _negotiation = _negotiation with { PolicyRevision = response.PolicyRevision };
            return ProductControlOutcome<ApplyPolicyResponse>.Ok(response);
        }
        catch (Exception ex)
        {
            return ProductControlOutcome<ApplyPolicyResponse>.Failure(Classify(ex), ex.Message);
        }
    }

    public void Reset()
    {
        _negotiation = ProductControlNegotiation.Offline;
        _processId = 0;
        _client = null;
        _lastSubmittedIntentId = default;
    }

    private async Task<ProductControlOutcome<T>> ExecuteAsync<T>(
        Func<IProductControlClient, int, Task<T>> operation,
        Func<T, ushort> statusSelector)
        where T : class
    {
        if (_negotiation.Status != ProductControlStatus.Ok || _client is null)
        {
            return ProductControlOutcome<T>.Failure(_negotiation.Status, _negotiation.Detail);
        }

        try
        {
            var response = await operation(_client, _processId).ConfigureAwait(false);
            var mapped = MapAgentStatus(statusSelector(response));
            return mapped == ProductControlStatus.Ok
                ? ProductControlOutcome<T>.Ok(response)
                : ProductControlOutcome<T>.Failure(mapped, $"agentStatus={statusSelector(response)}.");
        }
        catch (Exception ex)
        {
            return ProductControlOutcome<T>.Failure(Classify(ex), ex.Message);
        }
    }

    private ProductControlNegotiation Store(ProductControlNegotiation negotiation)
    {
        _negotiation = negotiation;
        return negotiation;
    }

    private static ApplyPolicyRequest ToApplyPolicyRequest(DurableProductPolicy policy) =>
        new(policy.Revision, policy.Entries);

    private static ProductControlStatus MapAgentStatus(ushort agentStatusCode)
    {
        if (agentStatusCode == ProductControlWireCodec.AgentStatusOk)
        {
            return ProductControlStatus.Ok;
        }

        return (ProductErrorCode)agentStatusCode switch
        {
            ProductErrorCode.SchemaMismatch => ProductControlStatus.SchemaMismatch,
            ProductErrorCode.PolicyStale => ProductControlStatus.StaleRevision,
            ProductErrorCode.CapabilityUnavailable => ProductControlStatus.CapabilityUnavailable,
            _ => ProductControlStatus.Faulted,
        };
    }

    private static ProductControlStatus MapApplyPolicyResponse(ApplyPolicyResponse response)
    {
        var topLevel = MapAgentStatus(response.AgentStatusCode);
        if (topLevel != ProductControlStatus.Ok)
        {
            return topLevel;
        }

        var entryError = response.Results
            .Select(item => item.ErrorCode)
            .FirstOrDefault(code => code != ProductErrorCode.None);
        if (response.RejectedCount == 0 && entryError == ProductErrorCode.None)
        {
            return ProductControlStatus.Ok;
        }

        return entryError switch
        {
            ProductErrorCode.PolicyStale => ProductControlStatus.StaleRevision,
            ProductErrorCode.SchemaMismatch => ProductControlStatus.SchemaMismatch,
            ProductErrorCode.CapabilityUnavailable => ProductControlStatus.CapabilityUnavailable,
            _ => ProductControlStatus.Faulted,
        };
    }

    private static ProductControlStatus Classify(Exception ex) => ex switch
    {
        InvalidDataException data when data.Message.Contains("schema", StringComparison.OrdinalIgnoreCase)
            => ProductControlStatus.SchemaMismatch,
        InvalidDataException => ProductControlStatus.Faulted,
        TimeoutException => ProductControlStatus.TargetOffline,
        IOException => ProductControlStatus.TargetOffline,
        _ => ProductControlStatus.Faulted,
    };
}

/// <summary>
/// Re-reads the live Product Control diagnostics. Attach-time initialization and manual
/// diagnostic refresh share this path so lifecycle, Desired state and the latest layered
/// result cannot drift apart.
/// </summary>
internal static class ProductControlDiagnosticsCollector
{
    public static async Task<ProductControlDiagnostics> CollectAsync(
        IProductControlSession session,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var negotiation = session.Negotiation;
        if (!negotiation.IsReady)
        {
            return ProductControlDiagnosticsBuilder.Build(negotiation, null, null);
        }

        var contextOutcome = await session
            .QueryMatchContextAsync(
                ScopeMask.CurrentPlayer |
                ScopeMask.AllOtherPlayers |
                ScopeMask.AllUnits |
                ScopeMask.SelectionSummary,
                default,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        var desiredOutcome = await session
            .GetDesiredIntentsAsync(
                0,
                ProductControlWireCodec.MaxGetDesiredLimit,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        var latestIntentId = session.LastSubmittedIntentId.Value;
        if (desiredOutcome.Value is { } desired)
        {
            foreach (var item in desired.Items)
            {
                latestIntentId = Math.Max(latestIntentId, item.IntentId.Value);
            }
        }

        ProductResult? result = null;
        if (latestIntentId != 0)
        {
            var resultOutcome = await session
                .GetProductResultAsync(new IntentId(latestIntentId), timeout, cancellationToken)
                .ConfigureAwait(false);
            result = resultOutcome.Value;
        }

        return ProductControlDiagnosticsBuilder.Build(
            negotiation,
            contextOutcome.Value,
            desiredOutcome.Value,
            new IntentId(latestIntentId),
            result);
    }
}

/// <summary>
/// Composes a novice-friendly <see cref="ProductControlDiagnostics"/> from the I4 negotiation
/// result plus the optional QueryMatchContext / GetDesired responses. Emits only the layered
/// public states and a single Chinese "next step" line — never pointers, Route strings or raw
/// bytes (U3 completion criterion).
/// </summary>
internal static class ProductControlDiagnosticsBuilder
{
    public static ProductControlDiagnostics Build(
        ProductControlNegotiation negotiation,
        QueryContextResponse? context,
        GetDesiredResponse? desired,
        IntentId latestIntentId = default,
        ProductResult? latestResult = null)
    {
        ArgumentNullException.ThrowIfNull(negotiation);

        var contextOk = context is not null && context.AgentStatusCode == ProductControlWireCodec.AgentStatusOk;
        var lifecycle = contextOk ? context!.Lifecycle : (MatchLifecycle?)null;

        var pending = 0;
        var active = 0;
        var disabled = 0;
        var superseded = 0;
        if (desired is not null)
        {
            foreach (var item in desired.Items)
            {
                switch (item.DesiredState)
                {
                    case DesiredState.Pending:
                        pending++;
                        break;
                    case DesiredState.Active:
                        active++;
                        break;
                    case DesiredState.Disabled:
                        disabled++;
                        break;
                    case DesiredState.Superseded:
                        superseded++;
                        break;
                }
            }
        }

        // Prefer the live registry revision from GetDesired; fall back to the Agent-confirmed
        // import revision.
        var policyRevision = desired?.PolicyRevision.Value ?? negotiation.PolicyRevision.Value;

        return new ProductControlDiagnostics
        {
            CapabilityNegotiated = negotiation.CapabilityNegotiated,
            GrantedCapabilities = negotiation.GrantedCapabilities,
            MatchContextCapable = negotiation.MatchContextCapable,
            ProductControlPlaneCapable = negotiation.ProductControlPlaneCapable,
            ContextSchemaVersion = negotiation.ContextSchemaVersion,
            ProductControlSchemaVersion = negotiation.ProductControlSchemaVersion,

            MatchLifecycle = lifecycle?.ToString() ?? "Unknown",
            SnapshotGeneration = contextOk ? context!.SnapshotGeneration.Value : 0,
            ActivePlayerCount = contextOk ? context!.ActivePlayerCount : 0,

            DesiredTotalCount = desired is not null ? checked((int)desired.TotalCount) : 0,
            DesiredPendingCount = pending,
            DesiredActiveCount = active,
            DesiredDisabledCount = disabled,
            DesiredSupersededCount = superseded,
            PolicyRevision = policyRevision,

            LastSubmittedIntentId = latestIntentId.Value,
            LastResultProductId = latestResult is { Availability: ResultAvailability.Present }
                ? latestResult.ProductId?.Value ?? ""
                : "",
            LastAdmissionState = latestResult is { Availability: ResultAvailability.Present }
                ? latestResult.Admission.ToString()
                : "",
            LastExecutionState = latestResult is { Availability: ResultAvailability.Present }
                ? latestResult.Execution.ToString()
                : "",
            LastEffectState = latestResult is { Availability: ResultAvailability.Present }
                ? latestResult.Effect.ToString()
                : "",
            LastCompensationState = latestResult is { Availability: ResultAvailability.Present }
                ? latestResult.Compensation.ToString()
                : "",
            LastErrorCode = latestResult is
                {
                    Availability: ResultAvailability.Present,
                    ErrorCode: not ProductErrorCode.None,
                }
                ? latestResult.ErrorCode.ToString()
                : "",

            StatusSummary = BuildStatusSummary(negotiation.Status, lifecycle),
        };
    }

    private static string BuildStatusSummary(
        ProductControlStatus status,
        MatchLifecycle? lifecycle)
    {
        switch (status)
        {
            case ProductControlStatus.TargetOffline:
                return "尚未连接游戏进程，连接后才能使用产品控制。";
            case ProductControlStatus.CapabilityUnavailable:
                return "当前 Agent 未提供产品控制能力，相关按钮不可用；请更新到匹配的 Agent 版本后重新连接。";
            case ProductControlStatus.SchemaMismatch:
                return "产品控制协议版本不一致，相关功能已停用；请同时更新修改器与 Agent。";
            case ProductControlStatus.StaleRevision:
                return "本地产品策略版本落后于游戏内记录，已跳过导入；请重新同步产品策略。";
            case ProductControlStatus.Faulted:
                return "产品控制初始化失败，请重新连接；若反复失败请导出诊断包给开发者。";
        }

        // Negotiation succeeded — explain readiness from the live match lifecycle so a novice
        // knows whether to act now or wait for the next match.
        return lifecycle switch
        {
            null => "产品控制已就绪，正在读取对局状态……",
            MatchLifecycle.Ready =>
                "对局已就绪，可以下发产品指令。",
            MatchLifecycle.Loading or MatchLifecycle.Resolving =>
                "对局正在加载，产品指令会在对局就绪后自动可用，请稍候。",
            _ => "尚未进入对局，进入遭遇战后即可下发产品指令。",
        };
    }
}
