using System.Globalization;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels.FeatureParameterProviders;

/// <summary>
/// 适配既有 ResourceValueSettings 的 provider。
/// 捕获/恢复三个资源参数（moneyAmount/powerValue/scPointValue）。
/// 当前值真相源仍是 ResourceValueSettings（通过 writeBack 回写）。
/// </summary>
public sealed class ResourceParameterProvider : IFeatureParameterProvider
{
    private readonly Func<ResourceValueSettings> _capture;
    private readonly Action<ResourceValueSettings>? _writeBack;
    private readonly ResourceValueSettings _lastValid;

    public ResourceParameterProvider(
        Func<ResourceValueSettings> capture,
        Action<ResourceValueSettings>? writeBack = null,
        ResourceValueSettings? lastValid = null)
    {
        _capture = capture;
        _writeBack = writeBack;
        _lastValid = lastValid ?? ResourceValueSettings.Default;
    }

    public string ProviderId => "resources";
    public IReadOnlyCollection<string> ParameterIds =>
        new[] { "resources.moneyAmount", "resources.powerValue", "resources.scPointValue" };


    public IReadOnlyDictionary<string, string> CaptureValidated()
    {
        ResourceValueSettings current;
        try
        {
            current = _capture();
        }
        catch (Exception)
        {
            current = _lastValid;
        }

        return new Dictionary<string, string>
        {
            ["resources.moneyAmount"] = current.MoneyAmount.ToString(CultureInfo.InvariantCulture),
            ["resources.powerValue"] = current.PowerValue.ToString(CultureInfo.InvariantCulture),
            ["resources.scPointValue"] = current.ScPointValue.ToString(CultureInfo.InvariantCulture)
        };
    }

    public ParameterRestoreResult RestoreValidated(
        IReadOnlyDictionary<string, string> values,
        bool suppressRuntimeApply)
    {
        var applied = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();

        int money = -1, power = -1, sc = -1;
        bool anyParsed = false;

        if (values.TryGetValue("resources.moneyAmount", out var m) &&
            int.TryParse(m, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mi))
        {
            var def = FeatureParameterCatalog.TryFind("resources.moneyAmount")!;
            if (def.Validate(m)) { money = mi; applied.Add("resources.moneyAmount"); anyParsed = true; }
            else skipped.Add("resources.moneyAmount");
        }
        if (values.TryGetValue("resources.powerValue", out var p) &&
            int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pi))
        {
            var def = FeatureParameterCatalog.TryFind("resources.powerValue")!;
            if (def.Validate(p)) { power = pi; applied.Add("resources.powerValue"); anyParsed = true; }
            else skipped.Add("resources.powerValue");
        }
        if (values.TryGetValue("resources.scPointValue", out var s) &&
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si))
        {
            var def = FeatureParameterCatalog.TryFind("resources.scPointValue")!;
            if (def.Validate(s)) { sc = si; applied.Add("resources.scPointValue"); anyParsed = true; }
            else skipped.Add("resources.scPointValue");
        }

        if (anyParsed)
        {
            ResourceValueSettings current;
            try { current = _capture(); }
            catch { current = _lastValid; }

            try
            {
                var settings = new ResourceValueSettings(
                    money >= 0 ? money : current.MoneyAmount,
                    power >= 0 ? power : current.PowerValue,
                    sc >= 0 ? sc : current.ScPointValue);
                _writeBack?.Invoke(settings);
            }
            catch (ArgumentOutOfRangeException)
            {
                errors.AddRange(applied);
                applied.Clear();
            }
        }

        return new ParameterRestoreResult(applied, skipped, errors);
    }
}
