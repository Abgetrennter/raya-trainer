using RayaTrainer.App.Services;
using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class MainViewModelTargetSelectionTests
{
    // Note: the "multiple installable targets -> no auto attach" core behavior is already
    // covered by MainViewModelVersionSelectionTests. These tests focus on the new structured
    // display (CurrentTargetInfo) and the candidate picker interaction (SelectableCandidates /
    // SelectCandidateCommand) added in Phase 5.

    [Fact]
    public void AmbiguousTargetsExposeInstallableCandidatesForPicker()
    {
        var session = new FakeTrainerSessionService();
        var locator = new TrainerProcessLocator(() =>
        [
            Candidate(1111, "ra3_1.12", "ra3_1.12.game", GameTarget.ExpectedVersion),
            Candidate(2222, "ra3_1.13", "ra3_1.13.game", "1.13.0.0")
        ]);
        var viewModel = LoadViewModel(session, locator);

        viewModel.RefreshProcess();

        Assert.True(viewModel.HasSelectableCandidates);
        // Only installable candidates surface in the picker (both are installable here).
        Assert.Equal(2, viewModel.SelectableCandidates.Count);
        Assert.All(viewModel.SelectableCandidates, c => Assert.Equal(TargetSupportStatus.Installable, c.SupportStatus));
        Assert.Equal(0, session.AttachCount);
    }

    [Fact]
    public void AmbiguousTargetsIncludeUprising10Candidate()
    {
        var session = new FakeTrainerSessionService();
        var locator = new TrainerProcessLocator(() =>
        [
            Candidate(1111, "ra3_1.12", "ra3_1.12.game", GameTarget.ExpectedVersion),
            Candidate(2222, "ra3_1.13", "ra3_1.13.game", "1.13.0.0"),
            Candidate(3333, "ra3ep1_1.0", "ra3ep1_1.0.game", "1.0.3313.38400")
        ]);
        var viewModel = LoadViewModel(session, locator);

        viewModel.RefreshProcess();

        Assert.True(viewModel.HasSelectableCandidates);
        Assert.Equal(3, viewModel.SelectableCandidates.Count);
        Assert.Contains(viewModel.SelectableCandidates, c =>
            c.ProcessId == 3333 && c.SupportStatus == TargetSupportStatus.Installable);
    }

    [Fact]
    public void SelectCandidateAttachesChosenTargetAndClearsPicker()
    {
        var session = new FakeTrainerSessionService();
        var locator = new TrainerProcessLocator(() =>
        [
            Candidate(1111, "ra3_1.12", "ra3_1.12.game", GameTarget.ExpectedVersion),
            Candidate(2222, "ra3_1.13", "ra3_1.13.game", "1.13.0.0")
        ]);
        var viewModel = LoadViewModel(session, locator);
        viewModel.RefreshProcess();
        var chosen = Assert.Single(viewModel.SelectableCandidates, c => c.ProcessId == 2222);

        viewModel.SelectCandidateCommand.Execute(chosen);

        Assert.False(viewModel.HasSelectableCandidates);
        Assert.Equal(1, session.AttachCount);
        Assert.Equal(2222, session.TargetProcessId);
    }

    [Fact]
    public void SelectCandidateIgnoresNullParameter()
    {
        var session = new FakeTrainerSessionService();
        var locator = new TrainerProcessLocator(() =>
        [
            Candidate(1111, "ra3_1.12", "ra3_1.12.game", GameTarget.ExpectedVersion),
            Candidate(2222, "ra3_1.13", "ra3_1.13.game", "1.13.0.0")
        ]);
        var viewModel = LoadViewModel(session, locator);
        viewModel.RefreshProcess();

        viewModel.SelectCandidateCommand.Execute(null);

        Assert.True(viewModel.HasSelectableCandidates);
        Assert.Equal(0, session.AttachCount);
    }

    [Fact]
    public void SingleTargetAttachPopulatesStructuredTargetInfo()
    {
        var session = new FakeTrainerSessionService();
        var locator = new TrainerProcessLocator(() =>
        [
            Candidate(1111, "ra3_1.12", "ra3_1.12.game", GameTarget.ExpectedVersion),
            // A different version family remains unsupported and keeps the single-target branch.
            Candidate(3333, "ra3ep1_1.0", "ra3ep1_1.0.game", "2.0.0.0")
        ]);
        var viewModel = LoadViewModel(session, locator);

        viewModel.RefreshProcess();

        Assert.False(viewModel.HasSelectableCandidates);
        Assert.Contains("1111", viewModel.CurrentTargetInfo);
        Assert.Contains("ra3_1.12", viewModel.CurrentTargetInfo);
        Assert.Contains("DLL Agent", viewModel.CurrentTargetInfo);
    }

    [Fact]
    public void SingleSignatureCompatibilityCandidateAutoAttachesWithVisibleModeLabel()
    {
        var session = new FakeTrainerSessionService();
        var locator = new TrainerProcessLocator(() =>
        [
            Candidate(1111, "ra3_1.12", "ra3_1.12.game", "1.12.9999.99999")
        ]);
        var viewModel = LoadViewModel(session, locator);

        viewModel.RefreshProcess();

        Assert.Equal(1, session.AttachCount);
        Assert.Contains("签名兼容", viewModel.CurrentTargetInfo, StringComparison.Ordinal);
        Assert.False(viewModel.HasSelectableCandidates);
    }

    // Regression: after a reload/restore the per-feature toggle rows used to keep
    // showing stale "已启用" because DisposeSession()/RestorePatches() reset the
    // session but never the FeatureItemViewModel toggle state. Now both dispose
    // paths call FeatureToggle.ResetToggleStates(), so toggles return to "未启用".
    [Fact]
    public void RestorePatchesResetsToggleStateToDisabled()
    {
        var session = new FakeTrainerSessionService(new StubFeatureController(toggleState: true));
        var locator = new TrainerProcessLocator(() => [Candidate(1111, "ra3_1.12", "ra3_1.12.game", GameTarget.ExpectedVersion)]);
        var viewModel = LoadViewModel(session, locator);

        // RefreshProcess -> DisposeSession (clears) -> attach -> InstallPatches (installs +
        // RefreshToggleStates reads toggleState:true from the stub, flipping the row to enabled).
        viewModel.RefreshProcess();
        Assert.True(viewModel.ArePatchesInstalled);
        var toggle = FirstToggleItem(viewModel);
        Assert.True(toggle.IsFeatureEnabled);
        Assert.Equal("已启用", toggle.Status);

        // Restore -> dispose path -> ResetToggleStates() zeroes the row.
        viewModel.RestorePatches();

        Assert.False(viewModel.ArePatchesInstalled);
        Assert.False(toggle.IsFeatureEnabled);
        Assert.Equal("未启用", toggle.Status);
    }

    // Regression: same fix also covers the reload-via-RefreshProcess path. After a
    // successful reload the post-install refresh self-corrects, but during the dispose
    // window (and on a reload that fails to re-install) the rows must show "未启用".
    [Fact]
    public void RefreshProcessClearsStaleToggleStateBeforeReattach()
    {
        var session = new FakeTrainerSessionService(new StubFeatureController(toggleState: false));
        var locator = new TrainerProcessLocator(() => [Candidate(1111, "ra3_1.12", "ra3_1.12.game", GameTarget.ExpectedVersion)]);
        var viewModel = LoadViewModel(session, locator);

        viewModel.RefreshProcess();
        var toggle = FirstToggleItem(viewModel);
        // Simulate a previously-enabled row that should not survive a reload.
        Assert.True(toggle.Command.CanExecute(null));
        toggle.ExecuteFromHotkey();
        Assert.True(toggle.IsFeatureEnabled);

        // A second RefreshProcess disposes (-> ResetToggleStates) then re-installs.
        // The stub now reports toggleState:false, but _desiredEnabled persists from the
        // earlier user toggle. The row lands at "应用失败" (desired=true, observed=false).
        viewModel.RefreshProcess();

        Assert.True(viewModel.ArePatchesInstalled);
        Assert.False(toggle.IsFeatureEnabled);
        Assert.Equal("应用失败", toggle.Status);
    }

    private static FeatureItemViewModel FirstToggleItem(MainViewModel viewModel) =>
        viewModel.FeatureToggle.AllFeatureItems().First(item => item.IsToggle);

    private static MainViewModel LoadViewModel(FakeTrainerSessionService session, TrainerProcessLocator locator)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settings = new TrainerAppSettingsStore(Path.Combine(directory, "settings.json"));
        return MainViewModel.Load(TestAssets.LoadManifest(), settings, sessionManager: session, locator: locator);
    }

    private static TrainerProcessCandidate Candidate(int pid, string processName, string moduleName, string fileVersion)
    {
        return new TrainerProcessCandidate(
            pid,
            processName,
            moduleName,
            @$"C:\Game\{pid}\Data\{moduleName}",
            new nint(0x400000),
            Is32Bit: true,
            fileVersion);
    }
}
