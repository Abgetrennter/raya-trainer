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
/// Golden-master characterization tests for <see cref="InjectedAgentBackend"/>.
/// Pins AttachAsync and InstallPatchesAsync behavior before P1.2/P1.3 refactors.
/// </summary>
[Trait("Category", "Characterization")]
public sealed class InjectedBackendCharacterizationTests
{
    private static readonly TrainerManifest Manifest = TestAssets.LoadManifest();

    private static TrainerTarget Ra3112Target(int? processId = 1234, bool signatureCompatibility = false) => new(
        "ra3_1.12.game", 0x400000, Is32Bit: true, VersionSupported: true,
        ProcessId: processId, VersionProfileId: "ra3_1.12",
        SignatureCompatibilityMode: signatureCompatibility);

    private static TrainerTarget UprisingTarget(int? processId = 1234) => new(
        "ra3ep1_1.0.game", 0x400000, Is32Bit: true, VersionSupported: true,
        ProcessId: processId, FileVersion: "1.0.3313.38400",
        VersionProfileId: Ra3VersionProfileRegistry.Uprising10.Id);

    #region AttachAsync

    [Fact]
    public async Task AttachAsync_HappyPath_InjectsAndConnects()
    {
        var client = new FakeBackendClient();
        var injector = new FakeAgentInjector();
        var backend = new InjectedAgentBackend(injector, client);

        var status = await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));

        Assert.True(backend.IsConnected);
        Assert.False(backend.ReusedExistingAgent);
        Assert.Equal(1234, backend.TargetProcessId);
        Assert.True(injector.InjectCalled);
        Assert.NotNull(backend.LastStatus);
        Assert.Equal(AgentStatusCode.Ok, status.StatusCode);
    }

    [Fact]
    public async Task AttachAsync_WithExistingAgent_ReusesIt()
    {
        var client = new FakeBackendClient { PingFailuresRemaining = 0 }; // Ping succeeds immediately
        var injector = new FakeAgentInjector();
        var backend = new InjectedAgentBackend(injector, client);

        var status = await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));

        Assert.True(backend.IsConnected);
        Assert.True(backend.ReusedExistingAgent);
        Assert.False(injector.InjectCalled); // No injection needed
    }

    [Fact]
    public async Task AttachAsync_VersionMismatch_ThrowsAgentCompatibilityException()
    {
        var client = new FakeBackendClient
        {
            PingFailuresRemaining = 0,
            PingAgentVersion = AgentProtocol.Version - 1, // Wrong version
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        var ex = await Assert.ThrowsAsync<AgentCompatibilityException>(() =>
            backend.AttachAsync(
                Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
                TimeSpan.FromSeconds(5)));

        Assert.Contains("protocol", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(backend.IsConnected);
    }

    [Fact]
    public async Task AttachAsync_VersionMismatchFromPing_ThrowsAgentCompatibilityException()
    {
        var client = new FakeBackendClient
        {
            PingFailuresRemaining = 0,
            PingStatusCode = AgentStatusCode.VersionMismatch,
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.AttachAsync(
                Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
                TimeSpan.FromSeconds(5)));

        Assert.Contains("Agent Ping failed", ex.Message, StringComparison.Ordinal);
        Assert.False(backend.IsConnected);
    }

    [Fact]
    public async Task AttachAsync_MissingNativeRuntimeCapabilities_ThrowsAgentCompatibilityException()
    {
        var client = new FakeBackendClient
        {
            PingFailuresRemaining = 0,
            PingCapabilities = 0u, // No capabilities at all
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        var ex = await Assert.ThrowsAsync<AgentCompatibilityException>(() =>
            backend.AttachAsync(
                Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
                TimeSpan.FromSeconds(5)));

        Assert.Contains("Native runtime capability", ex.Message, StringComparison.Ordinal);
        Assert.False(backend.IsConnected);
    }

    [Fact]
    public async Task AttachAsync_IncompatiblePingPayload_ThrowsFromPing()
    {
        // Simulate a ping that throws InvalidDataException after injection
        var client = new FakeBackendClient
        {
            PingFailuresRemaining = 1, // First ping fails → inject
            PingFailuresRemainingAfterInject = 0, // After inject, ping throws InvalidDataException
            ThrowInvalidDataOnPingAfterInject = true,
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        var ex = await Assert.ThrowsAsync<AgentCompatibilityException>(() =>
            backend.AttachAsync(
                Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
                TimeSpan.FromSeconds(5)));

        Assert.Contains("不匹配", ex.Message, StringComparison.Ordinal);
        Assert.False(backend.IsConnected);
    }

    [Fact]
    public async Task AttachAsync_WithSignatureCompatAndExistingHooks_Throws()
    {
        // Signature compatibility mode requires Is32Bit=true, SupportsSignatureScanning profile,
        // matching process name, matching file version family, and ReusedExistingAgent with
        // existing hooks. The ra3_1.12 profile supports all these.
        var client = new FakeBackendClient
        {
            PingFailuresRemaining = 0, // Ping succeeds → ReusedExistingAgent = true
            StatusInstalledHookCount = 5, // Already has hooks installed
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);
        var target = new TrainerTarget(
            Ra3VersionProfileRegistry.Ra3112.ProcessName,
            0x400000, Is32Bit: true, VersionSupported: true,
            ProcessId: 1234, FileVersion: "1.12.0.0",
            VersionProfileId: Ra3VersionProfileRegistry.Ra3112.Id,
            SignatureCompatibilityMode: true);

        var ex = await Assert.ThrowsAsync<AgentCompatibilityException>(() =>
            backend.AttachAsync(
                target, Manifest, "C:/agent/RayaTrainer.Agent.dll",
                TimeSpan.FromSeconds(5)));

        Assert.Contains("已经安装的 Hook", ex.Message, StringComparison.Ordinal);
        Assert.False(backend.IsConnected);
    }

    #endregion

    #region AttachAsync error paths

    [Fact]
    public async Task AttachAsync_WithoutProcessId_ThrowsInvalidOperation()
    {
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), new FakeBackendClient());
        var target = Ra3112Target(processId: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.AttachAsync(
                target, Manifest, "C:/agent/RayaTrainer.Agent.dll",
                TimeSpan.FromSeconds(5)));

        Assert.Contains("无法确定目标进程 PID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachAsync_WithUnsupportedProfile_ThrowsInvalidOperation()
    {
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), new FakeBackendClient());
        // Use a made-up profile ID that won't resolve
        var target = new TrainerTarget(
            "unknown.game", 0x400000, Is32Bit: true, VersionSupported: true,
            ProcessId: 1234, VersionProfileId: "nonexistent");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.AttachAsync(
                target, Manifest, "C:/agent/RayaTrainer.Agent.dll",
                TimeSpan.FromSeconds(5)));

        Assert.Contains("无法确认目标版本配置", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region InstallPatchesAsync

    [Fact]
    public async Task InstallPatchesAsync_HappyPath_ReturnsOk()
    {
        var client = new FakeBackendClient { PingFailuresRemaining = 0 };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));
        await backend.DeliverNativeCatalogAsync(
            Ra3112Target(), TimeSpan.FromSeconds(5));

        var result = await backend.InstallPatchesAsync(
            Manifest, Ra3112Target(), TimeSpan.FromSeconds(5));

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.NotNull(client.InstallRequest);
        Assert.NotEmpty(backend.InstalledNativeHookIds);
        Assert.NotEmpty(backend.PatchSetsRegistered);
    }

    [Fact]
    public async Task InstallPatchesAsync_PatchSetBaselineMismatch_SkipsOnlyPatchSet()
    {
        var client = new FakeBackendClient
        {
            PingFailuresRemaining = 0,
            MismatchFrameRatePatchSetBaseline = true
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));
        await backend.DeliverNativeCatalogAsync(
            Ra3112Target(), TimeSpan.FromSeconds(5));

        var result = await backend.InstallPatchesAsync(
            Manifest, Ra3112Target(), TimeSpan.FromSeconds(5));

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.NotNull(client.InstallRequest);
        Assert.Empty(client.InstallRequest.PatchSets);
        Assert.NotEmpty(client.InstallRequest.Hooks);
        Assert.Empty(backend.PatchSetsRegistered);
        Assert.Equal([(uint)NativeRuntimePatchSetId.FrameRateUnlock], backend.SkippedPatchSetIds);
    }

    [Fact]
    public async Task InstallPatchesAsync_RelocatedExactProfileHook_UsesAttestedLiveBytes()
    {
        const uint steamHookAddress = 0x665555;
        byte[] steamHookBytes = [0xE8, 0xE6, 0x3A, 0xE7, 0xFF];
        var client = new FakeBackendClient
        {
            PingFailuresRemaining = 0,
            SignatureScanPayload = TestAgentSignatureCatalog.CreateRa3112(
                overrides: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
                {
                    ["_BackLogicTimeFreezeGate"] = steamHookAddress
                })
        };
        client.MemoryByAddress[steamHookAddress] = steamHookBytes;
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));
        await backend.DeliverNativeCatalogAsync(
            Ra3112Target(), TimeSpan.FromSeconds(5));

        var result = await backend.InstallPatchesAsync(
            Manifest, Ra3112Target(), TimeSpan.FromSeconds(5));

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        var hook = Assert.Single(client.InstallRequest!.Hooks, item => item.NativeHookId == 44);
        Assert.Equal(steamHookAddress, hook.Address);
        Assert.Equal(steamHookBytes, hook.OriginalBytes);
    }

    [Fact]
    public async Task InstallPatchesAsync_BeforeCatalogDelivered_ThrowsInvalidOperation()
    {
        var client = new FakeBackendClient { PingFailuresRemaining = 0 };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.InstallPatchesAsync(
                Manifest, Ra3112Target(), TimeSpan.FromSeconds(5)));

        Assert.Contains("NATIVE_CATALOG_PENDING", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallPatchesAsync_BeforeAttach_ThrowsInvalidOperation()
    {
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), new FakeBackendClient());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.InstallPatchesAsync(
                Manifest, Ra3112Target(), TimeSpan.FromSeconds(5)));

        Assert.Contains("NATIVE_CATALOG_PENDING", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallPatchesAsync_WithPatchMismatch_Throws()
    {
        var client = new FakeBackendClient
        {
            PingFailuresRemaining = 0,
            InstallResultStatusCode = AgentStatusCode.PatchMismatch,
        };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));
        await backend.DeliverNativeCatalogAsync(
            Ra3112Target(), TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.InstallPatchesAsync(
                Manifest, Ra3112Target(), TimeSpan.FromSeconds(5)));

        Assert.Contains("Patch", ex.Message, StringComparison.Ordinal);
        // After mismatch, diagnostics should be fetched
        Assert.NotNull(backend.LastMismatchDiagnostic);
    }

    #endregion

    #region DeliverNativeCatalogAsync

    [Fact]
    public async Task DeliverNativeCatalogAsync_HappyPath_SetsFlag()
    {
        var client = new FakeBackendClient { PingFailuresRemaining = 0 };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);
        await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));

        await backend.DeliverNativeCatalogAsync(
            Ra3112Target(), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DeliverNativeCatalogAsync_BeforeAttach_ThrowsNativeCatalogDeliveryException()
    {
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), new FakeBackendClient());

        var ex = await Assert.ThrowsAsync<NativeCatalogDeliveryException>(() =>
            backend.DeliverNativeCatalogAsync(
                Ra3112Target(), TimeSpan.FromSeconds(5)));

        Assert.Contains("尚未连接", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region RestorePatchesAsync

    [Fact]
    public async Task RestorePatchesAsync_AfterAttachAndInstall_ReturnsOk()
    {
        var client = new FakeBackendClient { PingFailuresRemaining = 0 };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);
        await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));
        await backend.DeliverNativeCatalogAsync(
            Ra3112Target(), TimeSpan.FromSeconds(5));
        await backend.InstallPatchesAsync(
            Manifest, Ra3112Target(), TimeSpan.FromSeconds(5));

        var result = await backend.RestorePatchesAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.True(client.RestoreCalled);
    }

    [Fact]
    public async Task RestorePatchesAsync_BeforeAttach_Throws()
    {
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), new FakeBackendClient());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.RestorePatchesAsync(TimeSpan.FromSeconds(5)));

        Assert.Contains("尚未连接", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region CreateFeatureController

    [Fact]
    public async Task CreateFeatureController_ReturnsControllerWithDirectGameApi()
    {
        var client = new FakeBackendClient { PingFailuresRemaining = 0 };
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), client);

        // Go through AttachAsync so _supportsDirectGameApi is properly set
        await backend.AttachAsync(
            Ra3112Target(), Manifest, "C:/agent/RayaTrainer.Agent.dll",
            TimeSpan.FromSeconds(5));

        var status = new AgentStatusPayload(
            AgentStatusCode.Ok, AgentProtocol.Version, 1234, 0x400000, 24);
        var controller = backend.CreateFeatureController(status);

        Assert.NotNull(controller);
        Assert.True(controller is IAgentFeatureController { SupportsDirectGameApi: true });
    }

    [Fact]
    public void CreateFeatureController_WithoutProcessId_Throws()
    {
        var backend = new InjectedAgentBackend(new FakeAgentInjector(), new FakeBackendClient());
        var status = new AgentStatusPayload(
            AgentStatusCode.Ok, AgentProtocol.Version, 1234, 0x400000, 24);

        Assert.Throws<InvalidOperationException>(() =>
            backend.CreateFeatureController(status));
    }

    #endregion

    /// <summary>
    /// Fake IAgentClient for InjectedAgentBackend characterization tests.
    /// Supports configurable error conditions for pinning error paths.
    /// </summary>
    private sealed class FakeBackendClient : IAgentClient
    {
        public int PingFailuresRemaining { get; set; } = 1;
        public int PingFailuresRemainingAfterInject { get; set; } = 0;
        public bool ThrowInvalidDataOnPingAfterInject { get; set; }
        public ushort PingAgentVersion { get; set; } = AgentProtocol.Version;
        public ulong PingFingerprint { get; set; } = AgentBuildIdentity.Fingerprint;
        public AgentStatusCode PingStatusCode { get; set; } = AgentStatusCode.Ok;
        public uint PingCapabilities { get; set; } = (uint)NativeRuntimeCapabilities.Required;
        public uint StatusInstalledHookCount { get; set; }
        public AgentStatusCode InstallResultStatusCode { get; set; } = AgentStatusCode.Ok;
        public AgentInstallPatchesRequest? InstallRequest { get; private set; }
        public bool MismatchFrameRatePatchSetBaseline { get; set; }
        public Dictionary<uint, byte[]> MemoryByAddress { get; } = [];
        public bool RestoreCalled { get; private set; }
        public bool RestoreFails { get; set; }
        public AgentSignatureScanPayload SignatureScanPayload { get; set; } =
            TestAgentSignatureCatalog.CreateRa3112();

        public Task<AgentPingPayload> PingAsync(int processId, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (PingFailuresRemaining-- > 0)
                throw new TimeoutException("no existing agent");

            if (ThrowInvalidDataOnPingAfterInject)
                throw new InvalidDataException("Agent protocol version mismatch");

            return Task.FromResult(new AgentPingPayload(
                PingStatusCode, PingAgentVersion, processId,
                0x400000, PingCapabilities, PingFingerprint));
        }

        public Task<AgentStatusPayload> GetStatusAsync(int processId, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentStatusPayload(
                PingStatusCode, PingAgentVersion, processId,
                0x400000, StatusInstalledHookCount, PingCapabilities));

        public Task<AgentCommandResultPayload> InstallPatchesAsync(int processId,
            AgentInstallPatchesRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            InstallRequest = request;
            return Task.FromResult(new AgentCommandResultPayload(
                InstallResultStatusCode, AgentProtocol.Version,
                checked((uint)request.Hooks.Count)));
        }

        public Task<AgentCommandResultPayload> RestorePatchesAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            RestoreCalled = true;
            if (RestoreFails)
                return Task.FromResult(new AgentCommandResultPayload(
                    AgentStatusCode.PatchMismatch, AgentProtocol.Version, 0));
            return Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

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
            CancellationToken cancellationToken = default)
        {
            var bytes = MemoryByAddress.TryGetValue(request.Address, out var configured)
                ? configured.ToArray()
                : ResolvePatchSetBaseline(request);
            if (MismatchFrameRatePatchSetBaseline &&
                request.Address == 0x400000u + 0x8AD5F4u)
            {
                bytes = BitConverter.GetBytes(11);
            }

            return Task.FromResult(new AgentMemoryReadPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, request.Address, bytes));
        }

        private static byte[] ResolvePatchSetBaseline(AgentMemoryReadRequest request)
        {
            return TestAssets.ResolveRuntimePatchSetDisableBytes(request.Address, request.ByteCount)
                ?? new byte[request.ByteCount];
        }

        public Task<AgentCommandResultPayload> SetNativeCatalogAsync(int processId,
            IReadOnlyList<uint> rvas, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentCommandResultPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0));

        public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentMismatchDiagnosticsPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, 0,
                new byte[] { 1 }, new byte[] { 0x90 }, new byte[] { 0x90 },
                MismatchKind.Hook, 0x123456));

        public Task<AgentSignatureScanPayload> ScanSignaturesAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(SignatureScanPayload);

        public Task<AgentGameModePayload> GetGameModeAsync(int processId,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentGameModePayload(
                AgentStatusCode.Ok, AgentProtocol.Version,
                GameRuntimeConstants.GameModeShell));

        // GameApi methods — not needed for backend tests
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
