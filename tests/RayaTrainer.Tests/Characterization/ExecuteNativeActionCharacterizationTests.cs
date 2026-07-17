using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests.Characterization;

/// <summary>
/// Golden-master characterization tests for <see cref="AgentFeatureController.ExecuteNativeAction"/>.
/// Pins which IAgentClient method gets called (and with what arguments) for every RawName
/// branch in the main switch statement, preserving the typo-ridden RawNames as-is.
/// </summary>
[Trait("Category", "Characterization")]
public sealed class ExecuteNativeActionCharacterizationTests
{
    private static readonly AgentStatusPayload AgentStatus = new(
        AgentStatusCode.Ok, AgentProtocol.Version, ProcessId: 1234,
        ModuleBase: 0x400000, InstalledHookCount: 24);

    /// <summary>
    /// Creates a controller wired to a <see cref="CapturingAgentClient"/> that records
    /// every IAgentClient call for later assertion.
    /// </summary>
    private static (AgentFeatureController Controller, CapturingAgentClient Client) CreateFixture()
    {
        var client = new CapturingAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus,
            supportsDirectGameApi: true);
        return (controller, client);
    }

    private static TrainerFeature Feature(string rawName) => new TrainerFeature(
        rawName, rawName, null, [], null, null);

    #region ExecuteNativeAction main switch — GameApi methods

    [Fact]
    public void SelectUnitLevelUP_CallsLevelUpSelected()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Select Unit Level UP"));

        Assert.Equal(nameof(IAgentGameApiClient.LevelUpSelectedAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastLevelUpRequest);
    }

    [Fact]
    public void SelectUnitSuperSpeed_CallsSetSelectedUnitSpeedMode1()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Select Unit Super Speed"));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitSpeedAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastSpeedRequest);
        Assert.Equal(1u, client.LastSpeedRequest!.Mode);
    }

    [Fact]
    public void SelectUnitSlowSpeed_CallsSetSelectedUnitSpeedMode2()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Select Unit Slow Speed"));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitSpeedAsync), client.LastCalledMethod);
        Assert.Equal(2u, client.LastSpeedRequest!.Mode);
    }

    [Fact]
    public void SelectUnitFreeze_CallsSetSelectedUnitSpeedMode3()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Select Unit Freeze"));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitSpeedAsync), client.LastCalledMethod);
        Assert.Equal(3u, client.LastSpeedRequest!.Mode);
    }

    [Fact]
    public void RestoreSelectUnitSpeed_CallsSetSelectedUnitSpeedMode4()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Restore Select Unit Speed"));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitSpeedAsync), client.LastCalledMethod);
        Assert.Equal(4u, client.LastSpeedRequest!.Mode);
    }

    [Fact]
    public void SelectUnitChangeID_CallsCaptureSelectedUnits()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Select Unit Change ID"));

        Assert.Equal(nameof(IAgentGameApiClient.CaptureSelectedUnitsAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastCaptureRequest);
    }

    [Fact]
    public void DestorySelectUnit_CallsKillUnit()
    {
        // Typo preserved exactly as in production code
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Destory Select Unit"));

        Assert.Equal(nameof(IAgentGameApiClient.KillUnitAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastKillRequest);
    }

    [Fact]
    public void GetMeBase_CallsGetMeBase()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature(TrainerFeatureIds.GetBase));

        Assert.Equal(nameof(IAgentGameApiClient.GetMeBaseAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastGetMeBaseRequest);
    }

    [Fact]
    public void WeNeedBack_CallsWeNeedBackWithReinforcementSettings()
    {
        var (controller, client) = CreateFixture();
        controller.WriteReinforcementSettings(new ReinforcementSettings(0x12345678, 5, 3));
        controller.TriggerAction(Feature(TrainerFeatureIds.Reinforcement));

        Assert.Equal(nameof(IAgentGameApiClient.WeNeedBackAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastWeNeedBackRequest);
        Assert.Equal(0x12345678u, client.LastWeNeedBackRequest!.UnitTypeId);
        Assert.Equal(5u, client.LastWeNeedBackRequest.Count);
        Assert.Equal(3u, client.LastWeNeedBackRequest.Rank);
    }

    [Fact]
    public void SelectUnitCopyForMe_CallsCopyForMe()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature(TrainerFeatureIds.CopySelectedUnit));

        Assert.Equal(nameof(IAgentGameApiClient.CopyForMeAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastCopyForMeRequest);
    }

    [Fact]
    public void SetUnitSupportState_CallsSetUnitState()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Set Unit Support State"));

        Assert.Equal(nameof(IAgentGameApiClient.SetUnitStateAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastSetUnitStateRequest);
    }

    [Fact]
    public void SecretProtocolBindingProbe_CallsSecretProtocolBindingProbe()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature(TrainerFeatureIds.SecretProtocolBindingProbe));

        Assert.Equal(nameof(IAgentGameApiClient.SecretProtocolBindingProbeAsync), client.LastCalledMethod);
    }

    [Fact]
    public void SovietOrbitalRefuseRank1Probe_CallsGrantPlayerTechWithFixedHash()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Soviet Orbital Refuse Rank 1 Probe"));

        Assert.Equal(nameof(IAgentGameApiClient.GrantPlayerTechAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastGrantPlayerTechRequest);
        Assert.Equal(0x3A7E2F69u, client.LastGrantPlayerTechRequest!.TechHash);
    }

    [Fact]
    public void GrantSecretProtocol_CallsGrantSecretProtocolWithConfiguredIds()
    {
        var (controller, client) = CreateFixture();
        controller.WriteSecretProtocolGrantSettings(
            new SecretProtocolGrantSettings(PlayerTechId: 0xAABB, UpgradeId: 0xCCDD));
        controller.TriggerAction(Feature(TrainerFeatureIds.GrantSecretProtocol));

        Assert.Equal(nameof(IAgentGameApiClient.GrantSecretProtocolAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastGrantSecretProtocolRequest);
        Assert.Equal(0xAABBu, client.LastGrantSecretProtocolRequest!.TechHash);
        Assert.Equal(0xCCDDu, client.LastGrantSecretProtocolRequest.UpgradeHash);
    }

    [Fact]
    public void GrantSelectedObjectUpgrade_CallsGrantSelectedUpgrade()
    {
        var (controller, client) = CreateFixture();
        controller.WriteSecretProtocolGrantSettings(
            new SecretProtocolGrantSettings(PlayerTechId: 0, UpgradeId: 0xDEAD));
        controller.TriggerAction(Feature(TrainerFeatureIds.GrantSelectedObjectUpgrade));

        Assert.Equal(nameof(IAgentGameApiClient.GrantSelectedUpgradeAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastGrantSelectedUpgradeRequest);
        Assert.Equal(0xDEADu, client.LastGrantSelectedUpgradeRequest!.UpgradeHash);
    }

    [Fact]
    public void ClearPlayerTechLocks_CallsClearPlayerTechLocks()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Clear Player Tech Locks"));

        Assert.Equal(nameof(IAgentGameApiClient.ClearPlayerTechLocksAsync), client.LastCalledMethod);
    }

    [Fact]
    public void ReplaceTemplateModel_CallsReplaceTemplateModel()
    {
        var (controller, client) = CreateFixture();
        controller.WriteTemplateModelReplacementSettings(
            new TemplateModelReplacementSettings(0x1111, 0x2222));
        controller.TriggerAction(Feature(TrainerFeatureIds.ReplaceTemplateModel));

        Assert.Equal(nameof(IAgentGameApiClient.ReplaceTemplateModelAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastReplaceTemplateModelRequest);
        Assert.Equal(0x1111u, client.LastReplaceTemplateModelRequest!.TargetHash);
        Assert.Equal(0x2222u, client.LastReplaceTemplateModelRequest.DonorHash);
    }

    [Fact]
    public void ReplaceTemplateWeapon_CallsReplaceTemplateWeapon()
    {
        var (controller, client) = CreateFixture();
        controller.WriteTemplateWeaponReplacementSettings(
            new TemplateWeaponReplacementSettings(0x3333, 0x4444));
        controller.TriggerAction(Feature(TrainerFeatureIds.ReplaceTemplateWeapon));

        Assert.Equal(nameof(IAgentGameApiClient.ReplaceTemplateWeaponAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastReplaceTemplateWeaponRequest);
        Assert.Equal(0x3333u, client.LastReplaceTemplateWeaponRequest!.TargetHash);
        Assert.Equal(0x4444u, client.LastReplaceTemplateWeaponRequest.DonorHash);
    }

    [Fact]
    public void SetSelectedUnitTargetHealth_WithHealthValue_CallsSetSelectedUnitHealth()
    {
        var (controller, client) = CreateFixture();
        controller.WriteTargetHealthValue(500f, 1000f);
        controller.TriggerAction(Feature(TrainerFeatureIds.SetSelectedUnitTargetHealth));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitHealthAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastHealthRequest);
        Assert.Equal(1u, client.LastHealthRequest!.Mode); // HealthModeExplicit
        Assert.Equal(500f, client.LastHealthRequest.Health);
        Assert.Equal(1000f, client.LastHealthRequest.MaxHealth);
    }

    [Fact]
    public void SetSelectedUnitTargetHealth_WithoutHealthValue_Throws()
    {
        var (controller, _) = CreateFixture();
        // No target health written → _targetHealth == 0

        var ex = Assert.Throws<InvalidOperationException>(() =>
            controller.TriggerAction(Feature(TrainerFeatureIds.SetSelectedUnitTargetHealth)));

        Assert.Contains("目标生命值", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FillSelectedUnitAmmo_CallsSetSelectedUnitAmmoWithMaxValue()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Fill Selected Unit Ammo"));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitAmmoAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastAmmoRequest);
        Assert.Equal(0x7FFFFFFFu, client.LastAmmoRequest!.Ammo);
    }

    [Fact]
    public void ResetSelectedUnitAmmo_CallsSetSelectedUnitAmmoWithOne()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Reset Selected Unit Ammo"));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitAmmoAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastAmmoRequest);
        Assert.Equal(1u, client.LastAmmoRequest!.Ammo);
    }

    [Fact]
    public void ToggleSelectedUnitAttackSpeed_CallsToggleSelectedAttackSpeed()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Toggle Selected Unit Attack Speed"));

        Assert.Equal(nameof(IAgentGameApiClient.ToggleSelectedAttackSpeedAsync), client.LastCalledMethod);
    }

    [Fact]
    public void ToggleSelectedUnitAttackRange_CallsToggleSelectedAttackRange()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Toggle Selected Unit Attack Range"));

        Assert.Equal(nameof(IAgentGameApiClient.ToggleSelectedAttackRangeAsync), client.LastCalledMethod);
    }

    [Fact]
    public void ClearSelectedAttackSpeedEffects_CallsClearSelectedAttackSpeedEffects()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature(TrainerFeatureIds.ClearSelectedAttackSpeedEffects));

        Assert.Equal(nameof(IAgentGameApiClient.ClearSelectedAttackSpeedEffectsAsync), client.LastCalledMethod);
    }

    [Fact]
    public void ClearSelectedAttackRangeEffects_CallsClearSelectedAttackRangeEffects()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature(TrainerFeatureIds.ClearSelectedAttackRangeEffects));

        Assert.Equal(nameof(IAgentGameApiClient.ClearSelectedAttackRangeEffectsAsync), client.LastCalledMethod);
    }

    #endregion

    #region Health mode branches (pre-switch)

    [Fact]
    public void SelectUnitHpMax_CallsSetSelectedUnitHealthMode2()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Select Unit HP MAX"));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitHealthAsync), client.LastCalledMethod);
        Assert.Equal(2u, client.LastHealthRequest!.Mode); // HealthModeMax
    }

    [Fact]
    public void SelectUnitHpMin_CallsSetSelectedUnitHealthMode3()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Select Unit HP MIN"));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitHealthAsync), client.LastCalledMethod);
        Assert.Equal(3u, client.LastHealthRequest!.Mode); // HealthModeMin
    }

    [Fact]
    public void RestoreSelectUnitNormalHp_CallsSetSelectedUnitHealthMode4()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Restore Select Unit Normal HP"));

        Assert.Equal(nameof(IAgentGameApiClient.SetSelectedUnitHealthAsync), client.LastCalledMethod);
        Assert.Equal(4u, client.LastHealthRequest!.Mode); // HealthModeRestore
    }

    #endregion

    #region Production queue branches (pre-switch)

    [Fact]
    public void ExpandProductionQueue_CallsExpandProductionQueue()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature(TrainerFeatureIds.ExpandProductionQueue));

        Assert.Equal(nameof(IAgentGameApiClient.ExpandProductionQueueAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastExpandRequest);
        Assert.Equal(999u, client.LastExpandRequest!.MaxQueueEntries);
    }

    [Fact]
    public void RestoreProductionQueue_CallsExpandProductionQueueWithDefault()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature(TrainerFeatureIds.RestoreProductionQueue));

        Assert.Equal(nameof(IAgentGameApiClient.ExpandProductionQueueAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastExpandRequest);
        Assert.Equal(1u, client.LastExpandRequest!.MaxQueueEntries);
    }

    #endregion

    #region Teleport branch (pre-switch)

    [Fact]
    public void TeleportSelectedUnitsToMouse_CallsTeleport()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature(TrainerFeatureIds.TeleportSelectedUnitsToMouse));

        Assert.Equal(nameof(IAgentGameApiClient.TeleportSelectedUnitsToMouseAsync), client.LastCalledMethod);
    }

    #endregion

    #region ExecuteLegacyPulse branches

    [Fact]
    public void Money_CallsSetFeatureStatesWithMoneyPulse()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature(TrainerFeatureIds.Money)); // "Money"

        Assert.Equal(nameof(IAgentClient.SetFeatureStatesAsync), client.LastCalledMethod);
        Assert.NotNull(client.LastSetFeatureStatesRequest);
        Assert.Contains(client.LastSetFeatureStatesRequest!.Value.States,
            s => s.StateId == (uint)NativeFeatureStateId.MoneyPulse && s.Value == 1);
    }

    [Fact]
    public void ChallengeMoney_CallsSetFeatureStatesWithChallengeMoneyPulse()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Challenge Money"));

        Assert.Equal(nameof(IAgentClient.SetFeatureStatesAsync), client.LastCalledMethod);
        Assert.Contains(client.LastSetFeatureStatesRequest!.Value.States,
            s => s.StateId == (uint)NativeFeatureStateId.ChallengeMoneyPulse);
    }

    [Fact]
    public void DangerLevelMAX_CallsSetFeatureStatesWithDangerLevelMode1()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Danger Level MAX"));

        Assert.Equal(nameof(IAgentClient.SetFeatureStatesAsync), client.LastCalledMethod);
        Assert.True(client.LastSetFeatureStatesRequest.HasValue);
        Assert.Contains(client.LastSetFeatureStatesRequest.Value.States,
            s => s.StateId == (uint)NativeFeatureStateId.DangerLevelMode && s.Value == 1);
    }

    [Fact]
    public void DangerLevelMIN_CallsSetFeatureStatesWithDangerLevelMode2()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Danger Level MIN"));

        Assert.Equal(nameof(IAgentClient.SetFeatureStatesAsync), client.LastCalledMethod);
        Assert.True(client.LastSetFeatureStatesRequest.HasValue);
        Assert.Contains(client.LastSetFeatureStatesRequest.Value.States,
            s => s.StateId == (uint)NativeFeatureStateId.DangerLevelMode && s.Value == 2);
    }

    [Fact]
    public void RestoreDangerLevelNormal_CallsSetFeatureStatesWithDangerLevelMode0()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Restore Danger Level Normal"));

        Assert.Equal(nameof(IAgentClient.SetFeatureStatesAsync), client.LastCalledMethod);
        Assert.True(client.LastSetFeatureStatesRequest.HasValue);
        Assert.Contains(client.LastSetFeatureStatesRequest.Value.States,
            s => s.StateId == (uint)NativeFeatureStateId.DangerLevelMode && s.Value == 0);
    }

    [Fact]
    public void RestoreSelectOreMine_CallsSetFeatureStatesWithRestoreOrePulse()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Restore Select Ore Mine"));

        Assert.Equal(nameof(IAgentClient.SetFeatureStatesAsync), client.LastCalledMethod);
        Assert.True(client.LastSetFeatureStatesRequest.HasValue);
        Assert.Contains(client.LastSetFeatureStatesRequest.Value.States,
            s => s.StateId == (uint)NativeFeatureStateId.RestoreOrePulse && s.Value == 1);
    }

    [Fact]
    public void FreeBuild_CallsSetFeatureStatesWithFreeBuild()
    {
        var (controller, client) = CreateFixture();
        controller.TriggerAction(Feature("Free Build"));

        Assert.Equal(nameof(IAgentClient.SetFeatureStatesAsync), client.LastCalledMethod);
        Assert.True(client.LastSetFeatureStatesRequest.HasValue);
        Assert.Contains(client.LastSetFeatureStatesRequest.Value.States,
            s => s.StateId == (uint)NativeFeatureStateId.FreeBuild && s.Value == 1);
    }

    [Fact]
    public void FallbackPulseFeature_ForUnrecognizedRawName_Throws()
    {
        // Features that match the DispatchTarget=null && EnableFlags>0 fallback in the
        // main switch but whose RawName is not in ExecuteLegacyPulse's inner switch
        // throw InvalidOperationException (current pre-P1 behavior).
        var feature = new TrainerFeature(
            "FAST BUILD", "FAST BUILD", null, ["FAST BUILD"], null, null);
        var (controller, _) = CreateFixture();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            controller.TriggerAction(feature));

        Assert.Contains("Native pulse 路由", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region Unmapped feature

    [Fact]
    public void UnmappedFeature_ThrowsInvalidOperation()
    {
        var (controller, _) = CreateFixture();
        var feature = Feature("Unknown Feature That Has No Route");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            controller.TriggerAction(feature));

        Assert.Contains("尚未配置 Native action 路由", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region TriggerActionAndWaitForConsumptionAsync

    [Fact]
    public async Task TriggerActionAndWaitForConsumptionAsync_DispatchesCorrectMethod()
    {
        var (controller, client) = CreateFixture();
        controller.WriteReinforcementSettings(new ReinforcementSettings(0x1234, 2, 1));

        var result = await controller.TriggerActionAndWaitForConsumptionAsync(
            Feature(TrainerFeatureIds.Reinforcement));

        Assert.Equal(ActionDispatchResult.Consumed, result);
        Assert.Equal(nameof(IAgentGameApiClient.WeNeedBackAsync), client.LastCalledMethod);
    }

    #endregion

    /// <summary>
    /// Comprehensive fake IAgentClient that captures every call made through
    /// the interface. Returns valid OK payloads so AgentFeatureController
    /// synchronous wrappers do not throw.
    /// </summary>
    internal sealed class CapturingAgentClient : IAgentClient
    {
        public string? LastCalledMethod { get; private set; }

        // Captured requests
        public AgentGameApiLevelUpSelectedRequest? LastLevelUpRequest { get; private set; }
        public AgentGameApiSetSelectedUnitSpeedRequest? LastSpeedRequest { get; private set; }
        public AgentGameApiCaptureSelectedUnitsRequest? LastCaptureRequest { get; private set; }
        public AgentGameApiKillUnitRequest? LastKillRequest { get; private set; }
        public AgentGameApiGetMeBaseRequest? LastGetMeBaseRequest { get; private set; }
        public AgentGameApiWeNeedBackRequest? LastWeNeedBackRequest { get; private set; }
        public AgentGameApiCopyForMeRequest? LastCopyForMeRequest { get; private set; }
        public AgentGameApiSetUnitStateRequest? LastSetUnitStateRequest { get; private set; }
        public AgentGameApiGrantPlayerTechRequest? LastGrantPlayerTechRequest { get; private set; }
        public AgentGameApiGrantSecretProtocolRequest? LastGrantSecretProtocolRequest { get; private set; }
        public AgentGameApiGrantSelectedUpgradeRequest? LastGrantSelectedUpgradeRequest { get; private set; }
        public AgentGameApiReplaceTemplateModelRequest? LastReplaceTemplateModelRequest { get; private set; }
        public AgentGameApiReplaceTemplateWeaponRequest? LastReplaceTemplateWeaponRequest { get; private set; }
        public AgentGameApiSetSelectedUnitHealthRequest? LastHealthRequest { get; private set; }
        public AgentGameApiSetSelectedUnitAmmoRequest? LastAmmoRequest { get; private set; }
        public AgentGameApiExpandProductionQueueRequest? LastExpandRequest { get; private set; }
        public AgentGameApiTeleportSelectedUnitsToMouseRequest? LastTeleportRequest { get; private set; }
        public SetFeatureStatesRequest? LastSetFeatureStatesRequest { get; private set; }

        public AgentInstallPatchesRequest? InstallRequest { get; private set; }
        public bool RestoreCalled { get; private set; }

        // IAgentClient base methods
        public Task<AgentPingPayload> PingAsync(int processId, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(PingAsync);
            return Task.FromResult(new AgentPingPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, processId,
                0x400000, (uint)NativeRuntimeCapabilities.Required,
                AgentBuildIdentity.Fingerprint));
        }

        public Task<AgentStatusPayload> GetStatusAsync(int processId, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GetStatusAsync);
            return Task.FromResult(new AgentStatusPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, processId,
                0x400000, 24, (uint)NativeRuntimeCapabilities.Required));
        }

        public Task<AgentCommandResultPayload> InstallPatchesAsync(int processId,
            AgentInstallPatchesRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(InstallPatchesAsync);
            InstallRequest = request;
            return Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                checked((uint)request.Hooks.Count)));
        }

        public Task<AgentCommandResultPayload> RestorePatchesAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(RestorePatchesAsync);
            RestoreCalled = true;
            return Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<AgentCommandResultPayload> SetFeatureStatesAsync(int processId,
            SetFeatureStatesRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SetFeatureStatesAsync);
            LastSetFeatureStatesRequest = request;
            return Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 24));
        }

        public Task<AgentCommandResultPayload> SetRuntimePatchSetAsync(int processId,
            uint patchSetId, bool enable, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SetRuntimePatchSetAsync);
            return Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<FeatureStatesResponse> GetFeatureStatesAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GetFeatureStatesAsync);
            return Task.FromResult(new FeatureStatesResponse(
                AgentStatusCode.Ok, AgentProtocol.Version, Array.Empty<FeatureStateEntry>()));
        }

        public Task<AgentMemoryReadPayload> ReadMemoryAsync(int processId,
            AgentMemoryReadRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ReadMemoryAsync);
            return Task.FromResult(new AgentMemoryReadPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, request.Address,
                new byte[request.ByteCount]));
        }

        public Task<AgentCommandResultPayload> SetNativeCatalogAsync(int processId,
            IReadOnlyList<uint> rvas, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SetNativeCatalogAsync);
            return Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GetMismatchDiagnosticsAsync);
            return Task.FromResult(new AgentMismatchDiagnosticsPayload(
                AgentStatusCode.InvalidCommand, AgentProtocol.Version, 0,
                [], [], [], MismatchKind.Hook, 0));
        }

        public Task<AgentSignatureScanPayload> ScanSignaturesAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ScanSignaturesAsync);
            return Task.FromResult(new AgentSignatureScanPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0, 0,
                new Dictionary<string, uint>()));
        }

        public Task<AgentGameModePayload> GetGameModeAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GetGameModeAsync);
            return Task.FromResult(new AgentGameModePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameRuntimeConstants.GameModeShell));
        }

        // GameApi methods
        public Task<AgentGameApiGetThingClassPayload> SmokeGetThingClassAsync(
            int processId, AgentGameApiGetThingClassRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SmokeGetThingClassAsync);
            return Task.FromResult(new AgentGameApiGetThingClassPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0x12345678, 0x0D7DF7E0,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiSelectedUnitSnapshotPayload> ReadSelectedUnitSnapshotViaGameApiAsync(
            int processId, AgentGameApiReadSelectedUnitCodeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ReadSelectedUnitSnapshotViaGameApiAsync);
            return Task.FromResult(new AgentGameApiSelectedUnitSnapshotPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0x6586A5A0, 0x0D7DF7E0,
                GameApiDispatchStatus.Completed, 1, 100, 101));
        }

        public Task<AgentGameApiLevelUpSelectedPayload> LevelUpSelectedAsync(
            int processId, AgentGameApiLevelUpSelectedRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(LevelUpSelectedAsync);
            LastLevelUpRequest = request;
            return Task.FromResult(new AgentGameApiLevelUpSelectedPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiCreateUnitPayload> CreateUnitAsync(
            int processId, AgentGameApiCreateUnitRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(CreateUnitAsync);
            return Task.FromResult(new AgentGameApiCreateUnitPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0x12345678,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiKillUnitPayload> KillUnitAsync(
            int processId, AgentGameApiKillUnitRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(KillUnitAsync);
            LastKillRequest = request;
            return Task.FromResult(new AgentGameApiKillUnitPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiCopyForMePayload> CopyForMeAsync(
            int processId, AgentGameApiCopyForMeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(CopyForMeAsync);
            LastCopyForMeRequest = request;
            return Task.FromResult(new AgentGameApiCopyForMePayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0x12345678,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiGetMeBasePayload> GetMeBaseAsync(
            int processId, AgentGameApiGetMeBaseRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GetMeBaseAsync);
            LastGetMeBaseRequest = request;
            return Task.FromResult(new AgentGameApiGetMeBasePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiWeNeedBackPayload> WeNeedBackAsync(
            int processId, AgentGameApiWeNeedBackRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(WeNeedBackAsync);
            LastWeNeedBackRequest = request;
            return Task.FromResult(new AgentGameApiWeNeedBackPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiSetUnitStatePayload> SetUnitStateAsync(
            int processId, AgentGameApiSetUnitStateRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SetUnitStateAsync);
            LastSetUnitStateRequest = request;
            return Task.FromResult(new AgentGameApiSetUnitStatePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiGetCurrentPlayerPayload> GetCurrentPlayerAsync(
            int processId, AgentGameApiGetCurrentPlayerRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GetCurrentPlayerAsync);
            return Task.FromResult(new AgentGameApiGetCurrentPlayerPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0x12345678,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiLookupScienceByHashPayload> LookupScienceByHashAsync(
            int processId, AgentGameApiLookupScienceByHashRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(LookupScienceByHashAsync);
            return Task.FromResult(new AgentGameApiLookupScienceByHashPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0x12345678,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiGrantPlayerTechPayload> GrantPlayerTechAsync(
            int processId, AgentGameApiGrantPlayerTechRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GrantPlayerTechAsync);
            LastGrantPlayerTechRequest = request;
            return Task.FromResult(new AgentGameApiGrantPlayerTechPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiGrantUpgradeToPlayerPayload> GrantUpgradeToPlayerAsync(
            int processId, AgentGameApiGrantUpgradeToPlayerRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GrantUpgradeToPlayerAsync);
            return Task.FromResult(new AgentGameApiGrantUpgradeToPlayerPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiHasUpgradePayload> HasUpgradeAsync(
            int processId, AgentGameApiHasUpgradeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(HasUpgradeAsync);
            return Task.FromResult(new AgentGameApiHasUpgradePayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 1,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiLookupTemplateByHashPayload> LookupTemplateByHashAsync(
            int processId, AgentGameApiLookupTemplateByHashRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(LookupTemplateByHashAsync);
            return Task.FromResult(new AgentGameApiLookupTemplateByHashPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0x12345678,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiLookupUpgradeByHashPayload> LookupUpgradeByHashAsync(
            int processId, AgentGameApiLookupUpgradeByHashRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(LookupUpgradeByHashAsync);
            return Task.FromResult(new AgentGameApiLookupUpgradeByHashPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0x12345678,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiGrantSecretProtocolPayload> GrantSecretProtocolAsync(
            int processId, AgentGameApiGrantSecretProtocolRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GrantSecretProtocolAsync);
            LastGrantSecretProtocolRequest = request;
            return Task.FromResult(new AgentGameApiGrantSecretProtocolPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiGrantSelectedUpgradePayload> GrantSelectedUpgradeAsync(
            int processId, AgentGameApiGrantSelectedUpgradeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GrantSelectedUpgradeAsync);
            LastGrantSelectedUpgradeRequest = request;
            return Task.FromResult(new AgentGameApiGrantSelectedUpgradePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiClearPlayerTechLocksPayload> ClearPlayerTechLocksAsync(
            int processId, AgentGameApiClearPlayerTechLocksRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ClearPlayerTechLocksAsync);
            return Task.FromResult(new AgentGameApiClearPlayerTechLocksPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiSecretProtocolBindingProbePayload> SecretProtocolBindingProbeAsync(
            int processId, AgentGameApiSecretProtocolBindingProbeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SecretProtocolBindingProbeAsync);
            return Task.FromResult(new AgentGameApiSecretProtocolBindingProbePayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiReplaceTemplateModelPayload> ReplaceTemplateModelAsync(
            int processId, AgentGameApiReplaceTemplateModelRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ReplaceTemplateModelAsync);
            LastReplaceTemplateModelRequest = request;
            return Task.FromResult(new AgentGameApiReplaceTemplateModelPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiReplaceTemplateWeaponPayload> ReplaceTemplateWeaponAsync(
            int processId, AgentGameApiReplaceTemplateWeaponRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ReplaceTemplateWeaponAsync);
            LastReplaceTemplateWeaponRequest = request;
            return Task.FromResult(new AgentGameApiReplaceTemplateWeaponPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiSetSelectedStatusBitPayload> SetSelectedStatusBitAsync(
            int processId, AgentGameApiSetSelectedStatusBitRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SetSelectedStatusBitAsync);
            return Task.FromResult(new AgentGameApiSetSelectedStatusBitPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiSetSelectedUnitHealthPayload> SetSelectedUnitHealthAsync(
            int processId, AgentGameApiSetSelectedUnitHealthRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SetSelectedUnitHealthAsync);
            LastHealthRequest = request;
            return Task.FromResult(new AgentGameApiSetSelectedUnitHealthPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiExpandProductionQueuePayload> ExpandProductionQueueAsync(
            int processId, AgentGameApiExpandProductionQueueRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ExpandProductionQueueAsync);
            LastExpandRequest = request;
            return Task.FromResult(new AgentGameApiExpandProductionQueuePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 2, 0));
        }

        public Task<AgentGameApiTeleportSelectedUnitsToMousePayload> TeleportSelectedUnitsToMouseAsync(
            int processId, AgentGameApiTeleportSelectedUnitsToMouseRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(TeleportSelectedUnitsToMouseAsync);
            LastTeleportRequest = request;
            return Task.FromResult(new AgentGameApiTeleportSelectedUnitsToMousePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiSetSelectedUnitSpeedPayload> SetSelectedUnitSpeedAsync(
            int processId, AgentGameApiSetSelectedUnitSpeedRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SetSelectedUnitSpeedAsync);
            LastSpeedRequest = request;
            return Task.FromResult(new AgentGameApiSetSelectedUnitSpeedPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiCaptureSelectedUnitsPayload> CaptureSelectedUnitsAsync(
            int processId, AgentGameApiCaptureSelectedUnitsRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(CaptureSelectedUnitsAsync);
            LastCaptureRequest = request;
            return Task.FromResult(new AgentGameApiCaptureSelectedUnitsPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiSetSelectedUnitAmmoPayload> SetSelectedUnitAmmoAsync(
            int processId, AgentGameApiSetSelectedUnitAmmoRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(SetSelectedUnitAmmoAsync);
            LastAmmoRequest = request;
            return Task.FromResult(new AgentGameApiSetSelectedUnitAmmoPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiToggleSelectedAttackSpeedPayload> ToggleSelectedAttackSpeedAsync(
            int processId, AgentGameApiToggleSelectedAttackSpeedRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ToggleSelectedAttackSpeedAsync);
            return Task.FromResult(new AgentGameApiToggleSelectedAttackSpeedPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiToggleSelectedAttackRangePayload> ToggleSelectedAttackRangeAsync(
            int processId, AgentGameApiToggleSelectedAttackRangeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ToggleSelectedAttackRangeAsync);
            return Task.FromResult(new AgentGameApiToggleSelectedAttackRangePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiClearSelectedAttackSpeedEffectsPayload> ClearSelectedAttackSpeedEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackSpeedEffectsRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ClearSelectedAttackSpeedEffectsAsync);
            return Task.FromResult(new AgentGameApiClearSelectedAttackSpeedEffectsPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiClearSelectedAttackRangeEffectsPayload> ClearSelectedAttackRangeEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackRangeEffectsRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(ClearSelectedAttackRangeEffectsAsync);
            return Task.FromResult(new AgentGameApiClearSelectedAttackRangeEffectsPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiSelectedUnitUpgradesPayload> GetSelectedUnitUpgradesAsync(
            int processId, AgentGameApiGetSelectedUnitUpgradesRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GetSelectedUnitUpgradesAsync);
            return Task.FromResult(new AgentGameApiSelectedUnitUpgradesPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                UnitTypeId: 0, ThingTemplateAddress: 0, Count: 0,
                UpgradeHash0: 0, UpgradeHash1: 0, UpgradeHash2: 0, UpgradeHash3: 0,
                UpgradeHash4: 0, UpgradeHash5: 0, UpgradeHash6: 0, UpgradeHash7: 0,
                UpgradeHash8: 0, UpgradeHash9: 0, UpgradeHash10: 0, UpgradeHash11: 0,
                UpgradeHash12: 0, UpgradeHash13: 0, UpgradeHash14: 0, UpgradeHash15: 0,
                UpgradeHash16: 0, UpgradeHash17: 0, UpgradeHash18: 0, UpgradeHash19: 0,
                DispatchStatus: GameApiDispatchStatus.Completed,
                RequestId: 1, GameThreadTickBefore: 0, GameThreadTickAfter: 0));
        }

        public Task<AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload> GrantObjectUpgradeOnSelectedSameTypeAsync(
            int processId, AgentGameApiGrantObjectUpgradeOnSelectedSameTypeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCalledMethod = nameof(GrantObjectUpgradeOnSelectedSameTypeAsync);
            return Task.FromResult(new AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameApiDispatchStatus.Completed, 1, 0, 0));
        }
    }
}
