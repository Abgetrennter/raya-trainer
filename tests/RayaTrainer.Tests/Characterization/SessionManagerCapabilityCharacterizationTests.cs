using RayaTrainer.App.Services;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Characterization;

/// <summary>
/// Golden-master characterization tests for <see cref="TrainerSessionManager.GetFeatureCapability"/>.
/// These tests pin the CURRENT observable (pre-P1-refactor) behavior and must fail loudly
/// if refactors in P1.2/P1.3 change the output.
/// </summary>
[Trait("Category", "Characterization")]
public sealed class SessionManagerCapabilityCharacterizationTests
{
    private static readonly AgentStatusPayload DefaultStatus = new(
        AgentStatusCode.Ok, AgentProtocol.Version, ProcessId: 1234, ModuleBase: 0x400000, InstalledHookCount: 24);

    /// <summary>
    /// Builds a <see cref="TrainerSessionManager"/> with precise internal state set via
    /// reflection so we can pin the special-case logic in GetFeatureCapability without
    /// going through the full AttachTarget + InstallPatches pipeline.
    /// </summary>
    private static TrainerSessionManager CreateReflectionSession(
        TrainerTarget? currentTarget = null,
        bool patchesInstalled = true,
        ITrainerFeatureController? controller = null,
        InjectedAgentBackend? backend = null)
    {
        var sm = new TrainerSessionManager();
        if (currentTarget is not null)
            ReflectionHelper.SetPrivateField(sm, "_currentTarget", currentTarget);
        ReflectionHelper.SetPrivateField(sm, "_arePatchesInstalled", patchesInstalled);
        if (controller is not null)
            ReflectionHelper.SetPrivateField(sm, "_featureController", controller);
        if (backend is not null)
            ReflectionHelper.SetPrivateField(sm, "_agentBackend", backend);
        // Clear unavailable reasons so profile-gated features reach the special-case logic
        ReflectionHelper.SetPrivateField(sm, "_unavailableFeatureReasons",
            new Dictionary<string, string>());
        return sm;
    }

    private static IAgentFeatureController CreateAgentController(IAgentClient client) =>
        new AgentFeatureController(client, 1234, DefaultStatus, supportsDirectGameApi: true);

    private static TrainerTarget Ra3112Target(int? processId = 1234) => new(
        "ra3_1.12.game", 0x400000, Is32Bit: true, VersionSupported: true,
        ProcessId: processId, VersionProfileId: "ra3_1.12");

    private static TrainerTarget Ra3113Target(int? processId = 1234) => new(
        "ra3_1.13.game", 0x400000, Is32Bit: true, VersionSupported: true,
        ProcessId: processId, VersionProfileId: "ra3_1.13");

    private static TrainerTarget UprisingTarget(int? processId = 1234) => new(
        "ra3ep1_1.0.game", 0x400000, Is32Bit: true, VersionSupported: true,
        ProcessId: processId, VersionProfileId: "ra3ep1_1.0");

    private static readonly TrainerFeature UnitUpgradeFeature = new(
        TrainerFeatureIds.SelectedUnitObjectUpgrade, "单位升级", null, [], null, null,
        SupportedProfileIds: ["ra3_1.12"], RequiresDirectGameApi: true);

    private static readonly TrainerFeature FrameRateFeature = new(
        "Frame Rate Unlock 60fps", "60fps 帧率解锁", null, ["Frame Rate Unlock 60fps"], null, null,
        SupportedProfileIds: ["ra3_1.12"]);

    private static readonly TrainerFeature PowerFeature = new(
        TrainerFeatureIds.Power, "电力", null, [], null, null);

    private static readonly TrainerFeature SecretProtocolDependencyBypassFeature = new(
        TrainerFeatureIds.SecretProtocolDependencyBypass, "秘密协议依赖绕过", null, [], null, null);

    #region SelectedUnitObjectUpgrade special case

