using System.Globalization;
using RayaTrainer.Core.Features;

namespace RayaTrainer.App.ViewModels.FeatureParameterProviders;

/// <summary>
/// 选中单位目标生命值 provider。
/// 捕获 (current, max) 文本；空串省略 key（不覆盖默认）。
/// </summary>
public sealed class SelectedUnitParameterProvider : IFeatureParameterProvider
{
    private readonly Func<(string HealthText, string MaxHealthText)> _capture;
    private readonly Action<string, string> _writeBack;

    public SelectedUnitParameterProvider(
        Func<(string, string)> capture,
        Action<string, string> writeBack)
    {
        _capture = capture;
        _writeBack = writeBack;
    }

    public string ProviderId => "selectedUnit";
    public IReadOnlyCollection<string> ParameterIds =>
        new[] { "selectedUnit.targetHealth.current", "selectedUnit.targetHealth.max" };


    public IReadOnlyDictionary<string, string> CaptureValidated()
    {
        var (health, max) = _capture();
        var dict = new Dictionary<string, string>();
        var hTrim = health.Trim();
        var mTrim = max.Trim();
        if (float.TryParse(hTrim, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && h > 0)
        {
            var def = FeatureParameterCatalog.TryFind("selectedUnit.targetHealth.current")!;
            if (def.Validate(hTrim))
                dict["selectedUnit.targetHealth.current"] = hTrim;
        }
        if (float.TryParse(mTrim, NumberStyles.Float, CultureInfo.InvariantCulture, out _) &&
            FeatureParameterCatalog.TryFind("selectedUnit.targetHealth.max")!.Validate(mTrim))
        {
            dict["selectedUnit.targetHealth.max"] = mTrim;
        }
        return dict;
    }

    public ParameterRestoreResult RestoreValidated(
        IReadOnlyDictionary<string, string> values,
        bool suppressRuntimeApply)
    {
        var applied = new List<string>();
        var skipped = new List<string>();

        string health = "", max = "";
        bool any = false;

        if (values.TryGetValue("selectedUnit.targetHealth.current", out var h))
        {
            var def = FeatureParameterCatalog.TryFind("selectedUnit.targetHealth.current")!;
            if (def.Validate(h)) { health = h; applied.Add("selectedUnit.targetHealth.current"); any = true; }
            else skipped.Add("selectedUnit.targetHealth.current");
        }
        if (values.TryGetValue("selectedUnit.targetHealth.max", out var m))
        {
            var def = FeatureParameterCatalog.TryFind("selectedUnit.targetHealth.max")!;
            if (def.Validate(m)) { max = m; applied.Add("selectedUnit.targetHealth.max"); any = true; }
            else skipped.Add("selectedUnit.targetHealth.max");
        }

        if (any)
        {
            _writeBack(health, max);
        }

        return new ParameterRestoreResult(applied, skipped, Array.Empty<string>());
    }
}
