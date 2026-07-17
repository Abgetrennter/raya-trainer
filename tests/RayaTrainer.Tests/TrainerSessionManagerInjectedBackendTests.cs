using RayaTrainer.App.Services;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerSessionManagerInjectedBackendTests
{
    [Fact]
    public void DevelopmentRunPrefersFreshAppLocalDllOverArtifactWhenNewer()
    {
        // build-and-run.ps1 copies the just-built DLL next to the App output; that app-local
        // copy is the freshest source and must win over any stale artifact (the previous logic
        // hard-coded Release and could load a stale v6 DLL while the App was on v7, surfacing
        // as a protocol-mismatch error at injection time).
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var appDirectory = Path.Combine(root, "src", "RayaTrainer.App", "bin", "Debug");
        var releaseArtifact = Path.Combine(root, "artifacts", "native", "Release", "Win32", "RayaTrainer.Agent.dll");
        var appLocalPath = Path.Combine(appDirectory, "RayaTrainer.Agent.dll");

        try
        {
            Directory.CreateDirectory(appDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(releaseArtifact)!);
            File.WriteAllText(Path.Combine(root, "RayaTrainer.sln"), string.Empty);
            File.WriteAllText(releaseArtifact, "stale-release");
            File.SetLastWriteTimeUtc(releaseArtifact, new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc));
            File.WriteAllText(appLocalPath, "fresh-app-local");
            File.SetLastWriteTimeUtc(appLocalPath, new DateTime(2026, 7, 3, 5, 0, 0, DateTimeKind.Utc));

            var resolved = TrainerSessionManager.ResolveDefaultAgentDllPath(appDirectory);

            Assert.Equal(appLocalPath, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DevelopmentRunPrefersNewestArtifactWhenAppLocalAbsent()
    {
        // When the App output has no DLL (developer built the DLL via MSBuild directly without
        // copy), fall back to the newest artifact across Debug/Release instead of hard-coding
        // Release — that would load a stale DLL whenever the active config is Debug.
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var appDirectory = Path.Combine(root, "src", "RayaTrainer.App", "bin", "Debug");
        var debugArtifact = Path.Combine(root, "artifacts", "native", "Debug", "Win32", "RayaTrainer.Agent.dll");
        var releaseArtifact = Path.Combine(root, "artifacts", "native", "Release", "Win32", "RayaTrainer.Agent.dll");

        try
        {
            Directory.CreateDirectory(appDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(debugArtifact)!);
            Directory.CreateDirectory(Path.GetDirectoryName(releaseArtifact)!);
            File.WriteAllText(Path.Combine(root, "RayaTrainer.sln"), string.Empty);
            File.WriteAllText(debugArtifact, "fresh-debug");
            File.SetLastWriteTimeUtc(debugArtifact, new DateTime(2026, 7, 3, 5, 0, 0, DateTimeKind.Utc));
            File.WriteAllText(releaseArtifact, "stale-release");
            File.SetLastWriteTimeUtc(releaseArtifact, new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc));

            var resolved = TrainerSessionManager.ResolveDefaultAgentDllPath(appDirectory);

            Assert.Equal(debugArtifact, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackagedRunUsesAppLocalAgentDll()
    {
        var appDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var appLocalPath = Path.Combine(appDirectory, "RayaTrainer.Agent.dll");

        try
        {
            Directory.CreateDirectory(appDirectory);
            File.WriteAllText(appLocalPath, "packaged");

            var resolved = TrainerSessionManager.ResolveDefaultAgentDllPath(appDirectory);

            Assert.Equal(appLocalPath, resolved);
        }
        finally
        {
            Directory.Delete(appDirectory, recursive: true);
        }
    }

    [Fact]
    public void TrainerSessionManagerExposesViewModelFreeServiceBoundary()
    {
        var root = RepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Services", "ITrainerSessionService.cs"));
        var managerSource = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Services", "TrainerSessionManager.cs"));

        Assert.Contains("public interface ITrainerSessionService", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FeatureGroupViewModel", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RayaTrainer.App.ViewModels", serviceSource, StringComparison.Ordinal);
        Assert.Contains(
            "public sealed class TrainerSessionManager : ITrainerSessionService, ITrainerDiagnosticsSource, IDisposable",
            managerSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("using RayaTrainer.App.ViewModels;", managerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FeatureGroupViewModel", managerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectedAgentBackendInstallsPatchesWithAgentFeatureController()
    {
        var agentClient = new FakeAgentClient();
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), agentClient),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var productionQueueAction = TrainerFeatureCatalog.CreateGridFeatures(manifest.Features)
            .Single(feature => feature.RawName == "Expand Production Queue");
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234);

        var disconnectedCapability = manager.GetFeatureCapability(productionQueueAction);
        Assert.Equal(FeatureCapabilityState.Waiting, disconnectedCapability.State);
        Assert.Equal("NO_TARGET", disconnectedCapability.ReasonCode);

        var attach = manager.AttachTarget(manifest, target);
        var install = manager.InstallPatches(manifest, "diagnostics");

        Assert.True(attach.Success);
        Assert.Contains("DLL Agent", attach.Message);
        Assert.True(manager.CanUseFeatures);
        Assert.True(manager.ArePatchesInstalled);
        Assert.NotNull(manager.FeatureController);
        Assert.NotNull(agentClient.InstallRequest);
        Assert.Equal(TestAssets.CurrentStandardHookCount, agentClient.InstallRequest.Hooks.Count);
        Assert.Equal(TestAssets.CurrentStandardHookCount, install.PatchResult.InstallResult.InstalledHookCount);
        Assert.Contains("DLL Agent", install.StatusMessage);
        var capability = manager.GetFeatureCapability(productionQueueAction);
        Assert.Equal(FeatureCapabilityState.Ready, capability.State);
        Assert.Equal("READY", capability.ReasonCode);

        var snapshot = manager.GetDiagnosticSnapshot([productionQueueAction]);
        Assert.Equal(TrainerDiagnosticHealth.Healthy, snapshot.Health);
        Assert.Equal(AgentProtocol.Version, snapshot.Agent.AgentVersion);
        Assert.Equal(TestAssets.CurrentStandardHookCount, snapshot.Patch.InstalledHookCount);
        Assert.Contains(snapshot.Stages, stage => stage.Id == "signature" && stage.State == DiagnosticStageState.Healthy);
    }

    [Fact]
    public void SteamLayoutRegistersFrameRateUnlockPatchSetAndKeepsHooks()
    {
        var signatureScan = TestAgentSignatureCatalog.CreateRa3112(
            overrides: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                ["_BackFrameRateUnlockGameUpdate"] = 0x665560,
                ["_BackLogicTimeFreezeGate"] = 0x665555
            });
        var steamDefinitions = RuntimePatchSetCatalog.ResolveForTarget(
            "ra3_1.12",
            0x400000,
            signatureScan.Addresses,
            signatureCompatibilityMode: false);
        var agentClient = new FakeAgentClient
        {
            SignatureScanPayload = signatureScan,
            PatchSetDefinitions = steamDefinitions
        };
        agentClient.MemoryByAddress[0x665560] = [0x8B, 0x01, 0x8B, 0x90, 0x0C, 0x01, 0x00, 0x00];
        agentClient.MemoryByAddress[0x665555] = [0xE8, 0xE6, 0x3A, 0xE7, 0xFF];
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), agentClient),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234,
            FileVersion: GameTarget.ExpectedVersion,
            VersionProfileId: "ra3_1.12");

        manager.AttachTarget(manifest, target);
        var install = manager.InstallPatches(manifest, "diagnostics");

        Assert.True(manager.ArePatchesInstalled);
        Assert.NotNull(agentClient.InstallRequest);
        var patchSet = Assert.Single(agentClient.InstallRequest.PatchSets);
        Assert.Equal(29, patchSet.Entries.Count);
        Assert.Contains(patchSet.Entries, entry => entry.Address == 0xCB4098);
        Assert.Contains(patchSet.Entries, entry => entry.Address == 0xBC6420);
        Assert.Contains(patchSet.Entries, entry => entry.Address == 0x71F457);
        Assert.Equal(TestAssets.CurrentStandardHookCount, agentClient.InstallRequest.Hooks.Count);
        Assert.DoesNotContain("安全禁用", install.StatusMessage, StringComparison.Ordinal);

        var frameRateUnlock = TrainerFeatureCatalog.CreateGridFeatures(manifest.Features)
            .Single(feature => feature.RawName == TrainerFeatureIds.FrameRateUnlock60fps);
        var capability = manager.GetFeatureCapability(frameRateUnlock);
        Assert.Equal(FeatureCapabilityState.Ready, capability.State);
    }

    [Fact]
    public void InjectedAgentBackendAcceptsUprising10Profile()
    {
        var agentClient = new FakeAgentClient
        {
            SignatureScanPayload = TestAgentSignatureCatalog.CreateForProfile(
                Ra3VersionProfileRegistry.Uprising10)
        };
        var injector = new FakeAgentInjector();
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(injector, agentClient),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var target = new TrainerTarget(
            "ra3ep1_1.0.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234,
            FileVersion: "1.0.3313.38400",
            VersionProfileId: Ra3VersionProfileRegistry.Uprising10.Id);

        var result = manager.AttachTarget(TestAssets.LoadManifest(), target);

        Assert.True(result.Success);
        Assert.Contains("DLL Agent", result.Message);
        Assert.True(injector.InjectCalled);
        Assert.Equal(1234, manager.TargetProcessId);
        Assert.True(manager.CanUseFeatures);

        var snapshot = manager.GetDiagnosticSnapshot([]);
        var profile = Ra3VersionProfileRegistry.Uprising10;
        var expectedEntryCount = profile.Hooks.Count;
        var expectedMatchedCount = profile.Hooks.Values
            .Count(entry => entry.Status == AddressSupportStatus.Verified && entry.Rva is not null);
        Assert.Equal((uint)expectedEntryCount, snapshot.Signatures.EntryCount);
        Assert.Equal((uint)expectedMatchedCount, snapshot.Signatures.MatchedCount);
        Assert.Empty(snapshot.Signatures.RequiredUnresolved);
        Assert.Equal(expectedEntryCount - expectedMatchedCount, snapshot.Signatures.OptionalUnresolved.Count);
        Assert.Equal(profile.SupersededHooks.Order(), snapshot.Signatures.SupersededSymbols.Order());
        Assert.DoesNotContain(snapshot.Stages, stage => stage.Id == "signature" && stage.State == DiagnosticStageState.Error);
    }

    [Fact]
    public void RequiredSignatureMissProducesErrorDiagnostics()
    {
        var profile = Ra3VersionProfileRegistry.Ra3112;
        var requiredSymbol = profile.Hooks.Keys
            .First(symbol => !profile.OptionalSignatureSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase));
        var client = new FakeAgentClient
        {
            SignatureScanPayload = TestAgentSignatureCatalog.CreateRa3112(
                overrides: new Dictionary<string, uint> { [requiredSymbol] = 0 })
        };
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), client),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var target = new TrainerTarget(
            profile.ProcessName,
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234,
            VersionProfileId: profile.Id);

        var attach = manager.AttachTarget(TestAssets.LoadManifest(), target);
        Assert.True(attach.Success); // no longer throws; deferred to InstallPatches PatchMismatch

        var snapshot = manager.GetDiagnosticSnapshot([]);
        Assert.Equal(TrainerDiagnosticHealth.Error, snapshot.Health);
        Assert.Contains(requiredSymbol, snapshot.Signatures.RequiredUnresolved);
        Assert.Contains(snapshot.RecentEvents, diagnosticEvent =>
            diagnosticEvent.Code == "agent.signature_scan" &&
            diagnosticEvent.Severity == DiagnosticEventSeverity.Error);
    }

    [Theory]
    [InlineData("ra3_1.12", "1.12.9999.0")]
    [InlineData("ra3_1.13", "1.13.9999.0")]
    [InlineData("ra3_uprising_1.0", "1.0.9999.0")]
    [InlineData("ra3_uprising_1.1", "1.1.9999.0")]
    public void SignatureCompatibilityCandidateAttestsRelocatedLayoutBeforeInstalling(
        string profileId,
        string candidateVersion)
    {
        var profile = Ra3VersionProfileRegistry.FindById(profileId)!;
        var manifest = TestAssets.LoadManifest();
        var signatureScan = TestAgentSignatureCatalog.CreateForProfile(
            profile,
            includeNativeRefs: true,
            addressShift: 0x1000);
        var client = new FakeAgentClient { SignatureScanPayload = signatureScan };
        foreach (var plan in PatchHookPlanner.CreateSupportedPlans(
                     manifest.PatchManifest,
                     profile,
                     includeUnlistedHooks: false).Plans)
        {
            var key = string.IsNullOrWhiteSpace(plan.ReturnLabel) ? plan.Address : plan.ReturnLabel;
            client.MemoryByAddress[signatureScan.Addresses[key]] = plan.OriginalBytes;
        }

        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), client),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var target = CompatibilityTarget(profile, candidateVersion);

        var attach = manager.AttachTarget(manifest, target);
        var install = manager.InstallPatches(manifest, "diagnostics");

        Assert.True(attach.Success);
        Assert.Contains("签名兼容校验通过", attach.Message, StringComparison.Ordinal);
        Assert.NotNull(client.InstallRequest);
        Assert.All(client.InstallRequest.Hooks, hook =>
            Assert.Contains(hook.Address, signatureScan.Addresses.Values));
        Assert.Equal(client.InstallRequest.Hooks.Count, install.PatchResult.InstallResult.InstalledHookCount);
    }

    [Fact]
    public void SignatureCompatibilityCandidateRejectsMissingRequiredSignatureBeforePatchInstall()
    {
        var profile = Ra3VersionProfileRegistry.Ra3112;
        var addresses = new Dictionary<string, uint>(
            TestAgentSignatureCatalog.CreateForProfile(profile, includeNativeRefs: true).Addresses,
            StringComparer.OrdinalIgnoreCase);
        var requiredHook = profile.Hooks.First(entry => entry.Value.Status == AddressSupportStatus.Verified).Key;
        addresses[requiredHook] = 0;
        var client = new FakeAgentClient
        {
            SignatureScanPayload = new AgentSignatureScanPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                checked((uint)addresses.Count),
                checked((uint)addresses.Count(entry => entry.Value != 0)),
                addresses)
        };
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), client),
            () => "C:/agent/RayaTrainer.Agent.dll");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.AttachTarget(TestAssets.LoadManifest(), CompatibilityTarget(profile)));

        Assert.Contains(requiredHook, exception.Message, StringComparison.Ordinal);
        Assert.Contains("未安装任何 Patch", exception.Message, StringComparison.Ordinal);
        Assert.Null(client.InstallRequest);
    }

    [Fact]
    public void SignatureCompatibilityCandidateRejectsLayoutDriftBeforePatchInstall()
    {
        var profile = Ra3VersionProfileRegistry.Ra3112;
        var manifest = TestAssets.LoadManifest();
        var signatureScan = TestAgentSignatureCatalog.CreateForProfile(profile, includeNativeRefs: true);
        var client = new FakeAgentClient { SignatureScanPayload = signatureScan };
        var plans = PatchHookPlanner.CreateSupportedPlans(
            manifest.PatchManifest,
            profile,
            includeUnlistedHooks: false).Plans;
        foreach (var plan in plans)
        {
            var key = string.IsNullOrWhiteSpace(plan.ReturnLabel) ? plan.Address : plan.ReturnLabel;
            client.MemoryByAddress[signatureScan.Addresses[key]] = plan.OriginalBytes.ToArray();
        }
        var changedPlan = plans[0];
        var changedKey = string.IsNullOrWhiteSpace(changedPlan.ReturnLabel)
            ? changedPlan.Address
            : changedPlan.ReturnLabel;
        client.MemoryByAddress[signatureScan.Addresses[changedKey]][0] = 0xCC;

        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), client),
            () => "C:/agent/RayaTrainer.Agent.dll");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.AttachTarget(manifest, CompatibilityTarget(profile)));

        Assert.Contains("代码布局已变化", exception.Message, StringComparison.Ordinal);
        Assert.Contains("未安装任何 Patch", exception.Message, StringComparison.Ordinal);
        Assert.Null(client.InstallRequest);
    }

    [Fact]
    public void SignatureCompatibilityCandidateRejectsHookAddressOutsideTargetModule()
    {
        var profile = Ra3VersionProfileRegistry.Ra3112;
        var addresses = new Dictionary<string, uint>(
            TestAgentSignatureCatalog.CreateForProfile(profile, includeNativeRefs: true).Addresses,
            StringComparer.OrdinalIgnoreCase);
        var requiredHook = profile.Hooks.First(entry => entry.Value.Status == AddressSupportStatus.Verified).Key;
        addresses[requiredHook] = 0x300000;
        var client = new FakeAgentClient
        {
            SignatureScanPayload = new AgentSignatureScanPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                checked((uint)addresses.Count),
                checked((uint)addresses.Count(entry => entry.Value != 0)),
                addresses)
        };
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), client),
            () => "C:/agent/RayaTrainer.Agent.dll");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.AttachTarget(TestAssets.LoadManifest(), CompatibilityTarget(profile)));

        Assert.Contains("超出目标模块", exception.Message, StringComparison.Ordinal);
        Assert.Null(client.InstallRequest);
    }

    [Fact]
    public void SignatureCompatibilityCandidateRejectsReusedInstalledAgentBecauseBytesAreNotPristine()
    {
        var profile = Ra3VersionProfileRegistry.Ra3112;
        var client = new FakeAgentClient
        {
            PingFailuresRemaining = 0,
            StatusInstalledHookCount = 1
        };
        var injector = new FakeAgentInjector();
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(injector, client),
            () => "C:/agent/RayaTrainer.Agent.dll");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.AttachTarget(TestAssets.LoadManifest(), CompatibilityTarget(profile)));

        Assert.Contains("原始指令", exception.Message, StringComparison.Ordinal);
        Assert.Contains("重启游戏", exception.Message, StringComparison.Ordinal);
        Assert.False(injector.InjectCalled);
        Assert.Null(client.InstallRequest);
    }

    private static TrainerTarget CompatibilityTarget(
        Ra3VersionProfile profile,
        string candidateVersion = "1.12.9999.99999")
    {
        return new TrainerTarget(
            profile.ProcessName,
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234,
            FileVersion: candidateVersion,
            VersionProfileId: profile.Id,
            SignatureCompatibilityMode: true);
    }

    [Fact]
    public async Task RuntimeReadFailureIsAttentionAndRecoveryReturnsHealthy()
    {
        var client = new FakeAgentClient { FailRuntimeReads = true };
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), client),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234);

        manager.AttachTarget(manifest, target);
        manager.InstallPatches(manifest, "diagnostics");

        var failed = manager.GetDiagnosticSnapshot([]);
        Assert.Equal(TrainerDiagnosticHealth.Attention, failed.Health);
        Assert.Contains(failed.RecentEvents, item =>
            item.Code == "runtime.refresh_failed" && item.Severity == DiagnosticEventSeverity.Warning);

        client.FailRuntimeReads = false;
        var recovered = await manager.RefreshDiagnosticsAsync([]);

        Assert.Equal(TrainerDiagnosticHealth.Healthy, recovered.Health);
        Assert.Contains(recovered.RecentEvents, item => item.Code == "runtime.refresh_recovered");
    }

    [Fact]
    public void InjectedAgentBackendInstallsEveryRa3113Hook()
    {
        var profile = Ra3VersionProfileRegistry.Ra3113;
        var agentClient = new FakeAgentClient
        {
            SignatureScanPayload = TestAgentSignatureCatalog.CreateForProfile(profile)
        };
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), agentClient),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = new TrainerTarget(
            profile.ProcessName,
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234,
            FileVersion: "1.13.0.0",
            VersionProfileId: profile.Id);

        var attach = manager.AttachTarget(manifest, target);
        var install = manager.InstallPatches(manifest, "diagnostics");

        Assert.True(attach.Success);
        Assert.Equal(TestAssets.CurrentRa3113HookCount, install.PatchResult.InstallResult.InstalledHookCount);
        Assert.Empty(install.PatchResult.SkippedHooks);
        Assert.DoesNotContain("部分安装", install.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectedAgentBackendRejectsUnknownExplicitProfileBeforeInjection()
    {
        var injector = new FakeAgentInjector();
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(injector, new FakeAgentClient()),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234,
            VersionProfileId: "ra3_unknown");

        var result = manager.AttachTarget(TestAssets.LoadManifest(), target);

        Assert.False(result.Success);
        Assert.False(injector.InjectCalled);
    }

    [Fact]
    public void ResetPatchesStateRestoresInjectedAgentPatches()
    {
        var agentClient = new FakeAgentClient();
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), agentClient),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234);

        manager.AttachTarget(manifest, target);
        manager.InstallPatches(manifest, "diagnostics");
        manager.ResetPatchesState();

        Assert.True(agentClient.RestoreCalled);
        Assert.False(manager.ArePatchesInstalled);
        Assert.False(manager.CanUseFeatures);
    }

    [Fact]
    public void MarkTargetOfflineClearsSessionWithoutWaitingForAgentRestore()
    {
        var agentClient = new FakeAgentClient();
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), agentClient),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234);

        manager.AttachTarget(manifest, target);
        manager.InstallPatches(manifest, "diagnostics");
        manager.MarkTargetOffline();

        Assert.False(agentClient.RestoreCalled);
        Assert.Null(manager.TargetProcessId);
        Assert.False(manager.ArePatchesInstalled);
        Assert.False(manager.CanUseFeatures);
    }

    [Fact]
    public void ExistingMatchingAgentRestoresInstalledSessionWithoutSecondInstall()
    {
        var client = new FakeAgentClient
        {
            PingFailuresRemaining = 0,
            StatusInstalledHookCount = (uint)TestAssets.CurrentStandardHookCount
        };
        var injector = new FakeAgentInjector();
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(injector, client),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234);

        var result = manager.AttachTarget(manifest, target);

        Assert.True(result.Success);
        Assert.Contains("重新连接", result.Message, StringComparison.Ordinal);
        Assert.False(injector.InjectCalled);
        Assert.True(manager.ArePatchesInstalled);
        Assert.NotNull(manager.FeatureController);
        Assert.Equal(TestAssets.CurrentStandardHookCount, manager.InstalledHookCount);
        Assert.Null(client.InstallRequest);
    }

    [Fact]
    public void ExistingIncompatibleAgentAsksForGameRestartWithoutSuggestingFallback()
    {
        var client = new FakeAgentClient
        {
            PingFailuresRemaining = 0,
            PingFingerprint = 0xDEADBEEFu
        };
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), client),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.AttachTarget(manifest, target));

        Assert.Contains("重启游戏", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("切换模式", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewlyInjectedIncompatibleAgentAsksForRestartWithoutSuggestingFallback()
    {
        var client = new FakeAgentClient
        {
            PingFailuresRemaining = 1,
            RejectInjectedAgentProtocol = true
        };
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), client),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.AttachTarget(TestAssets.LoadManifest(), target));

        Assert.Contains("关闭游戏和修改器", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("切换模式", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedUnitObjectUpgradeCapabilityNoTargetIsWaiting()
    {
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), new FakeAgentClient()),
            () => "C:/agent/RayaTrainer.Agent.dll");

        var capability = manager.GetFeatureCapability(TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature);

        Assert.Equal(FeatureCapabilityState.Waiting, capability.State);
        Assert.Equal("NO_TARGET", capability.ReasonCode);
    }

    [Fact]
    public void SelectedUnitObjectUpgradeCapabilityRa3112Ready()
    {
        var agentClient = new FakeAgentClient();
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), agentClient),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234);

        manager.AttachTarget(manifest, target);
        manager.InstallPatches(manifest, "diagnostics");

        var capability = manager.GetFeatureCapability(TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature);

        Assert.Equal(FeatureCapabilityState.Ready, capability.State);
        Assert.Equal("READY", capability.ReasonCode);
    }

    [Fact]
    public void SelectedUnitObjectUpgradeCapabilityUprisingProfileUnavailable()
    {
        var agentClient = new FakeAgentClient
        {
            SignatureScanPayload = TestAgentSignatureCatalog.CreateForProfile(
                Ra3VersionProfileRegistry.Uprising10)
        };
        var manager = new TrainerSessionManager(
            () => new InjectedAgentBackend(new FakeAgentInjector(), agentClient),
            () => "C:/agent/RayaTrainer.Agent.dll");
        var manifest = TestAssets.LoadManifest();
        var target = new TrainerTarget(
            "ra3ep1_1.0.game",
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            ProcessId: 1234,
            FileVersion: "1.0.3313.38400",
            VersionProfileId: Ra3VersionProfileRegistry.Uprising10.Id);

        manager.AttachTarget(manifest, target);
        manager.InstallPatches(manifest, "diagnostics");

        var capability = manager.GetFeatureCapability(TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature);

        Assert.Equal(FeatureCapabilityState.Unavailable, capability.State);
        Assert.Equal("UNIT_UPGRADE_PROFILE_NOT_SUPPORTED", capability.ReasonCode);
    }

    [Fact]
    public void IsUnitUpgradeNativeLayoutReady_AllVerifiedNonZero_ReturnsTrue()
    {
        var profile = Ra3VersionProfileRegistry.Ra3112;

        var result = TrainerSessionManager.IsUnitUpgradeNativeLayoutReady(profile);

        Assert.True(result);
    }

    [Fact]
    public void IsUnitUpgradeNativeLayoutReady_MissingEntry_ReturnsFalse()
    {
        var refs = new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameObjectAddUpgrade"] = new("GameObjectAddUpgrade", 0x379650, AddressSupportStatus.Verified, "test"),
            // ProductionModulesOffset deliberately omitted
            ["UpgradeTemplateTypeOffset"] = new("UpgradeTemplateTypeOffset", 0x24, AddressSupportStatus.Verified, "test")
        };
        var profile = new Ra3VersionProfile
        {
            Id = "ra3_1.12",
            DisplayName = "RA3 1.12 (test)",
            ProcessName = "ra3_1.12.game",
            FileVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1.12.0.0" },
            Hooks = new Dictionary<string, VersionedAddress>(),
            RemoteGlobals = new Dictionary<string, VersionedAddress>(),
            EngineFunctions = new Dictionary<string, VersionedAddress>(),
            NativeAgentRefs = refs
        };

        var result = TrainerSessionManager.IsUnitUpgradeNativeLayoutReady(profile);

        Assert.False(result);
    }

    [Fact]
    public void IsUnitUpgradeNativeLayoutReady_ZeroRva_ReturnsFalse()
    {
        var refs = new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameObjectAddUpgrade"] = new("GameObjectAddUpgrade", 0, AddressSupportStatus.Verified, "test"),
            ["ProductionModulesOffset"] = new("ProductionModulesOffset", 0x310, AddressSupportStatus.Verified, "test"),
            ["UpgradeTemplateTypeOffset"] = new("UpgradeTemplateTypeOffset", 0x24, AddressSupportStatus.Verified, "test")
        };
        var profile = new Ra3VersionProfile
        {
            Id = "ra3_1.12",
            DisplayName = "RA3 1.12 (test)",
            ProcessName = "ra3_1.12.game",
            FileVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1.12.0.0" },
            Hooks = new Dictionary<string, VersionedAddress>(),
            RemoteGlobals = new Dictionary<string, VersionedAddress>(),
            EngineFunctions = new Dictionary<string, VersionedAddress>(),
            NativeAgentRefs = refs
        };

        var result = TrainerSessionManager.IsUnitUpgradeNativeLayoutReady(profile);

        Assert.False(result);
    }

    [Fact]
    public void IsUnitUpgradeNativeLayoutReady_NeedsReanalysisStatus_ReturnsFalse()
    {
        var refs = new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameObjectAddUpgrade"] = new("GameObjectAddUpgrade", 0x379650, AddressSupportStatus.Verified, "test"),
            ["ProductionModulesOffset"] = new("ProductionModulesOffset", 0x310, AddressSupportStatus.Verified, "test"),
            ["UpgradeTemplateTypeOffset"] = new("UpgradeTemplateTypeOffset", 0x24, AddressSupportStatus.NeedsReanalysis, "test")
        };
        var profile = new Ra3VersionProfile
        {
            Id = "ra3_1.12",
            DisplayName = "RA3 1.12 (test)",
            ProcessName = "ra3_1.12.game",
            FileVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1.12.0.0" },
            Hooks = new Dictionary<string, VersionedAddress>(),
            RemoteGlobals = new Dictionary<string, VersionedAddress>(),
            EngineFunctions = new Dictionary<string, VersionedAddress>(),
            NativeAgentRefs = refs
        };

        var result = TrainerSessionManager.IsUnitUpgradeNativeLayoutReady(profile);

        Assert.False(result);
    }

    private sealed class FakeAgentInjector : IAgentInjector
    {
        public bool InjectCalled { get; private set; }

        public AgentInjectionResult Inject(int processId, string agentDllPath, TimeSpan timeout)
        {
            InjectCalled = true;
            return new AgentInjectionResult(true, "ok", 0x5000);
        }
    }

    private sealed class FakeAgentClient : IAgentClient
    {
        public AgentSignatureScanPayload SignatureScanPayload { get; init; } =
            TestAgentSignatureCatalog.CreateRa3112();

        public AgentInstallPatchesRequest? InstallRequest { get; private set; }
        public bool RestoreCalled { get; private set; }
        public bool FailRuntimeReads { get; set; }
        public int PingFailuresRemaining { get; set; } = 1;
        public ulong PingFingerprint { get; set; } = AgentBuildIdentity.Fingerprint;
        public bool RejectInjectedAgentProtocol { get; set; }
        public uint StatusInstalledHookCount { get; set; }
        public Dictionary<uint, byte[]> MemoryByAddress { get; } = [];
        public IReadOnlyList<RuntimePatchSetDefinition> PatchSetDefinitions { get; init; } =
            RuntimePatchSetCatalog.All;

        public Task<AgentPingPayload> PingAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (PingFailuresRemaining-- > 0)
            {
                throw new TimeoutException("no existing agent");
            }

            if (RejectInjectedAgentProtocol)
            {
                throw new InvalidDataException(
                    $"Agent protocol version mismatch. Expected {AgentProtocol.Version}, actual {AgentProtocol.Version - 1}.");
            }

            return Task.FromResult(new AgentPingPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                processId,
                0x400000,
                (uint)NativeRuntimeCapabilities.Required,
                PingFingerprint));
        }

        public Task<AgentStatusPayload> GetStatusAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentStatusPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                processId,
                0x400000,
                StatusInstalledHookCount,
                (uint)NativeRuntimeCapabilities.Required));
        }

        public Task<AgentCommandResultPayload> InstallPatchesAsync(int processId, AgentInstallPatchesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            InstallRequest = request;
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, checked((uint)request.Hooks.Count)));
        }

        public Task<AgentCommandResultPayload> RestorePatchesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            RestoreCalled = true;
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<AgentCommandResultPayload> SetFeatureStatesAsync(int processId, SetFeatureStatesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 24));
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

        public Task<AgentMemoryReadPayload> ReadMemoryAsync(int processId, AgentMemoryReadRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var bytes = MemoryByAddress.TryGetValue(request.Address, out var configured)
                ? configured.ToArray()
                : ResolvePatchSetBaseline(request);
            if (FailRuntimeReads && bytes is null)
            {
                throw new InvalidOperationException("runtime read failed");
            }

            bytes ??= new byte[request.ByteCount];
            return Task.FromResult(new AgentMemoryReadPayload(AgentStatusCode.Ok, AgentProtocol.Version, request.Address, bytes));
        }

        private byte[]? ResolvePatchSetBaseline(AgentMemoryReadRequest request)
        {
            return TestAssets.ResolveRuntimePatchSetDisableBytes(
                request.Address,
                request.ByteCount,
                definitions: PatchSetDefinitions);
        }

        public Task<AgentCommandResultPayload> SetNativeCatalogAsync(int processId, IReadOnlyList<uint> rvas, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentMismatchDiagnosticsPayload(AgentStatusCode.InvalidCommand, AgentProtocol.Version, 0, [], [], [], MismatchKind.Hook, 0));
        }

        public Task<AgentSignatureScanPayload> ScanSignaturesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SignatureScanPayload);
        }

        public Task<AgentGameModePayload> GetGameModeAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (FailRuntimeReads)
            {
                throw new InvalidOperationException("game mode read failed");
            }

            return Task.FromResult(new AgentGameModePayload(AgentStatusCode.Ok, AgentProtocol.Version, GameRuntimeConstants.GameModeShell));
        }

        public Task<AgentGameApiGetThingClassPayload> SmokeGetThingClassAsync(
            int processId,
            AgentGameApiGetThingClassRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSelectedUnitSnapshotPayload> ReadSelectedUnitSnapshotViaGameApiAsync(
            int processId,
            AgentGameApiReadSelectedUnitCodeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiLevelUpSelectedPayload> LevelUpSelectedAsync(
            int processId,
            AgentGameApiLevelUpSelectedRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiCreateUnitPayload> CreateUnitAsync(
            int processId,
            AgentGameApiCreateUnitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiKillUnitPayload> KillUnitAsync(
            int processId,
            AgentGameApiKillUnitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiCopyForMePayload> CopyForMeAsync(
            int processId,
            AgentGameApiCopyForMeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGetMeBasePayload> GetMeBaseAsync(
            int processId,
            AgentGameApiGetMeBaseRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiWeNeedBackPayload> WeNeedBackAsync(
            int processId,
            AgentGameApiWeNeedBackRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSetUnitStatePayload> SetUnitStateAsync(
            int processId,
            AgentGameApiSetUnitStateRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGetCurrentPlayerPayload> GetCurrentPlayerAsync(
            int processId,
            AgentGameApiGetCurrentPlayerRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiLookupScienceByHashPayload> LookupScienceByHashAsync(
            int processId,
            AgentGameApiLookupScienceByHashRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantPlayerTechPayload> GrantPlayerTechAsync(
            int processId,
            AgentGameApiGrantPlayerTechRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantUpgradeToPlayerPayload> GrantUpgradeToPlayerAsync(
            int processId,
            AgentGameApiGrantUpgradeToPlayerRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiHasUpgradePayload> HasUpgradeAsync(
            int processId,
            AgentGameApiHasUpgradeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiLookupTemplateByHashPayload> LookupTemplateByHashAsync(
            int processId,
            AgentGameApiLookupTemplateByHashRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiLookupUpgradeByHashPayload> LookupUpgradeByHashAsync(
            int processId,
            AgentGameApiLookupUpgradeByHashRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantSecretProtocolPayload> GrantSecretProtocolAsync(
            int processId,
            AgentGameApiGrantSecretProtocolRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantSelectedUpgradePayload> GrantSelectedUpgradeAsync(
            int processId,
            AgentGameApiGrantSelectedUpgradeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiClearPlayerTechLocksPayload> ClearPlayerTechLocksAsync(
            int processId,
            AgentGameApiClearPlayerTechLocksRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSecretProtocolBindingProbePayload> SecretProtocolBindingProbeAsync(
            int processId,
            AgentGameApiSecretProtocolBindingProbeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiReplaceTemplateModelPayload> ReplaceTemplateModelAsync(
            int processId,
            AgentGameApiReplaceTemplateModelRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiReplaceTemplateWeaponPayload> ReplaceTemplateWeaponAsync(
            int processId,
            AgentGameApiReplaceTemplateWeaponRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSetSelectedStatusBitPayload> SetSelectedStatusBitAsync(
            int processId,
            AgentGameApiSetSelectedStatusBitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSetSelectedUnitHealthPayload> SetSelectedUnitHealthAsync(
            int processId,
            AgentGameApiSetSelectedUnitHealthRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiExpandProductionQueuePayload> ExpandProductionQueueAsync(
            int processId,
            AgentGameApiExpandProductionQueueRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSetSelectedUnitSpeedPayload> SetSelectedUnitSpeedAsync(
            int processId, AgentGameApiSetSelectedUnitSpeedRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiCaptureSelectedUnitsPayload> CaptureSelectedUnitsAsync(
            int processId, AgentGameApiCaptureSelectedUnitsRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiSetSelectedUnitAmmoPayload> SetSelectedUnitAmmoAsync(
            int processId, AgentGameApiSetSelectedUnitAmmoRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiToggleSelectedAttackSpeedPayload> ToggleSelectedAttackSpeedAsync(
            int processId, AgentGameApiToggleSelectedAttackSpeedRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiToggleSelectedAttackRangePayload> ToggleSelectedAttackRangeAsync(
            int processId, AgentGameApiToggleSelectedAttackRangeRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiClearSelectedAttackSpeedEffectsPayload> ClearSelectedAttackSpeedEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackSpeedEffectsRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiClearSelectedAttackRangeEffectsPayload> ClearSelectedAttackRangeEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackRangeEffectsRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiTeleportSelectedUnitsToMousePayload> TeleportSelectedUnitsToMouseAsync(
            int processId,
            AgentGameApiTeleportSelectedUnitsToMouseRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSelectedUnitUpgradesPayload> GetSelectedUnitUpgradesAsync(
            int processId,
            AgentGameApiGetSelectedUnitUpgradesRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload> GrantObjectUpgradeOnSelectedSameTypeAsync(
            int processId,
            AgentGameApiGrantObjectUpgradeOnSelectedSameTypeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
