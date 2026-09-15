using RayaTrainer.Core.Features;

namespace RayaTrainer.App.ViewModels.FeatureParameterProviders;

/// <summary>
/// 选中单位自定义倍率 provider：攻速/射程/伤害/生命/移速各一个独立输入。
/// 捕获时省略空串 key（不覆盖默认）；恢复时逐项校验后写回对应文本属性。
/// </summary>
public sealed class SelectedUnitMultiplierParameterProvider : IFeatureParameterProvider
{
    public const string AttackSpeedId = "selectedUnit.multiplier.attackSpeed";
    public const string AttackRangeId = "selectedUnit.multiplier.attackRange";
    public const string AttackDamageId = "selectedUnit.multiplier.attackDamage";
    public const string MaxHealthId = "selectedUnit.multiplier.maxHealth";
    public const string MoveSpeedId = "selectedUnit.multiplier.moveSpeed";

    private static readonly string[] AllIds =
        [AttackSpeedId, AttackRangeId, AttackDamageId, MaxHealthId, MoveSpeedId];

    private readonly Func<IReadOnlyDictionary<string, string>> _capture;
    private readonly Action<IReadOnlyDictionary<string, string>> _writeBack;

    public SelectedUnitMultiplierParameterProvider(
        Func<IReadOnlyDictionary<string, string>> capture,
        Action<IReadOnlyDictionary<string, string>> writeBack)
    {
        _capture = capture;
        _writeBack = writeBack;
    }

    public string ProviderId => "selectedUnitMultiplier";
    public IReadOnlyCollection<string> ParameterIds => AllIds;

    public IReadOnlyDictionary<string, string> CaptureValidated()
    {
        var captured = _capture();
        var dict = new Dictionary<string, string>();
        foreach (var id in AllIds)
        {
            if (!captured.TryGetValue(id, out var text))
            {
                continue;
            }

            var trimmed = text.Trim();
            var definition = FeatureParameterCatalog.TryFind(id);
            if (trimmed.Length > 0 && definition is not null && definition.Validate(trimmed))
            {
                dict[id] = trimmed;
            }
        }

        return dict;
    }

    public ParameterRestoreResult RestoreValidated(
        IReadOnlyDictionary<string, string> values,
        bool suppressRuntimeApply)
    {
        var applied = new List<string>();
        var skipped = new List<string>();
        var restore = new Dictionary<string, string>();

        foreach (var id in AllIds)
        {
            if (!values.TryGetValue(id, out var text))
            {
                continue;
            }

            var definition = FeatureParameterCatalog.TryFind(id);
            if (definition is not null && definition.Validate(text))
            {
                restore[id] = text;
                applied.Add(id);
            }
            else
            {
                skipped.Add(id);
            }
        }

        if (restore.Count > 0)
        {
            _writeBack(restore);
        }

        return new ParameterRestoreResult(applied, skipped, Array.Empty<string>());
    }
}
