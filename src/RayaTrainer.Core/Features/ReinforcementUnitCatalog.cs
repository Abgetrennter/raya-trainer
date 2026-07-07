namespace RayaTrainer.Core.Features;

using System.IO;
using System.Reflection;
using RayaTrainer.Core.Assets;

public static class ReinforcementUnitCatalog
{
    public const string CustomFileName = "RayaTrainer.reinforcements.txt";

    private static readonly Lazy<IReadOnlyList<ReinforcementUnitEntry>> BuiltInUnits =
        new(LoadBuiltInCore);

    public static IReadOnlyList<ReinforcementUnitEntry> Load(string path)
    {
        return Parse(File.ReadLines(path));
    }

    public static IReadOnlyList<ReinforcementUnitEntry> LoadWithCustomFile(string? baseDirectory = null)
    {
        var customPath = CustomPath(baseDirectory);
        var customUnits = File.Exists(customPath)
            ? Load(customPath)
            : Array.Empty<ReinforcementUnitEntry>();

        return Merge(LoadBuiltIn(), customUnits);
    }

    public static ReinforcementUnitImportResult ImportToCustomFile(
        string? baseDirectory,
        IEnumerable<string> lines,
        IEnumerable<ReinforcementUnitEntry> existingUnits)
    {
        var existingKeys = existingUnits
            .Select(UnitKey.FromEntry)
            .ToHashSet();
        var added = new List<ReinforcementUnitEntry>();
        var duplicateCount = 0;
        var invalidCount = 0;

        foreach (var line in lines)
        {
            var parseResult = ParseLine(line);
            if (parseResult.IsIgnorable)
            {
                continue;
            }

            if (parseResult.Entry is null)
            {
                invalidCount++;
                continue;
            }

            if (!existingKeys.Add(UnitKey.FromEntry(parseResult.Entry)))
            {
                duplicateCount++;
                continue;
            }

            added.Add(parseResult.Entry);
        }

        if (added.Count > 0)
        {
            AppendToCustomFile(baseDirectory, added);
        }

        return new ReinforcementUnitImportResult(added, duplicateCount, invalidCount);
    }

    public static IReadOnlyList<ReinforcementUnitEntry> LoadBuiltIn()
    {
        return BuiltInUnits.Value;
    }

    public static IReadOnlyList<ReinforcementUnitEntry> Merge(
        IEnumerable<ReinforcementUnitEntry> primary,
        IEnumerable<ReinforcementUnitEntry> secondary)
    {
        var merged = new List<ReinforcementUnitEntry>();
        var seen = new HashSet<UnitKey>();

        foreach (var entry in primary.Concat(secondary))
        {
            if (seen.Add(UnitKey.FromEntry(entry)))
            {
                merged.Add(entry);
            }
        }

        return merged;
    }

    public static IReadOnlyList<ReinforcementUnitEntry> Filter(
        IEnumerable<ReinforcementUnitEntry> units,
        string? searchText)
    {
        var normalized = (searchText ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return units.ToArray();
        }

        return units
            .Where(unit => MatchesSearch(unit, normalized))
            .ToArray();
    }

    public static IReadOnlyList<ReinforcementUnitEntry> Parse(IEnumerable<string> lines)
    {
        return lines
            .Select(ParseLine)
            .Where(result => result.Entry is not null)
            .Select(result => result.Entry!)
            .ToArray();
    }

    private static IReadOnlyList<ReinforcementUnitEntry> LoadBuiltInCore()
    {
        using var stream = OpenBuiltInResourceStream();
        using var reader = new StreamReader(stream);
        var vanilla = Parse(ReadLines(reader));
        var fromPacks = LoadFromAssetPacks();
        return Merge(vanilla, fromPacks);
    }

