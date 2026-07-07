using System.Globalization;
using System.Text.RegularExpressions;

namespace RayaTrainer.Core.Patching;

public static partial class OriginalByteParser
{
    public static byte[] Parse(IReadOnlyList<string> originalAssembly)
    {
        if (originalAssembly.Count == 1)
        {
            var line = originalAssembly[0].Trim();
            var match = DbLineRegex().Match(line);
            if (match.Success)
            {
                return match.Groups["bytes"].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                    .ToArray();
            }
        }

        return RestoreAssemblyEncoder.Encode(originalAssembly);
    }

    [GeneratedRegex(@"^db\s+(?<bytes>[0-9A-Fa-f ]+)$")]
    private static partial Regex DbLineRegex();
}
