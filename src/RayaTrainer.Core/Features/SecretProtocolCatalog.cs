using System.IO;
using System.Reflection;
using RayaTrainer.Core.Assets;
using RayaTrainer.Core.Hashing;

namespace RayaTrainer.Core.Features;

public sealed record SecretProtocolEntry(
    string Mod,
    string Faction,
    string Name,
    string? PlayerTech,
    string? Upgrade,
    string? SpecialPower = null,
    uint? ExplicitPlayerTechId = null,
    uint? ExplicitUpgradeId = null)
{
    public uint PlayerTechId => ExplicitPlayerTechId ?? (PlayerTech is null ? 0 : Ra3InstanceIdHash.Compute(PlayerTech));

    public uint UpgradeId => ExplicitUpgradeId ?? (Upgrade is null ? 0 : Ra3InstanceIdHash.Compute(Upgrade));

    public bool CanGrant => PlayerTechId != 0 || UpgradeId != 0;

    public string PlayerTechIdText => PlayerTechId == 0 ? "-" : $"0x{PlayerTechId:X8}";

    public string UpgradeText
    {
        get
        {
            if (Upgrade is not null)
            {
                return $"{Upgrade} (0x{UpgradeId:X8})";
            }

            if (UpgradeId != 0)
            {
                return $"0x{UpgradeId:X8}";
            }

            return SpecialPower is null ? "无被动 Upgrade" : $"仅 SpecialPower：{SpecialPower}";
        }
    }

    public SecretProtocolGrantSettings ToGrantSettings() => new(PlayerTechId, UpgradeId);
}

public static class SecretProtocolCatalog
{
    public const string CustomFileName = "RayaTrainer.secret-protocols.txt";
    private static readonly string[] BuiltInResourceFileNames =
    [
        "secret-protocols.txt"
    ];

    private static readonly Lazy<IReadOnlyList<SecretProtocolEntry>> BuiltInProtocols =
        new(LoadBuiltInCore);

    public static IReadOnlyList<SecretProtocolEntry> Load(string path)
    {
        return Parse(File.ReadLines(path));
    }

    public static IReadOnlyList<SecretProtocolEntry> LoadWithCustomFile(string? baseDirectory = null)
    {
        var customPath = CustomPath(baseDirectory);
        var customProtocols = File.Exists(customPath)
            ? Load(customPath)
            : Array.Empty<SecretProtocolEntry>();

        return Merge(LoadBuiltIn(), customProtocols);
    }

    public static SecretProtocolImportResult ImportToCustomFile(
        string? baseDirectory,
        IEnumerable<string> lines,
        IEnumerable<SecretProtocolEntry> existingProtocols)
    {
        var existingKeys = existingProtocols
            .Select(SecretProtocolKey.FromEntry)
            .ToHashSet();
        var added = new List<SecretProtocolEntry>();
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

            if (!existingKeys.Add(SecretProtocolKey.FromEntry(parseResult.Entry)))
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

        return new SecretProtocolImportResult(added, duplicateCount, invalidCount);
    }

    public static IReadOnlyList<SecretProtocolEntry> LoadBuiltIn()
    {
        return BuiltInProtocols.Value;
    }

    public static IReadOnlyList<SecretProtocolEntry> Merge(
        IEnumerable<SecretProtocolEntry> primary,
        IEnumerable<SecretProtocolEntry> secondary)
    {
        var merged = new List<SecretProtocolEntry>();
        var seen = new HashSet<SecretProtocolKey>();

        foreach (var entry in primary.Concat(secondary))
        {
            if (seen.Add(SecretProtocolKey.FromEntry(entry)))
            {
                merged.Add(entry);
            }
        }

        return merged;
    }

    public static IReadOnlyList<SecretProtocolEntry> Parse(IEnumerable<string> lines)
    {
        return lines
            .Select(ParseLine)
            .Where(result => result.Entry is not null)
            .Select(result => result.Entry!)
            .ToArray();
    }

