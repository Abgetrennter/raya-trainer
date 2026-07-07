using RayaTrainer.App.Services;
using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class MainViewModelVersionSelectionTests
{
    [Fact]
    public void RefreshProcessRequiresUserChoiceWhenMultipleInstallableTargetsAreDetected()
    {
        var session = new FakeTrainerSessionService();
        var locator = new TrainerProcessLocator(() =>
        [
            Candidate(100),
            Candidate(101)
        ]);
        var viewModel = MainViewModel.Load(
            TestAssets.LoadManifest(),
            NewSettingsStore(),
            sessionManager: session,
            locator: locator);

        viewModel.RefreshProcess();

        Assert.Equal(0, session.AttachCount);
        Assert.Contains("多个", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    private static TrainerProcessCandidate Candidate(int processId)
    {
        return new TrainerProcessCandidate(
            processId,
            "ra3_1.12",
            GameTarget.ProcessName,
            @$"D:\Games\RA3\{processId}\Data\{GameTarget.ProcessName}",
            0x400000,
            true,
            GameTarget.ExpectedVersion);
    }

    private static TrainerAppSettingsStore NewSettingsStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        return new TrainerAppSettingsStore(Path.Combine(directory, "settings.json"));
    }
}
