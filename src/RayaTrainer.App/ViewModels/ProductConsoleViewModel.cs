using System.Collections.ObjectModel;
using System.Globalization;
using RayaTrainer.App.Services;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// Additive WPF management surface for the Product Control Plane (U2). It consumes the live
/// I4 <see cref="IProductControlSession"/> for the currently attached target via the internal
/// <see cref="IProductControlSessionHost"/> seam and renders a MORE detailed view than the
/// Overlay would: the attach-time negotiation (capability / schema / fingerprint) via the U3
/// structured diagnostics, the Agent Desired Intent registry, and the layered Product Result.
///
/// The important invariant is that although this surface shows more detail, its result
/// <see cref="ProductControlResultClassification"/> uses the managed frozen vocabulary.
/// WPF/Web share <see cref="ProductControlResultClassifier"/> directly; Native Overlay mirrors
/// the aligned result-state subset because it cannot reference managed code. Runtime state is
/// READ from the Agent Desired/Result; nothing is fabricated from UI booleans. The product list
/// and each intent's <see cref="ContextBinding"/>/parameters are DERIVED from the generated
/// <see cref="ProductCatalogProjection"/> — never hardcoded to <c>(Live, CurrentPlayer, None)</c>.
/// </summary>
public sealed class ProductConsoleViewModel : ViewModelBase
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResultPollInterval = TimeSpan.FromMilliseconds(50);
    private const uint DesiredPageLimit = ProductControlWireCodec.MaxGetDesiredLimit;
    private const int ResultPollAttempts = 5;

    private readonly IProductControlSessionHost? _host;
    private readonly Action<string> _publishStatus;
    private readonly Action<DurableProductPolicy> _persistDurablePolicy;
    private DurableProductPolicy _durablePolicy;

    // Catalog-driven inputs. The selected ProductId keys into ProductCatalogProjection, whose
    // declared definition supplies the ContextBinding and parameter descriptors. Fixture-only
    // codegen definitions are deliberately excluded from the user-facing catalog.
    private string _productIdText = "player.money.give";
    private string _amountText = "1";
    private ProductCatalogEntry? _selectedProduct;

    private string _statusText = "连接游戏后点击刷新，查看产品控制协商结果。";
    private string _negotiationSummary = "尚未采集产品控制状态。";
    private string _capabilityDetails = "-";
    private string _schemaDetails = "-";
    private string _lifecycleDetails = "-";
    private string _epochDetails = "-";
    private string _desiredSummary = "-";
    private string _resultDetails = "尚未下发产品指令。";
    private string _resultClassificationLabel = "-";
    private ProductControlResultClassification _lastResultClassification = ProductControlResultClassification.Pending;
    private bool _isBusy;

    // The host seam is internal, so this constructor is internal too (the class stays public
    // for WPF data binding). MainViewModel and the U2 tests construct it within the assembly.
    internal ProductConsoleViewModel(
        IProductControlSessionHost? host,
        Action<string> publishStatus,
        DurableProductPolicy? durablePolicy = null,
        Action<DurableProductPolicy>? persistDurablePolicy = null)
    {
        _host = host;
        _publishStatus = publishStatus;
        _durablePolicy = durablePolicy ?? DurableProductPolicy.Empty;
        _persistDurablePolicy = persistDurablePolicy ?? (_ => { });
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsBusy);
        SubmitCommand = new RelayCommand(() => _ = SubmitAsync(), () => !IsBusy);
        ApplyDurablePolicyCommand = new RelayCommand(() => _ = ApplyDurablePolicyAsync(), () => !IsBusy);
    }

    public ObservableCollection<DesiredIntentRowViewModel> DesiredIntents { get; } = [];

    /// <summary>Every real product from the generated catalog, projected onto the submit shape.</summary>
    public IReadOnlyList<ProductCatalogEntry> Products { get; } = ProductCatalogProjection.PublicEntries;

    /// <summary>
    /// The product picked from <see cref="Products"/>. Selecting one drives <see cref="ProductIdText"/>
    /// so both the catalog picker and manual entry resolve through the same catalog key.
    /// </summary>
    public ProductCatalogEntry? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            _selectedProduct = value;
            OnPropertyChanged();
            if (value is not null)
            {
                ProductIdText = value.ProductId.Value;
            }
        }
    }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand SubmitCommand { get; }

    public RelayCommand ApplyDurablePolicyCommand { get; }

    public string ProductIdText
    {
        get => _productIdText;
        set
        {
            _productIdText = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string AmountText
    {
        get => _amountText;
        set
        {
            _amountText = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string NegotiationSummary
    {
        get => _negotiationSummary;
        private set
        {
            _negotiationSummary = value;
            OnPropertyChanged();
        }
    }

    public string CapabilityDetails
    {
        get => _capabilityDetails;
        private set
        {
            _capabilityDetails = value;
            OnPropertyChanged();
        }
    }

    public string SchemaDetails
    {
        get => _schemaDetails;
        private set
        {
            _schemaDetails = value;
            OnPropertyChanged();
        }
    }

    public string LifecycleDetails
    {
        get => _lifecycleDetails;
        private set
        {
            _lifecycleDetails = value;
            OnPropertyChanged();
        }
    }

    public string EpochDetails
    {
        get => _epochDetails;
        private set
        {
            _epochDetails = value;
            OnPropertyChanged();
        }
    }

    public string DesiredSummary
    {
        get => _desiredSummary;
        private set
        {
            _desiredSummary = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The full layered Product Result — the WPF surface's "more detailed" view.</summary>
    public string ResultDetails
    {
        get => _resultDetails;
        private set
        {
            _resultDetails = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The short shared classification label (identical to the Overlay's).</summary>
    public string ResultClassificationLabel
    {
        get => _resultClassificationLabel;
        private set
        {
            _resultClassificationLabel = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The canonical shared classification of the last submitted intent. Exposed to
    /// same-assembly consumers/tests so they can assert semantic parity with the Overlay.
    /// </summary>
    internal ProductControlResultClassification LastResultClassification => _lastResultClassification;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }
            _isBusy = value;
            OnPropertyChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            SubmitCommand.RaiseCanExecuteChanged();
            ApplyDurablePolicyCommand.RaiseCanExecuteChanged();
        }
    }

    private IProductControlSession? Session => _host?.ProductControl;

    internal async Task RefreshAsync()
    {
        var session = Session;
        if (session is null)
        {
            StatusText = "请先连接游戏，再刷新产品控制状态。";
            ResetDiagnosticsDisplay();
            return;
        }

        IsBusy = true;
        try
        {
            var negotiation = session.Negotiation;

            var contextOutcome = await session
                .QueryMatchContextAsync(
                    ScopeMask.CurrentPlayer |
                    ScopeMask.AllOtherPlayers |
                    ScopeMask.AllUnits |
                    ScopeMask.SelectionSummary,
                    default,
                    OperationTimeout);
            var desiredOutcome = await session
                .GetDesiredIntentsAsync(0, DesiredPageLimit, OperationTimeout);

            // Reuse the U3 structured builder so the WPF surface and the diagnostics page share
            // exactly the same projection — no self-combined booleans + reason strings here.
            var diagnostics = ProductControlDiagnosticsBuilder.Build(
                negotiation, contextOutcome.Value, desiredOutcome.Value);
            ApplyDiagnosticsDisplay(diagnostics);

            DesiredIntents.Clear();
            if (desiredOutcome.Value is { } desired)
            {
                foreach (var item in desired.Items)
                {
                    DesiredIntents.Add(new DesiredIntentRowViewModel(item));
                }
            }

            var latestIntentId = session.LastSubmittedIntentId;
            if (desiredOutcome.Value is { } desiredResult)
            {
                foreach (var item in desiredResult.Items)
                {
                    if (item.IntentId.Value > latestIntentId.Value)
                    {
                        latestIntentId = item.IntentId;
                    }
                }
            }

            if (!latestIntentId.IsInvalid)
            {
                var latestResult = await session
                    .GetProductResultAsync(latestIntentId, OperationTimeout);
                ApplyResultDisplay(
                    ProductControlOutcome<SubmitIntentResponse>.Ok(
                        new SubmitIntentResponse(
                            ProductControlWireCodec.AgentStatusOk,
                            ProductAcceptance.Accepted,
                            ProductErrorCode.None,
                            latestIntentId)),
                    latestResult);
            }

            StatusText = negotiation.IsReady
                ? $"已刷新：目标意图 {DesiredIntents.Count} 项。"
                : $"产品控制未就绪（{ProductControlResultClassifier.ToLabel(ProductControlResultClassifier.FromStatus(negotiation.Status))}）。";
            _publishStatus(StatusText);
        }
        catch (Exception exception)
        {
            StatusText = $"刷新产品控制状态失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task SubmitAsync()
    {
        var session = Session;
        if (session is null)
        {
            StatusText = "请先连接游戏，再下发产品指令。";
            return;
        }

        if (!TryResolveProduct(out var entry, out var error) ||
            !TryBuildParameters(entry, out var parameters, out error))
        {
            StatusText = error;
            return;
        }

        IsBusy = true;
        try
        {
            // ContextBinding (Binding/Scope/Reapply) and parameters come from the product's
            // DECLARED catalog definition, not a hardcoded (Live, CurrentPlayer, None).
            var request = new SubmitIntentRequest(
                entry.ProductId,
                entry.ToContextBinding(),
                parameters);

            var submitOutcome = await session
                .SubmitProductIntentAsync(request, OperationTimeout);

            ProductControlOutcome<ProductResult>? resultOutcome = null;
            if (submitOutcome.Value is { Acceptance: ProductAcceptance.Accepted, IntentId.IsInvalid: false } accepted)
            {
                resultOutcome = await QueryResultUntilSettledAsync(session, accepted.IntentId);
            }

            ApplyResultDisplay(submitOutcome, resultOutcome);
            StatusText = $"产品指令分类：{ResultClassificationLabel}。";
            _publishStatus(StatusText);
        }
        catch (Exception exception)
        {
            StatusText = $"下发产品指令失败：{exception.Message}";
            ResultDetails = StatusText;
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task ApplyDurablePolicyAsync()
    {
        var session = Session;
        if (session is null)
        {
            StatusText = "请先连接游戏，再写入产品策略。";
            return;
        }

        if (!TryResolveProduct(out var catalogEntry, out var error) ||
            !TryBuildParameters(catalogEntry, out var parameters, out error))
        {
            StatusText = error;
            return;
        }

        if (catalogEntry.Binding != BindingKind.Rebindable ||
            catalogEntry.Reapply != ReapplyPolicy.OnReadyOnce ||
            !DurableProductPolicy.IsSymbolicScope(catalogEntry.Scope))
        {
            StatusText = $"产品“{catalogEntry.DisplayName}”不支持持久策略。";
            return;
        }

        IsBusy = true;
        try
        {
            // ApplyDurableProductPolicy is a full replacement. Merge into the persisted F6
            // policy, retain every unrelated entry and monotonically advance its revision.
            var existing = _durablePolicy.Entries.FirstOrDefault(entry =>
                entry.ProductId == catalogEntry.ProductId &&
                entry.ScopeKind == catalogEntry.Scope);
            var policyEntryId = existing?.PolicyEntryId ?? NextPolicyEntryId(_durablePolicy);
            var policy = _durablePolicy.AddOrUpdate(new DurablePolicyEntry(
                PolicyEntryId: policyEntryId,
                catalogEntry.ProductId,
                catalogEntry.Scope,
                catalogEntry.Reapply,
                parameters));

            var outcome = await session
                .ApplyDurableProductPolicyAsync(policy, OperationTimeout);

            if (outcome.IsOk)
            {
                _durablePolicy = policy.WithRevision(outcome.Value!.PolicyRevision);
                _persistDurablePolicy(_durablePolicy);
                StatusText = $"已写入产品策略（版本 {_durablePolicy.Revision.Value}）。";
            }
            else
            {
                StatusText =
                    $"写入产品策略失败：{ProductControlResultClassifier.ToLabel(ProductControlResultClassifier.FromStatus(outcome.Status))}。";
            }
            _publishStatus(StatusText);
        }
        catch (Exception exception)
        {
            StatusText = $"写入产品策略失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryResolveProduct(out ProductCatalogEntry entry, out string error)
    {
        entry = null!;
        error = string.Empty;
        var id = ProductIdText.Trim();
        if (id.Length == 0)
        {
            error = "请先选择要下发的产品。";
            return false;
        }

        if (!ProductCatalogProjection.TryGetPublic(id, out var resolved))
        {
            error = $"产品不在目录中：{id}。";
            return false;
        }

        entry = resolved;
        return true;
    }

    // Fail closed: a product with a typed parameter must have valid text before we submit; a
    // zero-parameter product ignores the amount box and submits an empty parameter list.
    private bool TryBuildParameters(
        ProductCatalogEntry entry,
        out IReadOnlyList<ScriptValue> parameters,
        out string error)
    {
        error = string.Empty;
        var values = new List<ScriptValue>(entry.Parameters.Count);
        foreach (var descriptor in entry.Parameters)
        {
            switch (descriptor.Kind)
            {
                case ScriptValueKind.Integer:
                    if (!long.TryParse(
                            AmountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                    {
                        parameters = [];
                        error = $"参数“{descriptor.Name}”无效：请输入整数。";
                        return false;
                    }
                    values.Add(ScriptValue.Integer(amount));
                    break;
                default:
                    parameters = [];
                    error = $"暂不支持的参数类型：{descriptor.Kind}（{descriptor.Name}）。";
                    return false;
            }
        }

        parameters = values;
        return true;
    }

    private void ApplyResultDisplay(
        ProductControlOutcome<SubmitIntentResponse> submitOutcome,
        ProductControlOutcome<ProductResult>? resultOutcome)
    {
        var classification = ProductControlResultClassifier.Classify(submitOutcome, resultOutcome);
        _lastResultClassification = classification;
        ResultClassificationLabel = ProductControlResultClassifier.ToLabel(classification);
        OnPropertyChanged(nameof(LastResultClassification));
        ResultDetails = BuildResultDetails(submitOutcome, resultOutcome, classification);
    }

    private static async Task<ProductControlOutcome<ProductResult>?> QueryResultUntilSettledAsync(
        IProductControlSession session,
        IntentId intentId)
    {
        var accepted = ProductControlOutcome<SubmitIntentResponse>.Ok(
            new SubmitIntentResponse(
                ProductControlWireCodec.AgentStatusOk,
                ProductAcceptance.Accepted,
                ProductErrorCode.None,
                intentId));
        ProductControlOutcome<ProductResult>? latest = null;
        for (var attempt = 0; attempt < ResultPollAttempts; attempt++)
        {
            latest = await session.GetProductResultAsync(intentId, OperationTimeout);
            if (ProductControlResultClassifier.Classify(accepted, latest) !=
                ProductControlResultClassification.Pending)
            {
                break;
            }

            if (attempt + 1 < ResultPollAttempts)
            {
                await Task.Delay(ResultPollInterval);
            }
        }

        return latest;
    }

    private static ulong NextPolicyEntryId(DurableProductPolicy policy)
    {
        var max = policy.Entries.Count == 0
            ? 0UL
            : policy.Entries.Max(entry => entry.PolicyEntryId);
        if (max == ulong.MaxValue)
        {
            throw new InvalidOperationException("产品策略条目 ID 已耗尽。");
        }
        return max + 1;
    }

    private void ApplyDiagnosticsDisplay(ProductControlDiagnostics diagnostics)
    {
        NegotiationSummary = diagnostics.StatusSummary.Length > 0
            ? diagnostics.StatusSummary
            : "尚未采集产品控制状态。";

        var negotiated = diagnostics.CapabilityNegotiated ? "已协商" : "未协商";
        var match = diagnostics.MatchContextCapable ? "已授权" : "未授权";
        var plane = diagnostics.ProductControlPlaneCapable ? "已授权" : "未授权";
        CapabilityDetails =
            $"能力协商：{negotiated} · 对局上下文：{match} · 产品控制面：{plane}（0x{diagnostics.GrantedCapabilities:X}）";

        SchemaDetails =
            $"上下文 Schema v{diagnostics.ContextSchemaVersion} · 产品控制 Schema v{diagnostics.ProductControlSchemaVersion}";

        var single = diagnostics.SinglePlayerProven ? "已确认单人对局" : "未确认单人对局";
        var activePlayers = diagnostics.ActivePlayerCount == 0
            ? "未知"
            : diagnostics.ActivePlayerCount.ToString(CultureInfo.InvariantCulture);
        LifecycleDetails =
            $"对局阶段：{diagnostics.MatchLifecycle} · 在场玩家：{activePlayers} · {single}";

        EpochDetails = $"地图纪元：{diagnostics.MapEpoch} · 快照代次：{diagnostics.SnapshotGeneration}";

        DesiredSummary =
            $"目标意图：共 {diagnostics.DesiredTotalCount} · 待生效 {diagnostics.DesiredPendingCount}" +
            $" · 生效中 {diagnostics.DesiredActiveCount} · 已停用 {diagnostics.DesiredDisabledCount}" +
            $" · 被取代 {diagnostics.DesiredSupersededCount} · 策略版本 {diagnostics.PolicyRevision}";
    }

    private void ResetDiagnosticsDisplay()
    {
        NegotiationSummary = "尚未采集产品控制状态。";
        CapabilityDetails = "-";
        SchemaDetails = "-";
        LifecycleDetails = "-";
        EpochDetails = "-";
        DesiredSummary = "-";
        DesiredIntents.Clear();
    }

    private static string BuildResultDetails(
        ProductControlOutcome<SubmitIntentResponse> submitOutcome,
        ProductControlOutcome<ProductResult>? resultOutcome,
        ProductControlResultClassification classification)
    {
        var lines = new List<string>
        {
            $"分类：{ProductControlResultClassifier.ToLabel(classification)}（{classification}）",
        };

        if (submitOutcome.Value is { } submit)
        {
            lines.Add($"提交：准入 {submit.Acceptance} · 意图 #{submit.IntentId.Value} · 错误 {submit.ErrorCode}");
        }
        else
        {
            lines.Add($"提交：{submitOutcome.Status} - {submitOutcome.Detail}");
        }

        if (resultOutcome?.Value is { } result)
        {
            // The WPF surface exposes the whole layered model; the Overlay would show only the
            // classification label above.
            lines.Add(
                $"结果：可用性 {result.Availability} · 准入 {result.Admission} · 执行 {result.Execution}" +
                $" · 效果 {result.Effect} · 补偿 {result.Compensation}");
            lines.Add($"错误码：{result.ErrorCode} · 地图纪元 {result.MapEpoch.Value} · 证据 {result.Evidence.Count} 项");
            if (result.Detail.Length > 0)
            {
                lines.Add($"详情：{result.Detail}");
            }
        }
        else if (resultOutcome is { } failed)
        {
            lines.Add($"结果：{failed.Status} - {failed.Detail}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>Read-only projection of one Agent Desired Intent registry row.</summary>
public sealed class DesiredIntentRowViewModel
{
    public DesiredIntentRowViewModel(DesiredIntentSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        IntentId = summary.IntentId.Value;
        ProductId = summary.ProductId.Value;
        Binding = summary.BindingKind.ToString();
        Scope = summary.ScopeKind.ToString();
        Reapply = summary.ReapplyPolicy.ToString();
        DesiredState = summary.DesiredState.ToString();
        LastMapEpoch = summary.LastMapEpoch.Value;
    }

    public ulong IntentId { get; }

    public string ProductId { get; }

    public string Binding { get; }

    public string Scope { get; }

    public string Reapply { get; }

    public string DesiredState { get; }

    public ulong LastMapEpoch { get; }
}
