using RayaTrainer.App.Services;
using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class NoviceExperienceTests
{
    [Fact]
    public void PrimaryActionExplainsOfflineConnectedAndReadyStates()
    {
        using var offline = Load(new NoviceSession());
        using var connected = Load(new NoviceSession
        {
            TargetProcessIdValue = 1234,
            CanUseFeaturesValue = true,
            Snapshot = Snapshot(TrainerDiagnosticHealth.Attention)
        });
        using var ready = Load(new NoviceSession
        {
            TargetProcessIdValue = 1234,
            CanUseFeaturesValue = true,
            ArePatchesInstalledValue = true,
            Snapshot = Snapshot(TrainerDiagnosticHealth.Healthy)
        });

        Assert.Contains("查找", offline.PrimaryActionTitle, StringComparison.Ordinal);
        Assert.Equal("第 1 步，共 3 步", offline.PrimaryActionStepText);
        Assert.Equal("启用修改器功能", connected.PrimaryActionTitle);
        Assert.Equal("第 2 步，共 3 步", connected.PrimaryActionStepText);
        Assert.Equal("开始使用修改器", ready.PrimaryActionTitle);
        Assert.Equal("准备完成", ready.PrimaryActionStepText);
    }

    [Fact]
    public void OfflinePrimaryActionOpensGamePathSetupWhenNoGameIsFound()
    {
        using var viewModel = Load(new NoviceSession());

        viewModel.PrimaryActionCommand.Execute(null);

        Assert.True(viewModel.IsGameSetupExpanded);
        Assert.Contains("选择游戏程序", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticStageUsesPlainChineseAndExecutesRepairAction()
    {
        var retryCount = 0;
        var source = new NoviceSession
        {
            Snapshot = Snapshot(TrainerDiagnosticHealth.Offline) with
            {
                Stages =
                [
                    new DiagnosticStageSnapshot(
                        "target",
                        "TARGET",
                        DiagnosticStageState.Pending,
                        "未连接目标",
                        "先打开游戏，再重新查找")
                ]
            }
        };
        using var diagnostics = new DiagnosticsViewModel(
            source,
            [],
            _ => { },
            retrySession: () => retryCount++);

        var stage = Assert.Single(diagnostics.Stages);
        Assert.Equal("找到游戏", stage.Label);
        Assert.Equal("等待中", stage.StateLabel);
        Assert.Equal("重新查找游戏", stage.ActionLabel);

        stage.ActionCommand.Execute(null);

        Assert.Equal(1, retryCount);
    }

    [Fact]
    public void FailedAutomaticConnectionOpensRepairPageInsteadOfContinuingBlindly()
    {
        var session = new NoviceSession { FailAttach = true };
        var profile = RayaTrainer.Core.Versions.Ra3VersionProfileRegistry.Ra3112;
        var locator = new TrainerProcessLocator(() =>
        [
            new TrainerProcessCandidate(
                1234,
                profile.ProcessName,
                profile.ProcessName,
                "C:/Games/RA3/ra3_1.12.game",
                new nint(0x400000),
                true,
                GameTarget.ExpectedVersion)
        ]);
        using var viewModel = Load(session, locator);

        viewModel.PrimaryActionCommand.Execute(null);

        Assert.Equal(6, viewModel.SelectedPageIndex);
        Assert.Equal(TrainerDiagnosticHealth.Error, viewModel.Diagnostics.Health);
    }

    private static MainViewModel Load(NoviceSession session, TrainerProcessLocator? locator = null)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        return MainViewModel.Load(
            TestAssets.LoadManifest(),
            new TrainerAppSettingsStore(settingsPath),
            sessionManager: session,
            locator: locator ?? new TrainerProcessLocator(() => []));
    }

    private static TrainerDiagnosticSnapshot Snapshot(TrainerDiagnosticHealth health) =>
        TrainerDiagnosticSnapshot.Offline with
        {
            CapturedAt = DateTimeOffset.Now,
            Health = health,
            Summary = health.ToString()
        };

    private sealed class NoviceSession : ITrainerSessionService, ITrainerDiagnosticsSource
    {
        private EventHandler? _diagnosticsChanged;

        public event EventHandler? DiagnosticsChanged
        {
            add => _diagnosticsChanged += value;
            remove => _diagnosticsChanged -= value;
        }

        public ITrainerFeatureController? FeatureController => null;
        public bool ArePatchesInstalled => ArePatchesInstalledValue;
        public int? TargetProcessId => TargetProcessIdValue;
        public bool CanUseFeatures => CanUseFeaturesValue;
        public int InstalledHookCount => 0;
        public string RemoteSymbolSummary => "";
        public IReadOnlyList<TrainerDiagnosticEvent> DiagnosticEvents => Snapshot.RecentEvents;

        public bool ArePatchesInstalledValue { get; set; }
        public int? TargetProcessIdValue { get; set; }
        public bool CanUseFeaturesValue { get; set; }
        public bool FailAttach { get; set; }
        public TrainerDiagnosticSnapshot Snapshot { get; set; } = TrainerDiagnosticSnapshot.Offline;

        public AttachResult AttachTarget(TrainerManifest manifest, TrainerTarget target)
        {
            if (FailAttach)
            {
                Snapshot = NoviceExperienceTests.Snapshot(TrainerDiagnosticHealth.Error);
                _diagnosticsChanged?.Invoke(this, EventArgs.Empty);
                throw new InvalidOperationException("无法连接 Agent");
            }

            TargetProcessIdValue = target.ProcessId;
            CanUseFeaturesValue = true;
            return new AttachResult(true, "已连接");
        }

        public SessionInstallOutcome InstallPatches(TrainerManifest manifest, string diagnosticsDir)
        {
            ArePatchesInstalledValue = true;
            return new SessionInstallOutcome(new PatchMismatchReportResult(PatchInstallResult.Empty, null), "已启用");
        }

        public void ResetPatchesState()
        {
            TargetProcessIdValue = null;
            CanUseFeaturesValue = false;
            ArePatchesInstalledValue = false;
        }

        public void MarkTargetOffline() => ResetPatchesState();

        public bool IsTargetGameForeground() => false;
        public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature) =>
            TrainerFeatureCapabilityEvaluator.Evaluate(
                feature,
                new TrainerFeatureCapabilityContext(
                    TargetProcessId is not null,
                    CanUseFeatures,
                    ArePatchesInstalled,
                    true,
                    false));

        public TrainerDiagnosticSnapshot GetDiagnosticSnapshot(IReadOnlyList<TrainerFeature> features, int maxEvents = 200) => Snapshot;

        public Task<TrainerDiagnosticSnapshot> RefreshDiagnosticsAsync(
            IReadOnlyList<TrainerFeature> features,
            CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);

        public void RecordDiagnosticEvent(DiagnosticEventSeverity severity, string code, string message, string? detail = null) { }

        public void Dispose() => _diagnosticsChanged = null;
    }
}
