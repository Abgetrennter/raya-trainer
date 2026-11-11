using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.Services;

/// <summary>
/// 单写者防抖原子保存协调器。
/// - 任意调用方只调 MarkDirty()（防抖）或 FlushAsync()（立即）。
/// - 快照捕获在 UI 线程（调用方提供 captureSnapshot）。
/// - 后台串行写文件，临时文件 flush 后原子替换正式文件。
/// - 连续失败只报一次错误，成功后清除。
/// </summary>
public sealed class SettingsPersistenceCoordinator : IDisposable
{
    private readonly Func<TrainerAppSettings> _captureSnapshot;
    private readonly Action<string?> _onError;
    private readonly int _debounceMs;
    private readonly Action<TrainerAppSettings>? _saveAction; // 测试注入用；默认 null → 用 _store
    private readonly TrainerAppSettingsStore? _store;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;
    private string? _lastError;
    private long _capturedGeneration;
    private long _persistedGeneration; // 受 _writeLock 保护

    public SettingsPersistenceCoordinator(
        Func<TrainerAppSettings> captureSnapshot,
        Action<string?> onError,
        int debounceMs = 800,
        Action<TrainerAppSettings>? saveAction = null,
        TrainerAppSettingsStore? store = null)
    {
        _captureSnapshot = captureSnapshot;
        _onError = onError;
        _debounceMs = debounceMs;
        _saveAction = saveAction;
        _store = store;
    }

    /// <summary>标记脏。在调用线程捕获快照（UI 线程），防抖合并后后台写文件。</summary>
    public void MarkDirty()
    {
        // 在调用线程（UI 线程）捕获不可变快照，避免后台线程读取 ObservableCollection。
        var generation = Interlocked.Increment(ref _capturedGeneration);
        var snapshot = _captureSnapshot();

        var newCts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _debounceCts, newCts);
        old?.Cancel();
        var token = newCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceMs, token);
                if (!token.IsCancellationRequested) await WriteSnapshotAsync(generation, snapshot);
            }
            catch (OperationCanceledException)
            {
                // Debounce cancelled — expected.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Already handled inside SaveInternal; swallow to avoid unobserved task exception.
            }
            catch (Exception ex)
            {
                // Unexpected failure — report once.
                if (_lastError is null)
                {
                    _lastError = ex.Message;
                    _onError(ex.Message);
                }
            }
        });
    }

    /// <summary>立即 flush（取消防抖）。在调用线程捕获快照（应为 UI 线程）。</summary>
    public async Task FlushAsync()
    {
        _debounceCts?.Cancel();
        var generation = Interlocked.Increment(ref _capturedGeneration);
        var snapshot = _captureSnapshot();
        await WriteSnapshotAsync(generation, snapshot);
    }

    /// <summary>同步 flush（退出时用）。在调用线程捕获快照（应为 UI 线程）。</summary>
    public void Flush()
    {
        _debounceCts?.Cancel();
        var generation = Interlocked.Increment(ref _capturedGeneration);
        var snapshot = _captureSnapshot();
        WriteSnapshotInternal(generation, snapshot);
    }

    private async Task WriteSnapshotAsync(long generation, TrainerAppSettings snapshot)
    {
        await _writeLock.WaitAsync();
        try
        {
            SaveIfNewer(generation, snapshot);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void WriteSnapshotInternal(long generation, TrainerAppSettings snapshot)
    {
        _writeLock.Wait();
        try
        {
            SaveIfNewer(generation, snapshot);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // 已越过防抖延迟的写任务可能在 Flush 之后才排队拿到写锁；编号更旧的快照必须丢弃，
    // 否则旧设置会覆盖刚刚落盘的新设置（表现为改完快捷键、退出后被改回去）。
    private void SaveIfNewer(long generation, TrainerAppSettings snapshot)
    {
        if (generation <= _persistedGeneration)
        {
            return;
        }

        SaveInternal(snapshot);
        _persistedGeneration = generation;
    }

    private void SaveInternal(TrainerAppSettings snapshot)
    {
        try
        {
            (_saveAction ?? (s => _store?.Save(s))).Invoke(snapshot);
            if (_lastError is not null)
            {
                _lastError = null;
                _onError(null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_lastError is null)
            {
                _lastError = ex.Message;
                _onError(ex.Message);
            }
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _writeLock.Dispose();
    }
}
