using System;
using System.Collections.Generic;
using System.Globalization;

namespace RayaTrainer.Core.RuntimeAssets.AttributeModifiers;

// AscensionQuantizer — the managed-side bidirectional bit-weight quantizer for the Ascension
// matrix page (design doc 2026-08-17, scheme A). Maps an arbitrary multiplier input onto the
// two frozen ladders planned for asset pack v4:
//
//   * Additive ladder (aggregate = 1 + signed increment): positive integer weights +1..+64 plus
//     negative binary fractions -1/2..-1/128. Mixing both directions represents any multiplier
//     that is a multiple of 1/128 within [1/128, 128] (e.g. x1.5 = +1 -1/2, x2.75 = +4 -1/4).
//     Quantization snaps the input to the nearest 1/128 step; the decomposition minimizes
//     template count by preferring the ceiling integer weight then subtracting the deficit.
//   * Multiplicative ladder (SPEED only, confirmed multiplicative aggregate): factors collapse
//     under multiplication, so only signed powers of two are expressible; the input snaps to the
//     nearest 2^e with e in [-7, 7].
//
// This class is UI/validation-side only in slice 1: it never resolves template names (the v4
// templates do not exist yet). The Native mirror lands together with the asset pack extension.
public sealed record AscensionQuantization(
    bool IsValid,
    string Message,
    double AchievedMultiplier,
    IReadOnlyList<string> Weights,
    IReadOnlyList<double> WeightValues)
{
    public int TemplateCount => Weights.Count;
}

public static class AscensionQuantizer
{
    public const double MaxMultiplier = 128.0;
    public const double MinMultiplier = 1.0 / 128.0;
    private const int FractionalBits = 7; // 1/2 .. 1/128
    private const int Scale = 1 << FractionalBits;

    private static readonly int[] PositiveWeights = [1, 2, 4, 8, 16, 32, 64];
    private static readonly (int Units, string Label)[] NegativeWeights =
    [
        (64, "-1/2"), (32, "-1/4"), (16, "-1/8"), (8, "-1/16"), (4, "-1/32"), (2, "-1/64"), (1, "-1/128"),
    ];

    public static AscensionQuantization Quantize(AscensionAggregation aggregation, string? input)
    {
        return Quantize(aggregation, input, invert: false);
    }

    // invert=true models the Down direction: the entered factor is applied as its reciprocal
    // (Down x2 => effective x0.5), so toggling Up/Down changes the quantization preview.
    public static AscensionQuantization Quantize(AscensionAggregation aggregation, string? input, bool invert)
    {
        if (!TryParseMultiplier(input, out var multiplier, out var parseError))
        {
            return Invalid(parseError);
        }

        if (invert)
        {
            multiplier = 1.0 / multiplier;
        }

        if (multiplier <= 0.0)
        {
            return Invalid("倍率必须大于 0。");
        }
        if (multiplier > MaxMultiplier)
        {
            return Invalid($"倍率超出上限 ×{Format(MaxMultiplier)}。");
        }
        if (multiplier < MinMultiplier)
        {
            return Invalid($"倍率低于下限 ×1/128（≈{Format(MinMultiplier)}）。");
        }

        return aggregation == AscensionAggregation.Multiplicative
            ? QuantizeMultiplicative(multiplier)
            : QuantizeAdditive(multiplier);
    }

    private static AscensionQuantization Invalid(string message) =>
        new(false, message, 1.0, Array.Empty<string>(), Array.Empty<double>());

    // Accepts "3", "x3", "×3", "1.5", "0.5", "50%" (percent converts to multiplier / 100).
    public static bool TryParseMultiplier(string? input, out double multiplier, out string error)
    {
        multiplier = 1.0;
        error = string.Empty;
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            error = "请输入倍率。";
            return false;
        }

        var isPercent = false;
        if (text.EndsWith('%'))
        {
            isPercent = true;
            text = text[..^1].TrimEnd();
        }
        if (text.Length > 0 && (text[0] is 'x' or 'X' or '×'))
        {
            text = text[1..].TrimStart();
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            double.IsNaN(value) || double.IsInfinity(value))
        {
            error = "无法解析为数字（例如 3、1.5、0.5 或 50%）。";
            return false;
        }

        multiplier = isPercent ? value / 100.0 : value;
        return true;
    }

    private static AscensionQuantization QuantizeAdditive(double multiplier)
    {
        // Snap to the nearest 1/128 step; k is the increment in units of 1/128.
        var k = (long)Math.Round((multiplier - 1.0) * Scale, MidpointRounding.AwayFromZero);
        if (k == 0)
        {
            return Invalid("倍率 ×1 等效无修正，无需应用。");
        }

        var weights = new List<string>(8);
        var values = new List<double>(8);
        if (k > 0)
        {
            // Ceiling integer weight minus the fractional deficit: minimizes template count and
            // reuses the exact same negative-fraction ladder as the Down direction.
            var positiveUnits = (int)((k + Scale - 1) / Scale);
            AppendPositiveWeights(positiveUnits, weights, values);
            var deficit = (int)(positiveUnits * (long)Scale - k);
            AppendNegativeWeights(deficit, weights, values);
        }
        else
        {
            AppendNegativeWeights((int)-k, weights, values);
        }

        var achieved = 1.0 + k / (double)Scale;
        return new AscensionQuantization(
            true,
            $"量化为 ×{Format(achieved)}（位权 {string.Join(" ", weights)}，{weights.Count} 个模板）",
            achieved,
            weights,
            values);
    }

    private static AscensionQuantization QuantizeMultiplicative(double multiplier)
    {
        var exponent = (int)Math.Round(Math.Log2(multiplier), MidpointRounding.AwayFromZero);
        exponent = Math.Clamp(exponent, -FractionalBits, FractionalBits);
        if (exponent == 0)
        {
            return Invalid("速度为乘法聚合，最近的 2 的幂档位是 ×1（无修正）；请输入更大或更小的倍率。");
        }

        var achieved = Math.Pow(2.0, exponent);
        var label = exponent > 0 ? $"×{1 << exponent}" : $"×1/{1 << -exponent}";
        var note = Math.Abs(achieved - multiplier) > 1e-9 ? "（就近取档）" : string.Empty;
        return new AscensionQuantization(
            true,
            $"量化为 {label}{note}（速度仅支持 2 的幂档位）",
            achieved,
            new[] { label },
            new[] { achieved });
    }

    // Template value for a positive integer weight w is (1 + w): the engine aggregates additive
    // modifiers as 1 + sum(increments), so +1 maps to a 200% template.
    private static void AppendPositiveWeights(int units, List<string> weights, List<double> values)
    {
        foreach (var weight in PositiveWeights)
        {
            if ((units & weight) != 0)
            {
                weights.Add($"+{weight}");
                values.Add(1.0 + weight);
            }
        }
    }

    // Template value for a negative binary fraction -1/K is -(1.0/K) (a negative Percentage);
    // fractionUnits is expressed in 1/128 steps (64 units == -1/2).
    private static void AppendNegativeWeights(int units, List<string> weights, List<double> values)
    {
        foreach (var (fractionUnits, label) in NegativeWeights)
        {
            if ((units & fractionUnits) != 0)
            {
                weights.Add(label);
                values.Add(-fractionUnits / (double)Scale);
            }
        }
    }

    public static string Format(double value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
