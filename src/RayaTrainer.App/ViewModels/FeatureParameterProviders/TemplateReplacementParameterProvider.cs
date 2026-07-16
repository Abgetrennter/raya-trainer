namespace RayaTrainer.App.ViewModels.FeatureParameterProviders;

/// <summary>
/// 模板替换共享目标/来源 provider（UiOnly）。
/// 两个替换动作（模型替换/武器替换）共享同一对输入。
/// </summary>
public sealed class TemplateReplacementParameterProvider : IFeatureParameterProvider
{
    private readonly Func<(string TargetUnitId, string DonorUnitId)> _capture;
    private readonly Action<string, string> _writeBack;

    public TemplateReplacementParameterProvider(
        Func<(string, string)> capture,
        Action<string, string> writeBack)
    {
        _capture = capture;
        _writeBack = writeBack;
    }

    public string ProviderId => "templateReplacement";
    public IReadOnlyCollection<string> ParameterIds =>
        new[] { "templateReplacement.targetUnitId", "templateReplacement.donorUnitId" };


    public IReadOnlyDictionary<string, string> CaptureValidated()
    {
        var (target, donor) = _capture();
        var dict = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(target))
            dict["templateReplacement.targetUnitId"] = target.Trim();
        if (!string.IsNullOrWhiteSpace(donor))
            dict["templateReplacement.donorUnitId"] = donor.Trim();
        return dict;
    }

    public ParameterRestoreResult RestoreValidated(
        IReadOnlyDictionary<string, string> values,
        bool suppressRuntimeApply)
    {
        var applied = new List<string>();
        string target = "", donor = "";
        bool any = false;

        if (values.TryGetValue("templateReplacement.targetUnitId", out var t))
        {
            target = t; applied.Add("templateReplacement.targetUnitId"); any = true;
        }
        if (values.TryGetValue("templateReplacement.donorUnitId", out var d))
        {
            donor = d; applied.Add("templateReplacement.donorUnitId"); any = true;
        }

        if (any)
        {
            _writeBack(target, donor);
        }

        return new ParameterRestoreResult(applied, Array.Empty<string>(), Array.Empty<string>());
    }
}
