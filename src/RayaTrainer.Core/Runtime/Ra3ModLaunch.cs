namespace RayaTrainer.Core.Runtime;

public sealed record Ra3ModEntry(string Name, string Version, string SkudefPath, string? GameVersion)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Version)
        ? Name
        : $"{Name} {Version}";
}

public sealed record Ra3DirectLaunchPlan(
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string CommandLine);

public static class Ra3ModCatalog
{
    public static IReadOnlyList<Ra3ModEntry> Load(string modsRootPath)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath) || !Directory.Exists(modsRootPath))
        {
            return Array.Empty<Ra3ModEntry>();
        }

        return Directory.EnumerateFiles(modsRootPath, "*.skudef", SearchOption.AllDirectories)
            .Select(CreateEntry)
            .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Ra3ModEntry CreateEntry(string skudefPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(skudefPath);
        var separator = fileName.LastIndexOf('_');
        var name = separator > 0 ? fileName[..separator] : fileName;
        var version = separator > 0 && separator < fileName.Length - 1
            ? fileName[(separator + 1)..]
            : string.Empty;
        return new Ra3ModEntry(name, version, skudefPath, Ra3Skudef.ReadItem(skudefPath, "mod-game"));
    }
}

public static class Ra3DirectLaunchPlanner
{
    public static Ra3DirectLaunchPlan Create(string gameRootPath, string modSkudefPath, string extraArguments)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath))
        {
            throw new ArgumentException("Game root path is required.", nameof(gameRootPath));
        }

        var hasModConfig = !string.IsNullOrWhiteSpace(modSkudefPath);
        if (hasModConfig && !File.Exists(modSkudefPath))
        {
            throw new FileNotFoundException("MOD skudef file was not found.", modSkudefPath);
        }

        var resolvedGameRoot = Path.GetFullPath(gameRootPath);
        var requestedVersion = hasModConfig ? Ra3Skudef.ReadItem(modSkudefPath, "mod-game") : null;
        var configPath = FindGameConfig(resolvedGameRoot, requestedVersion);
        var gameExeValue = Ra3Skudef.ReadItem(configPath, "set-exe");
        if (string.IsNullOrWhiteSpace(gameExeValue))
        {
            throw new InvalidOperationException($"Game config does not contain set-exe: {configPath}");
        }

        var executablePath = Path.IsPathRooted(gameExeValue)
            ? gameExeValue
            : Path.GetFullPath(Path.Combine(resolvedGameRoot, gameExeValue));

        var arguments = hasModConfig
            ? JoinArguments(
                extraArguments,
                "-modConfig",
                Quote(modSkudefPath),
                "-config",
                Quote(configPath))
            : JoinArguments(
                extraArguments,
                "-config",
                Quote(configPath));
        var commandLine = JoinArguments(Quote(executablePath), arguments);

        return new Ra3DirectLaunchPlan(executablePath, arguments, resolvedGameRoot, commandLine);
    }

    private static string FindGameConfig(string gameRootPath, string? requestedVersion)
    {
        var configs = Directory.EnumerateFiles(gameRootPath, "ra3_*_*.skudef", SearchOption.TopDirectoryOnly)
            .Select(path => new GameConfigCandidate(path, GetConfigVersion(path)))
            .ToArray();

        if (configs.Length == 0)
        {
            throw new FileNotFoundException("No RA3 skudef config was found.", gameRootPath);
        }

        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            var matched = configs.FirstOrDefault(config =>
                string.Equals(config.VersionText, requestedVersion, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                return matched.Path;
            }

            throw new FileNotFoundException($"No RA3 skudef config matched version {requestedVersion}.", gameRootPath);
        }

        return configs
            .OrderByDescending(config => config.Version)
            .ThenByDescending(config => config.VersionText, StringComparer.OrdinalIgnoreCase)
            .First()
            .Path;
    }

    private static string GetConfigVersion(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var separator = fileName.LastIndexOf('_');
        return separator >= 0 && separator < fileName.Length - 1
            ? fileName[(separator + 1)..]
            : string.Empty;
    }

    private static string JoinArguments(params string?[] parts)
    {
        return string.Join(
            " ",
            parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record GameConfigCandidate(string Path, string VersionText)
    {
        public Version Version { get; } = Version.TryParse(VersionText, out var version)
            ? version
            : new Version(0, 0);
    }
}

internal static class Ra3Skudef
{
    public static string? ReadItem(string path, string itemName)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.StartsWith(itemName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[itemName.Length..].Trim();
            return value.Length == 0 ? null : value.Trim('"');
        }

        return null;
    }
}
