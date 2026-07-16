using RayaTrainer.App.Services;
using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Hashing;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Tests;

/// <summary>
/// Shared test doubles used across multiple test files to reduce duplication.
/// </summary>
internal static class SharedTestDoubles
{
    /// <summary>
    /// Creates a minimal manifest with optional hooks. Used by TrainerSessionTests and AgentPatchPayloadTests.
    /// </summary>
    public static TrainerManifest MinimalManifest(params PatchHook[] hooks)
    {
        return new TrainerManifest(
            "ra3_1.12.game",
            [],
            new PatchManifest(hooks),
            []);
    }

    /// <summary>
    /// Loads a MainViewModel with default settings. Used by multiple MainViewModel* test files.
    /// </summary>
    public static MainViewModel LoadDefaultViewModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "settings.json");
        return MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(settingsPath));
    }
}

/// <summary>
/// Fake ITrainerSessionService that records attach calls and supports configurable behavior.
/// Used by MainViewModelTargetSelectionTests, MainViewModelVersionSelectionTests, and others.
/// </summary>
internal sealed class FakeTrainerSessionService : ITrainerSessionService
{
    private readonly ITrainerFeatureController? _controllerOnInstall;

    public FakeTrainerSessionService(ITrainerFeatureController? controllerOnInstall = null)
        => _controllerOnInstall = controllerOnInstall;

    public int AttachCount { get; private set; }
    public ITrainerFeatureController? FeatureController { get; private set; }
    public bool ArePatchesInstalled { get; private set; }
    public int? TargetProcessId { get; private set; }
    public bool CanUseFeatures => TargetProcessId is not null;
    public int InstalledHookCount => 0;
    public string RemoteSymbolSummary => "";
    public AttachResult AttachTarget(TrainerManifest manifest, TrainerTarget target)
    {
        AttachCount++;
        TargetProcessId = target.ProcessId;
        return new AttachResult(true, $"attached {target.ProcessId}");
    }

    public SessionInstallOutcome InstallPatches(TrainerManifest manifest, string diagnosticsDir)
    {
        ArePatchesInstalled = true;
        if (_controllerOnInstall is not null)
        {
            FeatureController = _controllerOnInstall;
        }
        return new SessionInstallOutcome(new PatchMismatchReportResult(PatchInstallResult.Empty, null), "installed");
    }

    public void ResetPatchesState()
    {
        TargetProcessId = null;
        FeatureController = null;
        ArePatchesInstalled = false;
    }

    public void MarkTargetOffline() => ResetPatchesState();

    public bool IsTargetGameForeground() => false;
    public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature) =>
        TrainerFeatureCapabilityEvaluator.Evaluate(
            feature,
            new TrainerFeatureCapabilityContext(
                TargetProcessId is not null || (ArePatchesInstalled && FeatureController is not null),
                CanUseFeatures || FeatureController is not null,
                ArePatchesInstalled,
                true,
                FeatureController is IAgentFeatureController { SupportsDirectGameApi: true }));
    public void Dispose() { }
}

/// <summary>
/// Minimal ITrainerFeatureController stub that only tracks toggle state.
/// Used by MainViewModelTargetSelectionTests for toggle regression tests.
/// </summary>
internal sealed class StubFeatureController : ITrainerFeatureController
{
    private readonly bool _toggleState;

    public StubFeatureController(bool toggleState) => _toggleState = toggleState;

    public void SetToggle(TrainerFeature feature, bool enabled) { }
    public void TriggerAction(TrainerFeature feature) { }
    public void TriggerAction(TrainerFeature feature, ReinforcementSettings? reinforcementSettings) { }
    public Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
        TrainerFeature feature,
        ReinforcementSettings? reinforcementSettings = null,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        Action? onDispatched = null,
        CancellationToken cancellationToken = default,
        TimeSpan? pausedGracePeriod = null,
        Action<DispatchWaitStatus>? onWaitStatusChanged = null)
    {
        onDispatched?.Invoke();
        return Task.FromResult(ActionDispatchResult.Consumed);
    }

    public void WriteReinforcementSettings(ReinforcementSettings settings) { }
    public void WriteResourceValues(ResourceValueSettings settings) { }
    public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings settings) { }
    public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings settings) { }
    public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings settings) { }
    public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult() =>
        new(0, 0, 0, SecretProtocolBindingItemStatus.NotRun, 0, SecretProtocolBindingItemStatus.NotRun, SecretProtocolBindingProbeStatus.NotRun);
    public void PulseAutoRepair() { }
    public void ClearAutoRepairPulse() { }
    public void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f) { }
    public uint ReadSelectedUnitCode() => 0;
        public SelectedUnitUpgradesSnapshot ReadSelectedUnitUpgrades() => SelectedUnitUpgradesSnapshot.Empty;
        public GameApiDispatchStatus GrantObjectUpgradeOnSelectedSameType(uint upgradeHash, TimeSpan? timeout = null) => GameApiDispatchStatus.Disabled;
    public byte ReadActionDispatch() => 0;
    public uint ReadGameThreadTick() => 1;
    public int ReadGameMode() => 0;
    public void Reset(TrainerFeature feature) { }
    public bool ReadToggleState(TrainerFeature feature) => _toggleState;
}

