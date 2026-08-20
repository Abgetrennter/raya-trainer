namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 参数 provider 契约（App 层）。按领域组织，不按页面组织。
/// 自动保存/预设只捕获最后一个有效值；半输入不覆盖。
/// </summary>
public interface IFeatureParameterProvider
{
    string ProviderId { get; }
    IReadOnlyCollection<string> ParameterIds { get; }

    /// <summary>捕获当前有效规范值（invariant 规范化形式）。</summary>
    IReadOnlyDictionary<string, string> CaptureValidated();

    /// <summary>恢复有效值。suppressRuntimeApply=true 时只回填 UI/当前值，不触发 Agent 写入。</summary>
    ParameterRestoreResult RestoreValidated(
        IReadOnlyDictionary<string, string> values,
        bool suppressRuntimeApply);

}

public sealed record ParameterRestoreResult(
    IReadOnlyList<string> AppliedIds,
    IReadOnlyList<string> SkippedIds,
    IReadOnlyList<string> ErrorIds);