    private static IReadOnlyList<SecretProtocolEntry> LoadBuiltInCore()
    {
        var vanilla = LoadVanilla();
        var embeddedExtras = BuiltInResourceFileNames
            .SelectMany(ReadBuiltInResource)
            .ToArray();
        var fromPacks = LoadFromAssetPacks();
        return Merge(Merge(vanilla, embeddedExtras), fromPacks);
    }

    private static IReadOnlyList<SecretProtocolEntry> LoadFromAssetPacks()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Assets", "Catalogs");
        var result = new List<SecretProtocolEntry>();
        foreach (var packDir in AssetPackLoader.EnumeratePackDirs(root))
        {
            AssetPackManifest manifest;
            try { manifest = AssetPackLoader.LoadManifest(packDir); }
            catch (AssetPackException) { continue; }

            foreach (var entry in manifest.Assets.Where(a => a.Kind == "SecretProtocols"))
            {
                using var s = AssetPackLoader.OpenAsset(packDir, entry);
                using var sr = new StreamReader(s);
                var lines = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                result.AddRange(Parse(lines));
            }
        }
        return result;
    }

    private static IReadOnlyList<SecretProtocolEntry> ReadBuiltInResource(string fileName)
    {
        using var stream = OpenBuiltInResourceStream(fileName);
        using var reader = new StreamReader(stream);
        return Parse(ReadLines(reader));
    }

    private static IReadOnlyList<SecretProtocolEntry> LoadVanilla() =>
    [
        new("原版 RA3", "盟军", "自由贸易", "PlayerTech_ProductionBonus_Allies", null),
        new("原版 RA3", "盟军", "高科技", "PlayerTech_Allied_HighTechnology", "Upgrade_AlliedHighTechnology"),
        new("原版 RA3", "盟军", "先进航空学", "PlayerTech_Allied_AirPower", "Upgrade_AlliedAirPower"),
        new("原版 RA3", "盟军", "侦察扫描", "PlayerTech_Allied_SatelliteSweep", null),
        new("原版 RA3", "盟军", "精确轰炸", "PlayerTech_Allied_PrecisionStrike", null),
        new("原版 RA3", "盟军", "冰冻卫星 1", "PlayerTech_Allied_CryoSatellite_Rank1", null),
        new("原版 RA3", "盟军", "冰冻卫星 2", "PlayerTech_Allied_CryoSatellite_Rank2", null),
        new("原版 RA3", "盟军", "冰冻卫星 3", "PlayerTech_Allied_CryoSatellite_Rank3", null),
        new("原版 RA3", "盟军", "超时空交换", "PlayerTech_Allied_ChronoSwap", null),
        new("原版 RA3", "盟军", "定时炸弹 1", "PlayerTech_Allied_TimeBomb_Rank1", null),
        new("原版 RA3", "盟军", "定时炸弹 2", "PlayerTech_Allied_TimeBomb_Rank2", null),
        new("原版 RA3", "盟军", "定时炸弹 3", "PlayerTech_Allied_TimeBomb_Rank3", null),
        new("原版 RA3", "盟军", "超时空裂缝 1", "PlayerTech_Allied_ChronoRift_Rank1", null),
        new("原版 RA3", "盟军", "超时空裂缝 2", "PlayerTech_Allied_ChronoRift_Rank2", null),
        new("原版 RA3", "盟军", "超时空裂缝 3", "PlayerTech_Allied_ChronoRift_Rank3", null),
        new("原版 RA3", "盟军", "基地科技 2", null, "Upgrade_AlliedTech2"),
        new("原版 RA3", "盟军", "基地科技 3", null, "Upgrade_AlliedTech3"),

        new("原版 RA3", "苏联", "大生产", "PlayerTech_ProductionBonus_Soviet", null),
        new("原版 RA3", "苏联", "恐怖机器人巢", "PlayerTech_Soviet_TerrorDroneEggs", "Upgrade_SovietTerrorDroneEggs"),
        new("原版 RA3", "苏联", "碾压履带", "PlayerTech_Soviet_CrushPuppies", "Upgrade_SovietCrushPuppiesPower"),
        new("原版 RA3", "苏联", "资金悬赏", "PlayerTech_Soviet_ProductionKickbacks", null),
        new("原版 RA3", "苏联", "毒素腐蚀", "PlayerTech_Soviet_IrradiateTarget", null),
        new("原版 RA3", "苏联", "毒爆炸弹 1", "PlayerTech_Soviet_DesolatorBomb_Rank1", null),
        new("原版 RA3", "苏联", "毒爆炸弹 2", "PlayerTech_Soviet_DesolatorBomb_Rank2", null),
        new("原版 RA3", "苏联", "毒爆炸弹 3", "PlayerTech_Soviet_DesolatorBomb_Rank3", null),
        new("原版 RA3", "苏联", "轨道垃圾 1", "PlayerTech_Soviet_OrbitalRefuse_Rank1", null),
        new("原版 RA3", "苏联", "轨道垃圾 2", "PlayerTech_Soviet_OrbitalRefuse_Rank2", null),
        new("原版 RA3", "苏联", "轨道垃圾 3", "PlayerTech_Soviet_OrbitalRefuse_Rank3", null),
        new("原版 RA3", "苏联", "磁力卫星 1", "PlayerTech_Soviet_MagneticSatellite_Rank_1", null),
        new("原版 RA3", "苏联", "磁力卫星 2", "PlayerTech_Soviet_MagneticSatellite_Rank_2", null),
        new("原版 RA3", "苏联", "磁力卫星 3", "PlayerTech_Soviet_MagneticSatellite_Rank_3", null),
        new("原版 RA3", "苏联", "磁力奇点", "PlayerTech_Soviet_MagneticSingularity", null),

        new("原版 RA3", "升阳", "机械化组装", "PlayerTech_ProductionBonus_Japan", null),
        new("原版 RA3", "升阳", "海军优势", "PlayerTech_Japan_NavalPower", "Upgrade_JapanNavalPower"),
        new("原版 RA3", "升阳", "先进导弹包", "PlayerTech_Japan_AdvancedMissilePacks", "Upgrade_JapanAdvancedMissilePacks"),
        new("原版 RA3", "升阳", "光荣自爆", "PlayerTech_Japan_EnhancedKamikaze", "Upgrade_JapanEnhancedKamikaze"),
        new("原版 RA3", "升阳", "防御无人机", "PlayerTech_Japan_PointDefenseDrones", null),
        new("原版 RA3", "升阳", "伏击", "PlayerTech_Japan_Ambush", null),
        new("原版 RA3", "升阳", "天皇怒火 1", "PlayerTech_Japan_EmperorsRage_Rank1", null),
        new("原版 RA3", "升阳", "天皇怒火 2", "PlayerTech_Japan_EmperorsRage_Rank2", null),
        new("原版 RA3", "升阳", "天皇怒火 3", "PlayerTech_Japan_EmperorsRage_Rank3", null),
        new("原版 RA3", "升阳", "最终中队 1", "PlayerTech_Japan_FinalSquadron_Rank1", null),
        new("原版 RA3", "升阳", "最终中队 2", "PlayerTech_Japan_FinalSquadron_Rank2", null),
        new("原版 RA3", "升阳", "最终中队 3", "PlayerTech_Japan_FinalSquadron_Rank3", null),
        new("原版 RA3", "升阳", "气球炸弹 1", "PlayerTech_Japan_BalloonAttack_Rank1", null),
        new("原版 RA3", "升阳", "气球炸弹 2", "PlayerTech_Japan_BalloonAttack_Rank2", null),
        new("原版 RA3", "升阳", "气球炸弹 3", "PlayerTech_Japan_BalloonAttack_Rank3", null),
        new("原版 RA3", "升阳", "兵营科技 2", null, "Upgrade_JapanBarracksTech2"),
        new("原版 RA3", "升阳", "兵营科技 3", null, "Upgrade_JapanBarracksTech3"),
        new("原版 RA3", "升阳", "战车工厂科技 2", null, "Upgrade_JapanWarFactoryTech2"),
        new("原版 RA3", "升阳", "战车工厂科技 3", null, "Upgrade_JapanWarFactoryTech3"),
        new("原版 RA3", "升阳", "船坞科技 2", null, "Upgrade_JapanNavalYardTech2"),
        new("原版 RA3", "升阳", "船坞科技 3", null, "Upgrade_JapanNavalYardTech3")
    ];

    private static SecretProtocolParseResult ParseLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return SecretProtocolParseResult.Ignorable;
        }

        var parts = SplitColumns(trimmed);
        if (parts.Length is not (4 or 5 or 6))
        {
            return SecretProtocolParseResult.Invalid;
        }

        var mod = parts[0].Trim();
        var faction = parts[1].Trim();
        var name = parts[2].Trim();
        if (mod.Length == 0 || faction.Length == 0 || name.Length == 0)
        {
            return SecretProtocolParseResult.Invalid;
        }

        var playerTech = NormalizeToken(parts[3]);
        var upgrade = parts.Length >= 5 ? NormalizeToken(parts[4]) : null;
        var specialPower = parts.Length >= 6 ? NormalizeToken(parts[5]) : null;

        if (parts.Length == 4)
        {
            if (playerTech is not null && IsUpgrade(playerTech))
            {
                upgrade = playerTech;
                playerTech = null;
            }
            else if (playerTech is not null && IsSpecialPower(playerTech))
            {
                specialPower = playerTech;
                playerTech = null;
            }
        }

        if ((playerTech is not null && !IsPlayerTech(playerTech)) ||
            (upgrade is not null && !IsUpgrade(upgrade)) ||
            (specialPower is not null && !IsSpecialPower(specialPower)) ||
            (playerTech is null && upgrade is null && specialPower is null))
        {
            return SecretProtocolParseResult.Invalid;
        }

        return new SecretProtocolParseResult(
            new SecretProtocolEntry(mod, faction, name, playerTech, upgrade, specialPower),
            false);
    }

    private static string? NormalizeToken(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 || trimmed == "-" ? null : trimmed;
    }

    private static bool IsPlayerTech(string value) =>
        value.StartsWith("PlayerTech_", StringComparison.Ordinal);

    private static bool IsUpgrade(string value) =>
        value.StartsWith("Upgrade_", StringComparison.Ordinal);

    private static bool IsSpecialPower(string value) =>
        value.StartsWith("SpecialPower", StringComparison.Ordinal);

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

    private static void AppendToCustomFile(string? baseDirectory, IReadOnlyList<SecretProtocolEntry> entries)
    {
        var path = CustomPath(baseDirectory);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllLines(path, entries.Select(FormatLine));
    }

    private static string FormatLine(SecretProtocolEntry entry)
    {
        return string.Join(',', entry.Mod, entry.Faction, entry.Name, entry.PlayerTech ?? string.Empty, entry.Upgrade ?? string.Empty, entry.SpecialPower ?? string.Empty);
    }

    private static Stream OpenBuiltInResourceStream(string fileName)
    {
        var assembly = typeof(SecretProtocolCatalog).Assembly;
        var resourceName = $"RayaTrainer.Core.Assets.{fileName.Replace('/', '.').Replace('\\', '.')}";

        if (!assembly.GetManifestResourceNames().Contains(resourceName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Built-in secret protocol resource {fileName} was not found.");
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
}

public sealed record SecretProtocolImportResult(
    IReadOnlyList<SecretProtocolEntry> AddedEntries,
    int DuplicateCount,
    int InvalidCount)
{
    public int AddedCount => AddedEntries.Count;
}

internal readonly record struct SecretProtocolParseResult(
    SecretProtocolEntry? Entry,
    bool IsIgnorable)
{
    public static SecretProtocolParseResult Ignorable { get; } = new(null, true);

    public static SecretProtocolParseResult Invalid { get; } = new(null, false);
}

internal readonly record struct SecretProtocolKey(string Mod, string Identity)
{
    public static SecretProtocolKey FromEntry(SecretProtocolEntry entry)
    {
        return new SecretProtocolKey(
            entry.Mod.ToUpperInvariant(),
            (entry.PlayerTech ?? entry.Upgrade ?? entry.SpecialPower ?? entry.Name).ToUpperInvariant());
    }
}
