using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Versions;

public sealed class Ra3TargetSelectionWaiterTests
{
    [Fact]
    public async Task WaitForDefaultAsyncKeepsPollingWhenOnlyNonInstallableTargetsExist()
    {
        var calls = 0;
        var now = DateTimeOffset.Parse("2026-06-22T00:00:00Z");
        var first = Select([
            Candidate(100, "ra3_1.13.game", "2.0.9999.0")
        ]);
        var second = Select([
            Candidate(101, GameTarget.ProcessName, GameTarget.ExpectedVersion)
        ]);

        var result = await Ra3TargetSelectionWaiter.WaitForDefaultAsync(
            () => calls++ == 0 ? first : second,
            TimeSpan.FromSeconds(5),
            (delay, _) =>
            {
                now += delay;
                return Task.CompletedTask;
            },
            () => now);

        Assert.Equal(TargetSelectionStatus.SingleAutoSelected, result.Status);
        Assert.Equal(101, result.SelectedTarget?.ProcessId);
        Assert.Equal(2, calls);
    }

    private static TargetSelectionResult Select(IReadOnlyList<TrainerProcessCandidate> candidates)
    {
        return Ra3VersionDetector.SelectDefault(Ra3VersionDetector.DetectAll(candidates));
    }

    private static TrainerProcessCandidate Candidate(int processId, string moduleName, string fileVersion)
    {
        return new TrainerProcessCandidate(
            processId,
            Path.GetFileNameWithoutExtension(moduleName),
            moduleName,
            @$"D:\Games\RA3\Data\{moduleName}",
            0x400000,
            true,
            fileVersion);
    }
}
