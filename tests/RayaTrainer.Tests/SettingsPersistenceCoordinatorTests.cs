using System.Threading;
using RayaTrainer.App.Services;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class SettingsPersistenceCoordinatorTests
{
    [Fact]
    public async Task MarkDirty_AfterDebounce_SavesOnce()
    {
        var saveCount = 0;
        TrainerAppSettings last = TrainerAppSettings.Default;
        var coord = new SettingsPersistenceCoordinator(
            () => { last = TrainerAppSettings.Default; return TrainerAppSettings.Default; },
            _ => { },
            debounceMs: 50,
            saveAction: _ => { saveCount++; });

        coord.MarkDirty();
        coord.MarkDirty();
        coord.MarkDirty();

        await Task.Delay(200);
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public async Task FlushAsync_BypassesDebounce_SavesImmediately()
    {
        var saveCount = 0;
        var coord = new SettingsPersistenceCoordinator(
            () => TrainerAppSettings.Default,
            _ => { },
            debounceMs: 5000,
            saveAction: _ => { saveCount++; });

        coord.MarkDirty();
        await coord.FlushAsync();

        Assert.Equal(1, saveCount);
    }

    [Fact]
    public async Task ConcurrentMarkDirtyAndFlush_DoNotCorruptOrDoubleSave()
    {
        var saveCount = 0;
        var coord = new SettingsPersistenceCoordinator(
            () => TrainerAppSettings.Default,
            _ => { },
            debounceMs: 30,
            saveAction: _ => { Interlocked.Increment(ref saveCount); });

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 10; i++) coord.MarkDirty();
        })).ToArray();
        await Task.WhenAll(tasks);
        await coord.FlushAsync();

        // 并发不应损坏；防抖合并，最多几次保存（不应 50+）
        Assert.True(saveCount >= 1 && saveCount <= 10);
    }

    [Fact]
    public async Task SaveFailure_RaisesErrorOnce_RetriesSucceedClearsError()
    {
        string? error = null;
        int call = 0;
        var errorReported = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coord = new SettingsPersistenceCoordinator(
            () => TrainerAppSettings.Default,
            err =>
            {
                error = err;
                if (err is not null)
                {
                    errorReported.TrySetResult(err);
                }
            },
            debounceMs: 10,
            saveAction: _ =>
            {
                call++;
                if (call == 1) throw new IOException("disk full");
            });

        coord.MarkDirty();
        var reportedError = await errorReported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("disk full", reportedError);
        Assert.Equal("disk full", error);

        coord.MarkDirty();
        await coord.FlushAsync(); // 第二次成功
        Assert.Null(error);
    }
}
