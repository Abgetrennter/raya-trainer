using RayaTrainer.App.Services;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class NativeCatalogDeliveryTests
{
    /// <summary>
    /// FakeAgentClient that can simulate slow or failing native catalog delivery.
    /// </summary>
    private sealed class FakeAgentClient : IAgentClient
    {
        public AgentSignatureScanPayload SignatureScanPayload { get; init; } =
            TestAgentSignatureCatalog.CreateRa3112();

        public AgentInstallPatchesRequest? InstallRequest { get; set; }

        /// <summary>
        /// If non-null, the delay to introduce before completing SetNativeCatalogAsync.
        /// </summary>
        public TimeSpan? SetNativeCatalogDelay { get; set; }

        /// <summary>
        /// If non-null, the exception to throw from SetNativeCatalogAsync.
        /// </summary>
        public Exception? SetNativeCatalogException { get; set; }

        public AgentCommandResultPayload? SetNativeCatalogResult { get; set; }

        public Task<AgentPingPayload> PingAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentPingPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                processId,
                0x400000,
                (uint)NativeRuntimeCapabilities.Required,
                AgentBuildIdentity.Fingerprint));
        }

        public Task<AgentStatusPayload> GetStatusAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentStatusPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                processId,
                0x400000,
                InstalledHookCount: 0,
                (uint)NativeRuntimeCapabilities.Required));
        }

        public Task<AgentCommandResultPayload> InstallPatchesAsync(int processId, AgentInstallPatchesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            InstallRequest = request;
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, checked((uint)request.Hooks.Count)));
        }

        public Task<AgentCommandResultPayload> RestorePatchesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<AgentCommandResultPayload> SetFeatureStatesAsync(int processId, SetFeatureStatesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<AgentCommandResultPayload> SetRuntimePatchSetAsync(int processId, uint patchSetId, bool enable, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<FeatureStatesResponse> GetFeatureStatesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FeatureStatesResponse(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                Array.Empty<FeatureStateEntry>()));
        }

        public async Task<AgentCommandResultPayload> SetNativeCatalogAsync(int processId, IReadOnlyList<uint> rvas, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (SetNativeCatalogDelay.HasValue)
            {
                await Task.Delay(SetNativeCatalogDelay.Value, cancellationToken).ConfigureAwait(false);
            }

            if (SetNativeCatalogException is not null)
            {
                throw SetNativeCatalogException;
            }

            if (SetNativeCatalogResult is not null)
            {
                return (AgentCommandResultPayload)SetNativeCatalogResult;
            }

            return new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0);
        }

        public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentMismatchDiagnosticsPayload(
                AgentStatusCode.InvalidCommand, AgentProtocol.Version, 0, [], [], [], MismatchKind.Hook, 0));
        }

        public Task<AgentSignatureScanPayload> ScanSignaturesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SignatureScanPayload);
        }

        public Task<AgentGameModePayload> GetGameModeAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameModePayload(AgentStatusCode.Ok, AgentProtocol.Version, GameRuntimeConstants.GameModeShell));
        }

        public Task<AgentMemoryReadPayload> ReadMemoryAsync(int processId, AgentMemoryReadRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentMemoryReadPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, request.Address, new byte[request.ByteCount]));
        }

        #region IAgentGameApiClient (not used in these tests)
        public Task<AgentGameApiGetThingClassPayload> SmokeGetThingClassAsync(int processId, AgentGameApiGetThingClassRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSelectedUnitSnapshotPayload> ReadSelectedUnitSnapshotViaGameApiAsync(int processId, AgentGameApiReadSelectedUnitCodeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiLevelUpSelectedPayload> LevelUpSelectedAsync(int processId, AgentGameApiLevelUpSelectedRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiCreateUnitPayload> CreateUnitAsync(int processId, AgentGameApiCreateUnitRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiKillUnitPayload> KillUnitAsync(int processId, AgentGameApiKillUnitRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiCopyForMePayload> CopyForMeAsync(int processId, AgentGameApiCopyForMeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGetMeBasePayload> GetMeBaseAsync(int processId, AgentGameApiGetMeBaseRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiWeNeedBackPayload> WeNeedBackAsync(int processId, AgentGameApiWeNeedBackRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetUnitStatePayload> SetUnitStateAsync(int processId, AgentGameApiSetUnitStateRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGetCurrentPlayerPayload> GetCurrentPlayerAsync(int processId, AgentGameApiGetCurrentPlayerRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiLookupScienceByHashPayload> LookupScienceByHashAsync(int processId, AgentGameApiLookupScienceByHashRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantPlayerTechPayload> GrantPlayerTechAsync(int processId, AgentGameApiGrantPlayerTechRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantUpgradeToPlayerPayload> GrantUpgradeToPlayerAsync(int processId, AgentGameApiGrantUpgradeToPlayerRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiHasUpgradePayload> HasUpgradeAsync(int processId, AgentGameApiHasUpgradeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiLookupTemplateByHashPayload> LookupTemplateByHashAsync(int processId, AgentGameApiLookupTemplateByHashRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiLookupUpgradeByHashPayload> LookupUpgradeByHashAsync(int processId, AgentGameApiLookupUpgradeByHashRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantSecretProtocolPayload> GrantSecretProtocolAsync(int processId, AgentGameApiGrantSecretProtocolRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantSelectedUpgradePayload> GrantSelectedUpgradeAsync(int processId, AgentGameApiGrantSelectedUpgradeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiClearPlayerTechLocksPayload> ClearPlayerTechLocksAsync(int processId, AgentGameApiClearPlayerTechLocksRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSecretProtocolBindingProbePayload> SecretProtocolBindingProbeAsync(int processId, AgentGameApiSecretProtocolBindingProbeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiReplaceTemplateModelPayload> ReplaceTemplateModelAsync(int processId, AgentGameApiReplaceTemplateModelRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiReplaceTemplateWeaponPayload> ReplaceTemplateWeaponAsync(int processId, AgentGameApiReplaceTemplateWeaponRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedStatusBitPayload> SetSelectedStatusBitAsync(int processId, AgentGameApiSetSelectedStatusBitRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedUnitHealthPayload> SetSelectedUnitHealthAsync(int processId, AgentGameApiSetSelectedUnitHealthRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiExpandProductionQueuePayload> ExpandProductionQueueAsync(int processId, AgentGameApiExpandProductionQueueRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiTeleportSelectedUnitsToMousePayload> TeleportSelectedUnitsToMouseAsync(int processId, AgentGameApiTeleportSelectedUnitsToMouseRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedUnitSpeedPayload> SetSelectedUnitSpeedAsync(int processId, AgentGameApiSetSelectedUnitSpeedRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiCaptureSelectedUnitsPayload> CaptureSelectedUnitsAsync(int processId, AgentGameApiCaptureSelectedUnitsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedUnitAmmoPayload> SetSelectedUnitAmmoAsync(int processId, AgentGameApiSetSelectedUnitAmmoRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiToggleSelectedAttackSpeedPayload> ToggleSelectedAttackSpeedAsync(int processId, AgentGameApiToggleSelectedAttackSpeedRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiToggleSelectedAttackRangePayload> ToggleSelectedAttackRangeAsync(int processId, AgentGameApiToggleSelectedAttackRangeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiClearSelectedAttackSpeedEffectsPayload> ClearSelectedAttackSpeedEffectsAsync(int processId, AgentGameApiClearSelectedAttackSpeedEffectsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiClearSelectedAttackRangeEffectsPayload> ClearSelectedAttackRangeEffectsAsync(int processId, AgentGameApiClearSelectedAttackRangeEffectsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSelectedUnitUpgradesPayload> GetSelectedUnitUpgradesAsync(int processId, AgentGameApiGetSelectedUnitUpgradesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload> GrantObjectUpgradeOnSelectedSameTypeAsync(int processId, AgentGameApiGrantObjectUpgradeOnSelectedSameTypeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        #endregion
    }

    private sealed class FakeAgentInjector : IAgentInjector
    {
        public AgentInjectionResult Inject(int processId, string agentDllPath, TimeSpan timeout)
        {
            return new AgentInjectionResult(true, "ok", 0x5000);
        }
    }

    private static TrainerTarget CreateRa3112Target()
    {
        return new TrainerTarget(
            ProcessName: "ra3_1.12.game",
            ModuleBase: new nint(0x400000),
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 12345,
            FileVersion: "1.12.0.0",
            ModulePath: @"C:\fake\ra3_1.12.game",
            VersionProfileId: "ra3_1.12",
            SignatureCompatibilityMode: false);
    }

    private static TrainerManifest CreateMinimalManifest()
    {
        return new TrainerManifest(
            TargetProcess: "ra3_1.12.game",
            Features: [],
            PatchManifest: new PatchManifest(Hooks: []),
            ActionDispatch: []);
    }

    [Fact]
    public void DeliverNativeCatalog_Succeeds_WithinTimeout()
    {
        // Happy path: catalog delivery completes within the default 5-second timeout.
        var client = new FakeAgentClient();
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);
        backend.CatalogDeliveryTimeout = TimeSpan.FromSeconds(5);

        var target = CreateRa3112Target();
        var result = backend.AttachAsync(target, CreateMinimalManifest(), "RayaTrainer.Agent.dll", TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();

        // After attach, deliver the catalog
        backend.DeliverNativeCatalogAsync(target, TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();

        Assert.True(backend.IsConnected);
    }

    [Fact]
    public void DeliverNativeCatalog_TimesOut_ThrowsNativeCatalogDeliveryException()
    {
        // Slow catalog delivery exceeding the CatalogDeliveryTimeout.
        var client = new FakeAgentClient
        {
            SetNativeCatalogDelay = TimeSpan.FromSeconds(10)
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);
        backend.CatalogDeliveryTimeout = TimeSpan.FromMilliseconds(100);

        var target = CreateRa3112Target();
        backend.AttachAsync(target, CreateMinimalManifest(), "RayaTrainer.Agent.dll", TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();

        var ex = Assert.Throws<NativeCatalogDeliveryException>(() =>
            backend.DeliverNativeCatalogAsync(target, TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult());

        Assert.Contains("地址表未送达", ex.Message);
    }

    [Fact]
    public void DeliverNativeCatalog_Throws_PropagatesAsNativeCatalogDeliveryException()
    {
        // SetNativeCatalogAsync throws an unexpected exception.
        var client = new FakeAgentClient
        {
            SetNativeCatalogException = new InvalidOperationException("pipe broken")
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        var target = CreateRa3112Target();
        backend.AttachAsync(target, CreateMinimalManifest(), "RayaTrainer.Agent.dll", TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();

        var ex = Assert.Throws<NativeCatalogDeliveryException>(() =>
            backend.DeliverNativeCatalogAsync(target, TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult());
    }

    [Fact]
    public void DeliverNativeCatalog_NonOkStatus_ThrowsNativeCatalogDeliveryException()
    {
        // SetNativeCatalogAsync returns a non-Ok status.
        var client = new FakeAgentClient
        {
            SetNativeCatalogResult = new AgentCommandResultPayload(
                AgentStatusCode.InvalidCommand, AgentProtocol.Version, 0)
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        var target = CreateRa3112Target();
        backend.AttachAsync(target, CreateMinimalManifest(), "RayaTrainer.Agent.dll", TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();

        var ex = Assert.Throws<NativeCatalogDeliveryException>(() =>
            backend.DeliverNativeCatalogAsync(target, TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult());
    }

    [Fact]
    public void InstallPatchesAsync_BeforeCatalog_ThrowsInvalidOperationException()
    {
        var client = new FakeAgentClient();
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        var target = CreateRa3112Target();
        var manifest = CreateMinimalManifest();
        backend.AttachAsync(target, manifest, "RayaTrainer.Agent.dll", TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();

        // Do NOT deliver catalog — InstallPatchesAsync must refuse.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            backend.InstallPatchesAsync(manifest, target, TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult());

        Assert.Contains("NATIVE_CATALOG_PENDING", ex.Message);
    }

    [Fact]
    public void NativeCatalogDeliveryException_IsAgentCompatibilityException()
    {
        var ex = new NativeCatalogDeliveryException("test");
        Assert.IsAssignableFrom<AgentCompatibilityException>(ex);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public void DiagnosticEvent_EmittedOnDeliveryFailure()
    {
        var client = new FakeAgentClient
        {
            SetNativeCatalogDelay = TimeSpan.FromSeconds(10)
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);
        backend.CatalogDeliveryTimeout = TimeSpan.FromMilliseconds(50);

        var target = CreateRa3112Target();
        backend.AttachAsync(target, CreateMinimalManifest(), "RayaTrainer.Agent.dll", TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();

        // TrainerDiagnosticState captures the failure when the exception is handled.
        var diagnosticState = new TrainerDiagnosticState();

        try
        {
            backend.DeliverNativeCatalogAsync(target, TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();
        }
        catch (NativeCatalogDeliveryException ex)
        {
            diagnosticState.CaptureNativeCatalogDeliveryFailure(ex);
        }

        var events = diagnosticState.Events;
        var deliveryFailedEvent = events.FirstOrDefault(e => e.Code == "agent.native_catalog_delivery_failed");
        Assert.NotNull(deliveryFailedEvent);
        Assert.Equal(DiagnosticEventSeverity.Error, deliveryFailedEvent.Severity);
    }
}
