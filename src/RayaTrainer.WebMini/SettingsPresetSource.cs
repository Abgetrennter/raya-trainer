using RayaTrainer.Core.Features;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Host.Web;

namespace RayaTrainer.WebMini;

/// <summary>
/// 基于设置文件的预设源：WebMini 没有桌面 ViewModel，直接读
/// <see cref="TrainerAppSettings"/> 里的增援/秘密协议/功能预设。
/// 功能预设的保存/删除写回同一设置文件（与主程序共享同一份预设数据）。
/// </summary>
public sealed class SettingsPresetSource : ITrainerPresetSource
{
    private readonly TrainerAppSettingsStore _store;
    private readonly object _gate = new();
    private TrainerAppSettings _settings;

    public SettingsPresetSource(TrainerAppSettingsStore store)
    {
        _store = store;
        _settings = store.Load();
    }

    public IReadOnlyList<ReinforcementPreset> GetReinforcementPresets()
    {
        lock (_gate)
        {
            return _settings.ReinforcementPresets.ToArray();
        }
    }

    public IReadOnlyList<SecretProtocolQueuePreset> GetSecretProtocolPresets()
    {
        lock (_gate)
        {
            return _settings.SecretProtocolPresets.ToArray();
        }
    }

    public IReadOnlyList<FeaturePreset> GetFeaturePresets()
    {
        lock (_gate)
        {
            return _settings.FeaturePresets.ToArray();
        }
    }

    public void SaveFeaturePreset(string name, FeatureStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = _settings.FeaturePresets.FirstOrDefault(
                p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var preset = existing is null
                ? new FeaturePreset(name, snapshot, now, now)
                : existing with { Snapshot = snapshot, UpdatedAtUtc = now };

            var updated = _settings.FeaturePresets.Where(
                    p => !p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Append(preset)
                .ToArray();
            Persist(_settings with { FeaturePresets = updated });
        }
    }

    public bool DeleteFeaturePreset(string name)
    {
        lock (_gate)
        {
            var remaining = _settings.FeaturePresets
                .Where(p => !p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (remaining.Length == _settings.FeaturePresets.Count)
            {
                return false;
            }

            Persist(_settings with { FeaturePresets = remaining });
            return true;
        }
    }

    private void Persist(TrainerAppSettings settings)
    {
        _store.Save(settings);
        _settings = settings;
    }
}
