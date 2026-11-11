namespace RayaTrainer.Core.Features;

public sealed record ResourceValueSettings
{
    public const int DefaultMoneyAmount = 100000;
    public const int DefaultPowerValue = 100000;
    public const int DefaultScPointValue = 15;
    public const int MinResourceValue = 1;
    public const int MaxResourceValue = 99999999;
    public const int MinScPointValue = 0;
    public const int MaxScPointValue = 15;

    public static ResourceValueSettings Default { get; } =
        new(DefaultMoneyAmount, DefaultPowerValue, DefaultScPointValue);

    public ResourceValueSettings(int moneyAmount, int powerValue, int scPointValue)
    {
        ValidateRange(moneyAmount, MinResourceValue, MaxResourceValue, nameof(moneyAmount));
        ValidateRange(powerValue, MinResourceValue, MaxResourceValue, nameof(powerValue));
        ValidateRange(scPointValue, MinScPointValue, MaxScPointValue, nameof(scPointValue));

        MoneyAmount = moneyAmount;
        PowerValue = powerValue;
        ScPointValue = scPointValue;
    }

    public int MoneyAmount { get; }

    public int PowerValue { get; }

    public int ScPointValue { get; }

    public static ResourceValueSettings Parse(string moneyAmountText, string powerValueText, string scPointValueText)
    {
        return new ResourceValueSettings(
            ParseInt(moneyAmountText, nameof(moneyAmountText)),
            ParseInt(powerValueText, nameof(powerValueText)),
            ParseInt(scPointValueText, nameof(scPointValueText)));
    }

    private static int ParseInt(string text, string parameterName)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var value))
        {
            throw new FormatException($"{parameterName} must be a decimal integer.");
        }

        return value;
    }

    private static void ValidateRange(int value, int min, int max, string parameterName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Value must be between {min} and {max}.");
        }
    }
}
