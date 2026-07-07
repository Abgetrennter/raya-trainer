namespace RayaTrainer.App.Services;

public static class ReleaseVersionComparer
{
    public static bool IsNewer(string latestVersion, string currentVersion)
    {
        return TryCompare(latestVersion, currentVersion, out var result) && result > 0;
    }

    public static int Compare(string left, string right)
    {
        if (!TryCompare(left, right, out var result))
        {
            throw new FormatException("Version text must use a vMajor.Minor.Patch or Major.Minor.Patch format.");
        }

        return result;
    }

    public static bool TryCompare(string left, string right, out int result)
    {
        result = 0;
        if (!TryNormalize(left, out var leftVersion) || !TryNormalize(right, out var rightVersion))
        {
            return false;
        }

        result = leftVersion.CompareTo(rightVersion);
        return true;
    }

    private static bool TryNormalize(string value, out Version version)
    {
        version = new Version();
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        if (normalized.Count(c => c == '.') < 2)
        {
            return false;
        }

        return Version.TryParse(normalized, out version!);
    }
}
