using RayaTrainer.App.Services;
using RayaTrainer.Host.Services;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

// MainViewModel 的预设域 partial：功能预设 CRUD、快照应用与 R3/P3 控制台投影发布。
// 拆分自 god-file（Debt 4），沿用 MainViewModel.Launch.cs 的 partial 模式。
public sealed partial class MainViewModel
{
    public IReadOnlyList<ReinforcementPreset> GetReinforcementPresets() => Reinforcement.GetReinforcementPresetsSnapshot();

    public IReadOnlyList<SecretProtocolQueuePreset> GetSecretProtocolPresets() => SecretProtocol.GetSecretProtocolPresetsSnapshot();

    public IReadOnlyList<FeaturePreset> GetFeaturePresets() => _featurePresets.ToList();
    public IReadOnlyList<FeaturePreset> FeaturePresets => _featurePresets;
    public string? LastAppliedFeaturePresetName { get; private set; }

    /// <summary>
    /// UI 入口（Task 4）— 捕获当前状态作为快照保存
    /// </summary>
    public void SaveFeaturePreset(string name) =>
        SaveFeaturePreset(name, FeatureState.CaptureSnapshot());

    /// <summary>
    /// 接口实现（Task 6）— 接收外部 snapshot（Web 传快照）
    /// </summary>
    public void SaveFeaturePreset(string name, FeatureStateSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        var existingIndex = NamedPresetList.FindIndex(_featurePresets, name);
        var preset = existingIndex >= 0
            ? _featurePresets[existingIndex] with { Snapshot = snapshot, UpdatedAtUtc = now }
            : new FeaturePreset(name, snapshot, now, now);
        NamedPresetList.Upsert(_featurePresets, preset);
        Persistence?.MarkDirty();
    }

    public SnapshotApplyResult ApplyFeaturePreset(string name)
    {
        var presetIndex = NamedPresetList.FindIndex(_featurePresets, name);
        if (presetIndex < 0)
            return new SnapshotApplyResult(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var preset = _featurePresets[presetIndex];
        var result = FeatureState.ApplySnapshot(preset.Snapshot, suppressRuntimeApply: false);
        LastAppliedFeaturePresetName = name;
        Persistence?.MarkDirty();
        return result;
    }

    public bool RenameFeaturePreset(string oldName, string newName)
    {
        var idx = NamedPresetList.FindIndex(_featurePresets, oldName);
        if (idx < 0) return false;
        if (NamedPresetList.ContainsName(_featurePresets, newName, idx))
            return false; // 新名冲突
        var preset = _featurePresets[idx];
        _featurePresets[idx] = preset with { Name = newName, UpdatedAtUtc = DateTimeOffset.UtcNow };
        if (LastAppliedFeaturePresetName?.Equals(oldName, StringComparison.OrdinalIgnoreCase) == true)
            LastAppliedFeaturePresetName = newName;
        Persistence?.MarkDirty();
        return true;
    }

    public bool DeleteFeaturePreset(string name)
    {
        var removed = NamedPresetList.RemoveByName(_featurePresets, name);
        if (removed)
        {
            if (LastAppliedFeaturePresetName?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                LastAppliedFeaturePresetName = null;
            Persistence?.MarkDirty();
        }
        return removed;
    }

    // R3: 把当前完整预设快照推给会话层投影协调器（Agent 未就绪时缓存，就绪后自动同步）。
    private void PublishReinforcementProjection() =>
        (_sessionManager as IReinforcementProjectionPublisher)
            ?.PublishReinforcementPresets(Reinforcement.GetReinforcementPresetsSnapshot());

    // P3: 秘密协议预设快照同样推给独立的第二个投影协调器。
    private void PublishSecretProtocolProjection() =>
        (_sessionManager as ISecretProtocolProjectionPublisher)
            ?.PublishSecretProtocolPresets(SecretProtocol.GetSecretProtocolPresetsSnapshot());
}