    private static IReadOnlyList<ReinforcementUnitEntry> LoadFromAssetPacks()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Assets", "Catalogs");
        var result = new List<ReinforcementUnitEntry>();
        foreach (var packDir in AssetPackLoader.EnumeratePackDirs(root))
        {
            AssetPackManifest manifest;
            try { manifest = AssetPackLoader.LoadManifest(packDir); }
            catch (AssetPackException) { continue; }

            foreach (var entry in manifest.Assets.Where(a => a.Kind == "Reinforcements"))
            {
                using var s = AssetPackLoader.OpenAsset(packDir, entry);
                using var sr = new StreamReader(s);
                var lines = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                result.AddRange(Parse(lines));
            }
        }
        return result;
    }

    private static ReinforcementUnitParseResult ParseLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return ReinforcementUnitParseResult.Ignorable;
        }

        var parts = SplitColumns(trimmed);
        if (parts.Length is not 4 and not 5)
        {
            return ReinforcementUnitParseResult.Invalid;
        }

        var mod = parts[0].Trim();
        var faction = parts[1].Trim();
        var codeText = parts[2].Trim();
        var name = parts[3].Trim();
        var sourceId = parts.Length == 5 ? NormalizeOptionalColumn(parts[4]) : null;
        if (mod.Length == 0 ||
            faction.Length == 0 ||
            name.Length == 0 ||
            !UnitCodeParser.TryParse(codeText, out var code))
        {
            return ReinforcementUnitParseResult.Invalid;
        }

        return new ReinforcementUnitParseResult(new ReinforcementUnitEntry(mod, faction, code, name, sourceId), false);
    }

    private static bool MatchesSearch(ReinforcementUnitEntry unit, string searchText)
    {
        return unit.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            unit.Mod.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            unit.Faction.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            (unit.SourceId?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
            unit.CodeText.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            unit.CodeText[2..].Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] SplitColumns(string line)
    {
        return line.Contains('\t')
            ? line.Split('\t', StringSplitOptions.TrimEntries)
            : line.Split(',', StringSplitOptions.TrimEntries);
    }

    private static string CustomPath(string? baseDirectory)
    {
        return Path.Combine(baseDirectory ?? AppContext.BaseDirectory, CustomFileName);
    }

    private static void AppendToCustomFile(string? baseDirectory, IReadOnlyList<ReinforcementUnitEntry> entries)
    {
        var path = CustomPath(baseDirectory);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllLines(path, entries.Select(FormatLine));
    }

    private static string FormatLine(ReinforcementUnitEntry entry)
    {
        return entry.SourceId is null
            ? string.Join(',', entry.Mod, entry.Faction, entry.CodeText, entry.Name)
            : string.Join(',', entry.Mod, entry.Faction, entry.CodeText, entry.Name, entry.SourceId);
    }

    private static Stream OpenBuiltInResourceStream()
    {
        var assembly = typeof(ReinforcementUnitCatalog).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("code.txt", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException("Built-in unit code resource code.txt was not found.");
        }

        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Unable to open embedded resource {resourceName}.");
    }

    private static IEnumerable<string> ReadLines(StreamReader reader)
    {
        while (reader.ReadLine() is string line)
        {
            yield return line;
        }
    }

    private static string? NormalizeOptionalColumn(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 || trimmed.Equals("-", StringComparison.Ordinal)
            ? null
            : trimmed;
    }
}

public sealed record ReinforcementUnitEntry(
    string Mod,
    string Faction,
    uint Code,
    string Name,
    string? SourceId = null)
{
    public string CodeText => UnitCodeParser.Format(Code);
}

public sealed record ReinforcementUnitImportResult(
    IReadOnlyList<ReinforcementUnitEntry> AddedEntries,
    int DuplicateCount,
    int InvalidCount)
{
    public int AddedCount => AddedEntries.Count;
}

internal readonly record struct ReinforcementUnitParseResult(
    ReinforcementUnitEntry? Entry,
    bool IsIgnorable)
{
    public static ReinforcementUnitParseResult Ignorable { get; } = new(null, true);

    public static ReinforcementUnitParseResult Invalid { get; } = new(null, false);
}

internal readonly record struct UnitKey(string Mod, uint Code)
{
    public static UnitKey FromEntry(ReinforcementUnitEntry entry)
    {
        return new UnitKey(entry.Mod.ToUpperInvariant(), entry.Code);
    }
}