/// <summary>
/// ITrainerFeatureController that records toggle/action/resource writes for assertion.
/// Used by MainViewModelHelpTextTests and can be reused by other ViewModel tests.
/// </summary>
internal sealed class ResourceWriteFeatureController : ITrainerFeatureController
{
    public ActionDispatchResult DispatchResult { get; init; } = ActionDispatchResult.Consumed;

    public ResourceValueSettings? LastResourceValues { get; private set; }
    public TrainerFeature? LastActionFeature { get; private set; }
    public ReinforcementSettings? LastReinforcementSettings { get; private set; }
    public TrainerFeature? LastToggleFeature { get; private set; }
    public bool? LastToggleEnabled { get; private set; }
    public int ActionDispatchCount { get; private set; }
    public SecretProtocolGrantSettings? LastSecretProtocolSettings { get; private set; }
    public List<ReinforcementSettings> ReinforcementWrites { get; } = [];

    public void SetToggle(TrainerFeature feature, bool enabled)
    {
        LastToggleFeature = feature;
        LastToggleEnabled = enabled;
    }

    public void TriggerAction(TrainerFeature feature) => throw new NotImplementedException();
    public void TriggerAction(TrainerFeature feature, ReinforcementSettings? reinforcementSettings) => throw new NotImplementedException();

    public Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
        TrainerFeature feature,
        ReinforcementSettings? reinforcementSettings = null,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        Action? onDispatched = null,
        CancellationToken cancellationToken = default,
        TimeSpan? pausedGracePeriod = null,
        Action<DispatchWaitStatus>? onWaitStatusChanged = null)
    {
        ActionDispatchCount++;
        LastActionFeature = feature;
        LastReinforcementSettings = reinforcementSettings;
        if (reinforcementSettings is not null)
        {
            ReinforcementWrites.Add(reinforcementSettings);
        }
        onDispatched?.Invoke();
        return Task.FromResult(DispatchResult);
    }

    public void WriteReinforcementSettings(ReinforcementSettings settings) => throw new NotImplementedException();
    public void WriteResourceValues(ResourceValueSettings settings) { LastResourceValues = settings; }
    public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings settings) { LastSecretProtocolSettings = settings; }
    public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings settings) => throw new NotImplementedException();
    public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings settings) => throw new NotImplementedException();
    public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult() => throw new NotImplementedException();
    public void PulseAutoRepair() => throw new NotImplementedException();
    public void ClearAutoRepairPulse() => throw new NotImplementedException();
    public void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f) => throw new NotImplementedException();
    public uint ReadSelectedUnitCode() => throw new NotImplementedException();
        public SelectedUnitUpgradesSnapshot ReadSelectedUnitUpgrades() => SelectedUnitUpgradesSnapshot.Empty;
        public GameApiDispatchStatus GrantObjectUpgradeOnSelectedSameType(uint upgradeHash, TimeSpan? timeout = null) => GameApiDispatchStatus.Disabled;
    public byte ReadActionDispatch() => throw new NotImplementedException();
    public uint ReadGameThreadTick() => throw new NotImplementedException();
    public int ReadGameMode() => throw new NotImplementedException();
    public void Reset(TrainerFeature feature) => throw new NotImplementedException();
    public bool ReadToggleState(TrainerFeature feature) => false;
}

/// <summary>
/// Helper to set private fields on objects via reflection. Used by MainViewModelHelpTextTests.
/// </summary>
internal static class ReflectionHelper
{
    public static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field is null) throw new ArgumentException($"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    /// <summary>
    /// Creates a connected TrainerSessionManager with a feature controller set via reflection.
    /// </summary>
    public static TrainerSessionManager ConnectedSessionManager(ITrainerFeatureController controller)
    {
        var sessionManager = new TrainerSessionManager();
        SetPrivateField(sessionManager, "_featureController", controller);
        SetPrivateField(sessionManager, "_arePatchesInstalled", true);
        return sessionManager;
    }
}
