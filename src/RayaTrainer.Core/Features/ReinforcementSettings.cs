namespace RayaTrainer.Core.Features;

public sealed record ReinforcementSettings
{
    public const uint DefaultUnitId = 0x6586A5A0;
    public const int DefaultCount = 8;
    public const int DefaultRank = 3;
    public const int MinCount = 1;
    public const int MaxCount = 50;
    public const int MinRank = 0;
    public const int MaxRank = 3;

    public static ReinforcementSettings Default { get; } =
        new(DefaultUnitId, DefaultCount, DefaultRank);

    public static ReinforcementSettings Parse(string unitIdText, string countText, string rankText)
    {
        var unitId = UnitCodeParser.Parse(unitIdText);
        var count = ParseDecimalInt(countText, nameof(countText));
        var rank = ParseDecimalInt(rankText, nameof(rankText));
        return new ReinforcementSettings(unitId, count, rank);
    }

    public ReinforcementSettings(uint unitId, int count, int rank)
    {
        if (unitId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitId), "Unit id must be non-zero.");
        }
        if (count is < MinCount or > MaxCount)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"Count must be between {MinCount} and {MaxCount}.");
        }
        if (rank is < MinRank or > MaxRank)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), $"Rank must be between {MinRank} and {MaxRank}.");
        }

        UnitId = unitId;
        Count = count;
        Rank = rank;
    }

    public uint UnitId { get; }

    public int Count { get; }

    public int Rank { get; }

    private static int ParseDecimalInt(string text, string parameterName)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var value))
        {
            throw new FormatException($"{parameterName} must be a decimal integer.");
        }

        return value;
    }
}
