using System;
using System.Collections.Generic;

using RayaTrainer.Core.Features;

namespace RayaTrainer.Core.RuntimeAssets.AttributeModifiers;

// AttributeBitWeightCodec — the managed projection of the custom-multiplier binary mask contract.
// RATE_OF_FIRE / DAMAGE_MULT / HEALTH_MULT / RANGE aggregate as (1 + additiveSum) x product, so an
// integer multiplier N maps to the additive increment N-1 decomposed over the power-of-two template
// weights (+1/+2/+4/+8/+16/+32/+64), covering every integer in 2..128. SPEED aggregates
// multiplicatively, so only single factors 2/4/8/16/32/64/128 are expressible (products of factors
// collide and are not uniquely decodable). The Native Agent mirrors this contract on
// ModifierTemplateId; AttributeBitWeightCodecTests lock the name table so the two cannot drift.
public enum AttributeBitWeightKind
{
    RateOfFire = 0,
    DamageMult = 1,
    HealthMult = 2,
    Range = 3,
    Speed = 4,
}

public static class AttributeBitWeightCodec
{
    public const int MaxMultiplier = 128;

    // Additive bit weights -> asset template names (frozen in asset-manifest.json).
    private static readonly IReadOnlyDictionary<int, string> RateWeights = new Dictionary<int, string>
    {
        [1] = "Atlas_RateOfFire_200",
        [2] = "Atlas_RateOfFire_300",
        [4] = "Atlas_RateOfFire_500",
        [8] = "Atlas_RateOfFire_900",
        [16] = "Atlas_RateOfFire_1700",
        [32] = "Atlas_RateOfFire_3300",
        [64] = "Atlas_RateOfFire_6500",
    };

    private static readonly IReadOnlyDictionary<int, string> DamageWeights = new Dictionary<int, string>
    {
        [1] = "Atlas_DamageMult_200",
        [2] = "Atlas_DamageMult_300",
        [4] = "Atlas_DamageMult_500",
        [8] = "Atlas_DamageMult_900",
        [16] = "Atlas_DamageMult_1700",
        [32] = "Atlas_DamageMult_3300",
        [64] = "Atlas_DamageMult_6500",
    };

    private static readonly IReadOnlyDictionary<int, string> HealthWeights = new Dictionary<int, string>
    {
        [1] = "Atlas_HealthMult_200",
        [2] = "Atlas_HealthMult_300",
        [4] = "Atlas_HealthMult_500",
        [8] = "Atlas_HealthMult_900",
        [16] = "Atlas_HealthMult_1700",
        [32] = "Atlas_HealthMult_3300",
        [64] = "Atlas_HealthMult_6500",
    };

    private static readonly IReadOnlyDictionary<int, string> RangeWeights = new Dictionary<int, string>
    {
        [1] = "Atlas_Range_200",
        [2] = "Atlas_Range_300",
        [4] = "Atlas_Range_500",
        [8] = "Atlas_Range_900",
        [16] = "Atlas_Range_1700",
        [32] = "Atlas_Range_3300",
        [64] = "Atlas_Range_6500",
    };

    // RANGE-aligned VISION bit-weight templates (same increment structure, same symmetric weights).
    private static readonly IReadOnlyDictionary<int, string> VisionWeights = new Dictionary<int, string>
    {
        [1] = "Atlas_Vision_200",
        [2] = "Atlas_Vision_300",
        [4] = "Atlas_Vision_500",
        [8] = "Atlas_Vision_900",
        [16] = "Atlas_Vision_1700",
        [32] = "Atlas_Vision_3300",
        [64] = "Atlas_Vision_6500",
    };

    // Multiplicative SPEED factors -> asset template names (single-factor selection only).
    private static readonly IReadOnlyDictionary<int, string> SpeedFactors = new Dictionary<int, string>
    {
        [2] = "Atlas_Speed_200",
        [4] = "Atlas_Speed_400",
        [8] = "Atlas_Speed_800",
        [16] = "Atlas_Speed_1600",
        [32] = "Atlas_Speed_3200",
        [64] = "Atlas_Speed_6400",
        [128] = "Atlas_Speed_12800",
    };

    // Decompose an integer multiplier into the template names to load. Fails closed with a readable
    // error for out-of-range input; SPEED rejects every non-power-of-two factor.
    public static bool TryCompose(
        AttributeBitWeightKind kind,
        int multiplier,
        out IReadOnlyList<string> templateNames,
        out string error)
    {
        templateNames = Array.Empty<string>();
        error = string.Empty;

        if (kind == AttributeBitWeightKind.Speed)
        {
            if (SpeedFactors.TryGetValue(multiplier, out var factorName))
            {
                templateNames = new[] { factorName };
                return true;
            }
            error = $"速度倍率仅支持 2 的幂（2/4/8/16/32/64/128），收到 {multiplier}。";
            return false;
        }

        if (multiplier < 2 || multiplier > MaxMultiplier)
        {
            error = $"倍率必须在 2 到 {MaxMultiplier} 之间，收到 {multiplier}。";
            return false;
        }

        var weights = kind switch
        {
            AttributeBitWeightKind.RateOfFire => RateWeights,
            AttributeBitWeightKind.DamageMult => DamageWeights,
            AttributeBitWeightKind.HealthMult => HealthWeights,
            AttributeBitWeightKind.Range => RangeWeights,
            _ => null,
        };
        if (weights is null)
        {
            error = $"未知属性类型：{kind}。";
            return false;
        }

        // Additive attributes: multiplier N == baseline 1 + increment (N-1); decompose the increment
        // over the power-of-two weights, ascending so the load order matches the catalog order.
        var increment = multiplier - 1;
        var composed = new List<string>(7);
        foreach (var weight in new[] { 1, 2, 4, 8, 16, 32, 64 })
        {
            if ((increment & weight) != 0)
            {
                composed.Add(weights[weight]);
            }
        }

        templateNames = composed;
        return composed.Count > 0;
    }

    // Maps a custom-multiplier Product Intent feature RawName to the attribute kind it modifies;
    // non-custom features resolve to false. The App uses this to pre-validate the shared integer
    // multiplier input with the exact same contract the Native route enforces.
    public static bool TryGetCustomMultiplierKind(string featureRawName, out AttributeBitWeightKind kind)
    {
        switch (featureRawName)
        {
            case TrainerFeatureIds.ProductAttackSpeedCustom:
                kind = AttributeBitWeightKind.RateOfFire;
                return true;
            case TrainerFeatureIds.ProductAttackRangeCustom:
                kind = AttributeBitWeightKind.Range;
                return true;
            case TrainerFeatureIds.ProductAttackDamageCustom:
                kind = AttributeBitWeightKind.DamageMult;
                return true;
            case TrainerFeatureIds.ProductMaxHealthCustom:
                kind = AttributeBitWeightKind.HealthMult;
                return true;
            case TrainerFeatureIds.ProductMoveSpeedCustom:
                kind = AttributeBitWeightKind.Speed;
                return true;
            default:
                kind = AttributeBitWeightKind.RateOfFire;
                return false;
        }
    }
}
