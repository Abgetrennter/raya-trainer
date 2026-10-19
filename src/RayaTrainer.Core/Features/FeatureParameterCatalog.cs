using System.Globalization;

namespace RayaTrainer.Core.Features;

public enum FeatureParameterValueKind
{
    Integer,
    Float,
    String
}

public enum FeatureParameterApplyMode
{
    OnAction,
    BeforeToggleEnable,
    BeforeEnableAndLiveUpdate,
    UiOnly
}

public sealed record FeatureParameterDefinition(
    string Id,
    string OwnerFeatureRawName,
    FeatureParameterValueKind ValueKind,
    FeatureParameterApplyMode ApplyMode,
    string DefaultValue,
    string? MinValue = null,
    string? MaxValue = null,
    string? Unit = null,
    bool IncludeInFeaturePreset = true)
{
    /// <summary>
    /// 校验规范化值是否在范围内（不抛异常）。空/无法解析/越界返回 false。
    /// </summary>
    public bool Validate(string normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue)) return false;

        switch (ValueKind)
        {
            case FeatureParameterValueKind.Integer:
                if (!int.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return false;
                if (MinValue is not null && int.TryParse(MinValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minI) && i < minI) return false;
                if (MaxValue is not null && int.TryParse(MaxValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxI) && i > maxI) return false;
                return true;
            case FeatureParameterValueKind.Float:
                if (!float.TryParse(normalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    return false;
                if (MinValue is not null && float.TryParse(MinValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var minF) && f < minF) return false;
                if (MaxValue is not null && float.TryParse(MaxValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxF) && f > maxF) return false;
                return true;
            default:
                return true;
        }
    }
}

public static class FeatureParameterCatalog
{
    public static IReadOnlyList<FeatureParameterDefinition> Definitions { get; } =
    [
        // 资源值（投影自既有 ResourceValues；当前值存 ResourceValues，不存 FeatureParameterValues）
        new("resources.moneyAmount", TrainerFeatureIds.Money,
            FeatureParameterValueKind.Integer, FeatureParameterApplyMode.OnAction,
            DefaultValue: "100000", MinValue: "1", MaxValue: "99999999"),
        new("resources.powerValue", TrainerFeatureIds.Power,
            FeatureParameterValueKind.Integer, FeatureParameterApplyMode.BeforeEnableAndLiveUpdate,
            DefaultValue: "100000", MinValue: "1", MaxValue: "99999999"),
        new("resources.scPointValue", TrainerFeatureIds.SecretProtocolPoints,
            FeatureParameterValueKind.Integer, FeatureParameterApplyMode.OnAction,
            DefaultValue: "15", MinValue: "0", MaxValue: "15"),

        // 选中单位目标生命值（OnAction，新存 FeatureParameterValues）
        new("selectedUnit.targetHealth.current", TrainerFeatureIds.SetSelectedUnitTargetHealth,
            FeatureParameterValueKind.Float, FeatureParameterApplyMode.OnAction,
            DefaultValue: "1000", MinValue: "0"),
        new("selectedUnit.targetHealth.max", TrainerFeatureIds.SetSelectedUnitTargetHealth,
            FeatureParameterValueKind.Float, FeatureParameterApplyMode.OnAction,
            DefaultValue: "0", MinValue: "0"),

        // 模板替换（UiOnly，共享目标/来源）
        new("templateReplacement.targetUnitId", TrainerFeatureIds.CopySelectedUnit,
            FeatureParameterValueKind.String, FeatureParameterApplyMode.UiOnly,
            DefaultValue: ""),
        new("templateReplacement.donorUnitId", TrainerFeatureIds.CopySelectedUnit,
            FeatureParameterValueKind.String, FeatureParameterApplyMode.UiOnly,
            DefaultValue: ""),

        // 选中单位自定义倍率：每个属性一个独立输入（OnAction）。攻速/射程/伤害/生命接受
        // 2-128 任意整数（二进制位权组合，如 7=4+2+1）；移速为乘法因子，仅 2 的幂可组合，
        // 位权约束由 AttributeBitWeightCodec 在执行时校验。
        new("selectedUnit.multiplier.attackSpeed", TrainerFeatureIds.ProductAttackSpeedCustom,
            FeatureParameterValueKind.Integer, FeatureParameterApplyMode.OnAction,
            DefaultValue: "2", MinValue: "2", MaxValue: "128"),
        new("selectedUnit.multiplier.attackRange", TrainerFeatureIds.ProductAttackRangeCustom,
            FeatureParameterValueKind.Integer, FeatureParameterApplyMode.OnAction,
            DefaultValue: "2", MinValue: "2", MaxValue: "128"),
        new("selectedUnit.multiplier.attackDamage", TrainerFeatureIds.ProductAttackDamageCustom,
            FeatureParameterValueKind.Integer, FeatureParameterApplyMode.OnAction,
            DefaultValue: "2", MinValue: "2", MaxValue: "128"),
        new("selectedUnit.multiplier.maxHealth", TrainerFeatureIds.ProductMaxHealthCustom,
            FeatureParameterValueKind.Integer, FeatureParameterApplyMode.OnAction,
            DefaultValue: "2", MinValue: "2", MaxValue: "128"),
        new("selectedUnit.multiplier.moveSpeed", TrainerFeatureIds.ProductMoveSpeedCustom,
            FeatureParameterValueKind.Integer, FeatureParameterApplyMode.OnAction,
            DefaultValue: "2", MinValue: "2", MaxValue: "128"),
    ];

    public static FeatureParameterDefinition? TryFind(string id) =>
        Definitions.FirstOrDefault(d => d.Id == id);
}
