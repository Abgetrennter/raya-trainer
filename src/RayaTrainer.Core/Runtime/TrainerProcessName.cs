namespace RayaTrainer.Core.Runtime;

public static class TrainerProcessName
{
    private static readonly string[] ExecutableSuffixes = [".game", ".exe"];

    public static string ToProcessName(string name)
    {
        var fileName = Path.GetFileName(name.Trim());
        foreach (var suffix in ExecutableSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^suffix.Length];
            }
        }

        return fileName;
    }

    public static bool Matches(string left, string right)
    {
        return ToProcessName(left).Equals(ToProcessName(right), StringComparison.OrdinalIgnoreCase);
    }
}