    [Fact]
    public void SelectedUnitObjectUpgrade_OnRa3112WithNativeLayoutReady_ReturnsReady()
    {
        // This is the only reachable path with current game profiles: ra3_1.12
        // has the three required NativeAgentRef entries (GameObjectAddUpgrade,
        // ProductionModulesOffset, UpgradeTemplateTypeOffset) all Verified with
        // non-zero RVA.
        var client = new StubAgentClient();
        var controller = CreateAgentController(client);
        var session = CreateReflectionSession(
            currentTarget: Ra3112Target(),
            controller: controller);

        var result = session.GetFeatureCapability(UnitUpgradeFeature);

        Assert.Equal(FeatureCapabilityState.Ready, result.State);
        Assert.Equal("READY", result.ReasonCode);
    }

    [Fact]
    public void SelectedUnitObjectUpgrade_OnRa3113WrongProfile_ReturnsUnavailableWithProfileReason()
    {
        // The special case's first condition: profile is not ra3_1.12 →
        // downgrades to Unavailable with UNIT_UPGRADE_PROFILE_NOT_SUPPORTED.
        var client = new StubAgentClient();
        var controller = CreateAgentController(client);
        var session = CreateReflectionSession(
            currentTarget: Ra3113Target(),
            controller: controller);

        var result = session.GetFeatureCapability(UnitUpgradeFeature);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
        Assert.Equal("UNIT_UPGRADE_PROFILE_NOT_SUPPORTED", result.ReasonCode);
    }

    [Fact]
    public void SelectedUnitObjectUpgrade_OnUprisingProfile_ReturnsUnavailable()
    {
        // Uprising profile does not support DirectGameApi, so the evaluator returns
        // Unavailable with DIRECT_GAME_API_REQUIRED before the special case runs.
        var client = new StubAgentClient();
        var controller = CreateAgentController(client);
        var session = CreateReflectionSession(
            currentTarget: UprisingTarget(),
            controller: controller);

        var result = session.GetFeatureCapability(UnitUpgradeFeature);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
        Assert.Equal("DIRECT_GAME_API_REQUIRED", result.ReasonCode);
    }

    [Fact]
    public void IsUnitUpgradeNativeLayoutReady_WithMissingEntries_ReturnsFalse()
    {
        // Static method test: when a required entry is not Verified,
        // the layout is reported as not ready.
        var profile = Ra3VersionProfileRegistry.Ra3112;
        var modifiedRefs = new Dictionary<string, VersionedAddress>(profile.NativeAgentRefs)
        {
            ["GameObjectAddUpgrade"] = new VersionedAddress("GameObjectAddUpgrade", null, AddressSupportStatus.Unsupported, "test")
        };
        var patchedProfile = new Ra3VersionProfile
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            ProcessName = profile.ProcessName,
            FileVersions = profile.FileVersions,
            Hooks = profile.Hooks,
            RemoteGlobals = profile.RemoteGlobals,
            EngineFunctions = profile.EngineFunctions,
            NativeAgentRefs = modifiedRefs.AsReadOnly(),
            SupportsAgentBackend = profile.SupportsAgentBackend,
            SupportsDirectGameApi = profile.SupportsDirectGameApi,
            OptionalSignatureSymbols = profile.OptionalSignatureSymbols
        };

