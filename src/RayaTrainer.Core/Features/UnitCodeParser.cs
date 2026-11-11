using System.Globalization;

namespace RayaTrainer.Core.Features;

public static class UnitCodeParser
{
    public static bool TryParse(string? text, out uint value)
    {
        var normalized = Normalize(text);
        return uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    public static uint Parse(string? text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException("Unit code must be a hexadecimal value.");
        }

        return value;
    }

    public static string Format(uint value)
    {
        return $"0x{value:X8}";
    }

    private static string Normalize(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }
}
