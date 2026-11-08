using System.Windows.Threading;
using RayaTrainer.Core.Features;

namespace RayaTrainer.App.Services;

/// <summary>
/// 自动修复脉冲定时器。从 MainViewModel 提取，无 UI 表面，纯后台服务。
/// 监听 "Player Auto Repair" 开关，开启后每秒写入脉冲，关闭或卸载时清除。
/// </summary>
public sealed class AutoRepairPulseService : IDisposable
{
    public const string AutoRepairRawName = TrainerFeatureIds.AutoRepair;

    private readonly DispatcherTimer _pulseTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Func<bool> _arePatchesInstalled;
    private readonly Func<ITrainerFeatureController?> _getController;
    private readonly Action<string> _setStatus;
    private bool _enabled;

    public AutoRepairPulseService(
        Func<bool> arePatchesInstalled,
        Func<ITrainerFeatureController?> getController,
        Action<string> setStatus)
    {
        _arePatchesInstalled = arePatchesInstalled;
        _getController = getController;
        _setStatus = setStatus;
        _pulseTimer.Tick += OnPulseTimerTick;
    }

    /// <summary>
    /// Called when the "Player Auto Repair" toggle flips.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            // Immediately write enable flag + initial pulse so the native handler
            // sees AutoRepair=1 before the first timer tick.
            _getController()?.PulseAutoRepair();
        }
        else
        {
            _getController()?.ClearAutoRepairPulse();
        }
    }

    public void Start()
    {
        if (!_pulseTimer.IsEnabled)
        {
            _pulseTimer.Start();
        }
    }

    public void Stop()
    {
        if (_pulseTimer.IsEnabled)
        {
            _pulseTimer.Stop();
        }

        _enabled = false;
        try
        {
            _getController()?.ClearAutoRepairPulse();
        }
        catch
        {
            // Cleanup must not block patch restoration.
        }
    }

    private void OnPulseTimerTick(object? sender, EventArgs e)
    {
        if (!_enabled || !_arePatchesInstalled() || _getController() is null)
        {
            return;
        }

        try
        {
            _getController()!.PulseAutoRepair();
        }
        catch (Exception ex)
        {
            _setStatus($"自动修复脉冲写入失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        _pulseTimer.Stop();
        _pulseTimer.Tick -= OnPulseTimerTick;
    }
}