        Assert.False(TrainerSessionManager.IsUnitUpgradeNativeLayoutReady(patchedProfile));
    }

    #endregion

    #region FrameRateUnlock composite gate

    [Fact]
    public void FrameRateUnlock_OnRa3112WithFullComposite_ReturnsReady()
    {
        // B1 happy path: run the full attach+install flow. After a successful install,
        // the backend's InstalledNativeHookIds and PatchSetsRegistered collections are
        // populated by the actual manifest hooks/patchsets, which include Hook 41 and
        // PatchSet 1 when the manifest and profile support them.
        var client = new StubAgentClient();
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), client),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = Ra3112Target();
        manager.AttachTarget(manifest, target);
        manager.InstallPatches(manifest, "diagnostics");

        var result = manager.GetFeatureCapability(FrameRateFeature);

        Assert.Equal(FeatureCapabilityState.Ready, result.State);
        Assert.Equal("READY", result.ReasonCode);
    }

    [Fact]
    public void FrameRateUnlock_OnRa3112WithHook41Missing_ReturnsUnavailable()
    {
        var client = new StubAgentClient();
        var session = CreateReflectionSession(
            currentTarget: Ra3112Target(),
            controller: CreateAgentController(client),
            backend: new InjectedAgentBackend(new FakeAgentInjector(), client));

        var result = session.GetFeatureCapability(FrameRateFeature);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
        Assert.Equal("FRAMERATE_COMPOSITE_INCOMPLETE", result.ReasonCode);
    }

    [Fact]
    public void FrameRateUnlock_OnRa3112WithPatchSet1Missing_ReturnsUnavailable()
    {
        // Use full attach+install to populate the backend, then create a second session
        // that only has hooks (no patchsets registered).
        var client = new StubAgentClient();
        var injector = new FakeAgentInjector();
        var backend = new InjectedAgentBackend(injector, client);

        // Attach+deliver+install to populate the backend's collections
        var manager = new TrainerSessionManager(
            () => backend,
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        manager.AttachTarget(manifest, Ra3112Target());
        manager.InstallPatches(manifest, "diagnostics");
        // Now backend has real InstalledNativeHookIds populated by the install.

        // Create a separate reflection session that keeps hooks but clears PatchSetsRegistered.
        // We access the property via reflection to simulate the missing PatchSet 1 scenario.
        var prop = typeof(InjectedAgentBackend).GetProperty("PatchSetsRegistered",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        prop.SetValue(backend, Array.Empty<uint>());

        var session = CreateReflectionSession(
            currentTarget: Ra3112Target(),
            controller: CreateAgentController(client),
            backend: backend);

        var result = session.GetFeatureCapability(FrameRateFeature);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
        Assert.Equal("FRAMERATE_COMPOSITE_INCOMPLETE", result.ReasonCode);
    }

    [Fact]
    public void FrameRateUnlock_OnNonRa3112Profile_ReturnsUnavailable()
    {
        var client = new StubAgentClient();
        var session = CreateReflectionSession(
            currentTarget: Ra3113Target(),
            controller: CreateAgentController(client));

        // Feature only supports ra3_1.12, so profile availability blocks it
        // before the special case runs → reason code is profile-related
        var result = session.GetFeatureCapability(FrameRateFeature);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
    }

    #endregion

    #region Simple toggle capability

    [Fact]
    public void PowerToggle_OnConnectedRa3112Session_ReturnsReady()
    {
        var client = new StubAgentClient();
        var controller = CreateAgentController(client);
        var session = CreateReflectionSession(
            currentTarget: Ra3112Target(),
            controller: controller);

        var result = session.GetFeatureCapability(PowerFeature);

        Assert.Equal(FeatureCapabilityState.Ready, result.State);
        Assert.Equal("READY", result.ReasonCode);
    }

    [Fact]
    public void PowerToggle_OnDisconnectedSession_ReturnsWaiting()
    {
        var session = new TrainerSessionManager();
        // No target, no patches, no controller → Waiting/NO_TARGET

        var result = session.GetFeatureCapability(PowerFeature);

        Assert.Equal(FeatureCapabilityState.Waiting, result.State);
        Assert.Equal("NO_TARGET", result.ReasonCode);
    }

    #endregion

    #region Additional capability probes

    [Fact]
    public void SecretProtocolDependencyBypass_OnConnectedSession_ReturnsReady()
    {
        var client = new StubAgentClient();
        var controller = CreateAgentController(client);
        var session = CreateReflectionSession(
            currentTarget: Ra3112Target(),
            controller: controller);

        var result = session.GetFeatureCapability(SecretProtocolDependencyBypassFeature);

        Assert.Equal(FeatureCapabilityState.Ready, result.State);
        Assert.Equal("READY", result.ReasonCode);
    }

    [Fact]
    public void SecretProtocolDependencyBypass_OnDisconnectedSession_ReturnsWaiting()
    {
        var session = new TrainerSessionManager();

        var result = session.GetFeatureCapability(SecretProtocolDependencyBypassFeature);

        Assert.Equal(FeatureCapabilityState.Waiting, result.State);
        Assert.Equal("NO_TARGET", result.ReasonCode);
    }

    [Fact]
    public void SelectedUnitObjectUpgrade_OnDisconnectedSession_ReturnsWaiting()
    {
        var session = new TrainerSessionManager();

        var result = session.GetFeatureCapability(UnitUpgradeFeature);

        // RequiresDirectGameApi=true but no target → NO_TARGET
        Assert.Equal(FeatureCapabilityState.Waiting, result.State);
        Assert.Equal("NO_TARGET", result.ReasonCode);
    }

    [Fact]
    public void FrameRateUnlock_OnDisconnectedSession_ReturnsWaiting()
    {
        var session = new TrainerSessionManager();

        var result = session.GetFeatureCapability(FrameRateFeature);

        Assert.Equal(FeatureCapabilityState.Waiting, result.State);
        Assert.Equal("NO_TARGET", result.ReasonCode);
    }

    [Fact]
    public void GetFeatureCapability_WithNullFeature_Throws()
    {
        var session = new TrainerSessionManager();

        Assert.Throws<ArgumentNullException>(() => session.GetFeatureCapability(null!));
    }

    #endregion

    /// <summary>
    /// Minimal IAgentClient stub for session manager characterization tests.
    /// Only base IAgentClient methods return OK; GameApi methods throw.
    /// </summary>
    private sealed class StubAgentClient : IAgentClient
    {
        public AgentSignatureScanPayload SignatureScanPayload { get; init; } =
            TestAgentSignatureCatalog.CreateRa3112();

        public int PingFailuresRemaining { get; set; } = 1;

        public Task<AgentPingPayload> PingAsync(int processId, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (PingFailuresRemaining-- > 0)
                throw new TimeoutException("no existing agent");
            return Task.FromResult(new AgentPingPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, processId,
                0x400000, (uint)NativeRuntimeCapabilities.Required,
                AgentBuildIdentity.Fingerprint));
        }

        public Task<AgentStatusPayload> GetStatusAsync(int processId, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentStatusPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, processId,
                0x400000, 0, (uint)NativeRuntimeCapabilities.Required));

        public Task<AgentCommandResultPayload> InstallPatchesAsync(int processId,
            AgentInstallPatchesRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                checked((uint)request.Hooks.Count)));

        public Task<AgentCommandResultPayload> RestorePatchesAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0));

        public Task<AgentCommandResultPayload> SetFeatureStatesAsync(int processId,
            SetFeatureStatesRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 24));

        public Task<AgentCommandResultPayload> SetRuntimePatchSetAsync(int processId,
            uint patchSetId, bool enable, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0));

        public Task<FeatureStatesResponse> GetFeatureStatesAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FeatureStatesResponse(
                AgentStatusCode.Ok, AgentProtocol.Version, Array.Empty<FeatureStateEntry>()));

        public Task<AgentMemoryReadPayload> ReadMemoryAsync(int processId,
            AgentMemoryReadRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentMemoryReadPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, request.Address,
                TestAssets.ResolveRuntimePatchSetDisableBytes(request.Address, request.ByteCount)
                    ?? new byte[request.ByteCount]));

        public Task<AgentCommandResultPayload> SetNativeCatalogAsync(int processId,
            IReadOnlyList<uint> rvas, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0));

        public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentMismatchDiagnosticsPayload(
                AgentStatusCode.InvalidCommand, AgentProtocol.Version, 0,
                [], [], [], MismatchKind.Hook, 0));

        public Task<AgentSignatureScanPayload> ScanSignaturesAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(SignatureScanPayload);

        public Task<AgentGameModePayload> GetGameModeAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentGameModePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameRuntimeConstants.GameModeShell));

        // GameApi methods — not needed for session manager capability checks
        public Task<AgentGameApiGetThingClassPayload> SmokeGetThingClassAsync(
            int processId, AgentGameApiGetThingClassRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSelectedUnitSnapshotPayload> ReadSelectedUnitSnapshotViaGameApiAsync(
            int processId, AgentGameApiReadSelectedUnitCodeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiLevelUpSelectedPayload> LevelUpSelectedAsync(
            int processId, AgentGameApiLevelUpSelectedRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiCreateUnitPayload> CreateUnitAsync(
            int processId, AgentGameApiCreateUnitRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiKillUnitPayload> KillUnitAsync(
            int processId, AgentGameApiKillUnitRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiCopyForMePayload> CopyForMeAsync(
            int processId, AgentGameApiCopyForMeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGetMeBasePayload> GetMeBaseAsync(
            int processId, AgentGameApiGetMeBaseRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiWeNeedBackPayload> WeNeedBackAsync(
            int processId, AgentGameApiWeNeedBackRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSetUnitStatePayload> SetUnitStateAsync(
            int processId, AgentGameApiSetUnitStateRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGetCurrentPlayerPayload> GetCurrentPlayerAsync(
            int processId, AgentGameApiGetCurrentPlayerRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiLookupScienceByHashPayload> LookupScienceByHashAsync(
            int processId, AgentGameApiLookupScienceByHashRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantPlayerTechPayload> GrantPlayerTechAsync(
            int processId, AgentGameApiGrantPlayerTechRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantUpgradeToPlayerPayload> GrantUpgradeToPlayerAsync(
            int processId, AgentGameApiGrantUpgradeToPlayerRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiHasUpgradePayload> HasUpgradeAsync(
            int processId, AgentGameApiHasUpgradeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiLookupTemplateByHashPayload> LookupTemplateByHashAsync(
            int processId, AgentGameApiLookupTemplateByHashRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiLookupUpgradeByHashPayload> LookupUpgradeByHashAsync(
            int processId, AgentGameApiLookupUpgradeByHashRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantSecretProtocolPayload> GrantSecretProtocolAsync(
            int processId, AgentGameApiGrantSecretProtocolRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantSelectedUpgradePayload> GrantSelectedUpgradeAsync(
            int processId, AgentGameApiGrantSelectedUpgradeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiClearPlayerTechLocksPayload> ClearPlayerTechLocksAsync(
            int processId, AgentGameApiClearPlayerTechLocksRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSecretProtocolBindingProbePayload> SecretProtocolBindingProbeAsync(
            int processId, AgentGameApiSecretProtocolBindingProbeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiReplaceTemplateModelPayload> ReplaceTemplateModelAsync(
            int processId, AgentGameApiReplaceTemplateModelRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiReplaceTemplateWeaponPayload> ReplaceTemplateWeaponAsync(
            int processId, AgentGameApiReplaceTemplateWeaponRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSetSelectedStatusBitPayload> SetSelectedStatusBitAsync(
            int processId, AgentGameApiSetSelectedStatusBitRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSetSelectedUnitHealthPayload> SetSelectedUnitHealthAsync(
            int processId, AgentGameApiSetSelectedUnitHealthRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiExpandProductionQueuePayload> ExpandProductionQueueAsync(
            int processId, AgentGameApiExpandProductionQueueRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiTeleportSelectedUnitsToMousePayload> TeleportSelectedUnitsToMouseAsync(
            int processId, AgentGameApiTeleportSelectedUnitsToMouseRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSetSelectedUnitSpeedPayload> SetSelectedUnitSpeedAsync(
            int processId, AgentGameApiSetSelectedUnitSpeedRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiCaptureSelectedUnitsPayload> CaptureSelectedUnitsAsync(
            int processId, AgentGameApiCaptureSelectedUnitsRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSetSelectedUnitAmmoPayload> SetSelectedUnitAmmoAsync(
            int processId, AgentGameApiSetSelectedUnitAmmoRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiToggleSelectedAttackSpeedPayload> ToggleSelectedAttackSpeedAsync(
            int processId, AgentGameApiToggleSelectedAttackSpeedRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiToggleSelectedAttackRangePayload> ToggleSelectedAttackRangeAsync(
            int processId, AgentGameApiToggleSelectedAttackRangeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiClearSelectedAttackSpeedEffectsPayload> ClearSelectedAttackSpeedEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackSpeedEffectsRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiClearSelectedAttackRangeEffectsPayload> ClearSelectedAttackRangeEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackRangeEffectsRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSelectedUnitUpgradesPayload> GetSelectedUnitUpgradesAsync(
            int processId, AgentGameApiGetSelectedUnitUpgradesRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload> GrantObjectUpgradeOnSelectedSameTypeAsync(
            int processId, AgentGameApiGrantObjectUpgradeOnSelectedSameTypeRequest request,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
