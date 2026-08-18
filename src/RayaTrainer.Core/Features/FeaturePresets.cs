namespace RayaTrainer.Core.Features;

/// <summary>
/// 功能状态快照：toggle 期望状态 + 规范化参数值。
/// 用于实时保存（LastFeatureState 等价）与命名预设。
/// </summary>
public sealed record FeatureStateSnapshot(
    IReadOnlyDictionary<string, bool> ToggleStates,
    IReadOnlyDictionary<string, string> ParameterValues)
{
    public static FeatureStateSnapshot Empty { get; } =
        new(new Dictionary<string, bool>(), new Dictionary<string, string>());
}

/// <summary>
/// 命名功能预设。ParameterValues 是快照副本（可含从 ResourceValues 投影的资源参数），
/// 不构成当前值的第二真相源。
/// </summary>
public sealed record FeaturePreset(
    string Name,
    FeatureStateSnapshot Snapshot,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
