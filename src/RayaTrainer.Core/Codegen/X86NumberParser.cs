using System.Globalization;

namespace RayaTrainer.Core.Codegen;

public static class X86NumberParser
{
    public static int ParseInt32(string value)
    {
        return unchecked((int)ParseUInt32(value));
    }

    public static uint ParseUInt32(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
        {
            return uint.Parse(normalized[1..], NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (normalized.EndsWith("h", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^1];
        }

        return uint.Parse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
