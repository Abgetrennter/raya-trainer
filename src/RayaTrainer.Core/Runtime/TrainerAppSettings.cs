using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RayaTrainer.Core.Features;

namespace RayaTrainer.Core.Runtime;

public sealed record TrainerAppSettings
{
    public TrainerAppSettings(
        string LauncherPath,
        string LauncherArguments,
        int AttachTimeoutSeconds)
        : this(
            LauncherPath,
            LauncherArguments,
            AttachTimeoutSeconds,
            ResourceValueSettings.Default,
            Array.Empty<ReinforcementPreset>(),
            new Dictionary<string, string>(),
            string.Empty,
            string.Empty,
            null)
    {
    }

    public TrainerAppSettings(
        string LauncherPath,
        string LauncherArguments,
        int AttachTimeoutSeconds,
        ResourceValueSettings ResourceValues,
        IReadOnlyList<ReinforcementPreset> ReinforcementPresets)
        : this(
            LauncherPath,
            LauncherArguments,
            AttachTimeoutSeconds,
            ResourceValues,
            ReinforcementPresets,
            new Dictionary<string, string>(),
            string.Empty,
            string.Empty,
            null)
    {
    }

    public TrainerAppSettings(
        string LauncherPath,
        string LauncherArguments,
        int AttachTimeoutSeconds,
        ResourceValueSettings ResourceValues,
        IReadOnlyList<ReinforcementPreset> ReinforcementPresets,
        IReadOnlyDictionary<string, string> Hotkeys)
        : this(
            LauncherPath,
            LauncherArguments,
            AttachTimeoutSeconds,
            ResourceValues,
            ReinforcementPresets,
            Hotkeys,
            string.Empty,
            string.Empty,
            null)
    {
    }

    public TrainerAppSettings(
        string LauncherPath,
        string LauncherArguments,
        int AttachTimeoutSeconds,
        ResourceValueSettings ResourceValues,
        IReadOnlyList<ReinforcementPreset> ReinforcementPresets,
        IReadOnlyDictionary<string, string> Hotkeys,
        string ModsRootPath = "",
        string SelectedModSkudefPath = "",
        IReadOnlyList<SecretProtocolQueuePreset>? SecretProtocolPresets = null,
        bool HidePrimaryActionCard = false)
    {
        this.LauncherPath = LauncherPath;
        this.LauncherArguments = LauncherArguments;
        this.AttachTimeoutSeconds = AttachTimeoutSeconds;
        this.ResourceValues = ResourceValues;
        this.ReinforcementPresets = ReinforcementPresets.ToArray();
        this.Hotkeys = new Dictionary<string, string>(Hotkeys, StringComparer.Ordinal);
        this.ModsRootPath = ModsRootPath;
        this.SelectedModSkudefPath = SelectedModSkudefPath;
        this.SecretProtocolPresets = (SecretProtocolPresets ?? Array.Empty<SecretProtocolQueuePreset>()).ToArray();
        this.HidePrimaryActionCard = HidePrimaryActionCard;
    }

    public static TrainerAppSettings Default { get; } =
        new(string.Empty, "-ui", 30);

    public int SchemaVersion => TrainerAppSettingsStore.CurrentSchemaVersion;

    public string LauncherPath { get; }

    public string LauncherArguments { get; }

    public string ModsRootPath { get; }

    public string SelectedModSkudefPath { get; }

    public int AttachTimeoutSeconds { get; }

    public ResourceValueSettings ResourceValues { get; }

    public IReadOnlyList<ReinforcementPreset> ReinforcementPresets { get; }

    public IReadOnlyDictionary<string, string> Hotkeys { get; }

    public IReadOnlyList<SecretProtocolQueuePreset> SecretProtocolPresets { get; }

    public bool HidePrimaryActionCard { get; }
}

public sealed class TrainerAppSettingsStore
{
    public const string SettingsFileName = "RayaTrainer.settings.json";
    public const string LegacySettingsFileName = "Ra3Trainer.settings.json";
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public TrainerAppSettingsStore(string? path = null)
    {
        _path = path ?? DefaultPath();
    }

    public TrainerAppSettings Load()
    {
        return Load(new Dictionary<string, string>());
    }

    public TrainerAppSettings Load(IReadOnlyDictionary<string, string> defaultHotkeys)
    {
        var defaults = CreateDefault(defaultHotkeys);
        if (!File.Exists(_path))
        {
            var legacy = LegacyPath;
            if (File.Exists(legacy))
            {
                // One-time upgrade: parse legacy, save as new, keep legacy file.
                // refactor.md §19 — legacy file intentionally NOT deleted.
                if (TryLoadInternal(legacy, defaults, out var migrated))
                {
                    TrySave(migrated);
                    return migrated;
                }
                // Legacy unreadable → fall through to defaults
            }
            TrySave(defaults);
            return defaults;
        }

        if (!TryLoadInternal(_path, defaults, out var settings))
        {
            return defaults;
        }
        return settings;
    }

    public void Save(TrainerAppSettings settings)
    {
        SaveToPath(_path, settings);
    }

    public static string DefaultPath()
    {
        return DefaultPath(AppContext.BaseDirectory);
    }

    public static string DefaultPath(string baseDirectory)
    {
        return Path.Combine(baseDirectory, SettingsFileName);
    }

    private string LegacyPath
    {
        get
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory)) return LegacySettingsFileName;
            return Path.Combine(directory, LegacySettingsFileName);
        }
    }

    private void TrySave(TrainerAppSettings settings)
    {
        try
        {
            Save(settings);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void SaveToPath(string path, TrainerAppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, settings, JsonOptions);
    }

    private bool TryLoadInternal(string path, TrainerAppSettings defaults, out TrainerAppSettings result)
    {
        try
        {
            JsonElement root;
            using (var stream = File.OpenRead(path))
            using (var document = JsonDocument.Parse(stream))
            {
                root = document.RootElement.Clone();
            }

            if (ReadInt32(root, nameof(TrainerAppSettings.SchemaVersion)) != CurrentSchemaVersion)
            {
                result = ResetUnsupportedSettings(defaults);
                return true;
            }

            result = Normalize(root, defaults);
            return true;
        }
        catch (JsonException)
        {
            result = defaults;
            return false;
        }
        catch (IOException)
        {
            result = defaults;
            return false;
        }
    }

    private static TrainerAppSettings CreateDefault(IReadOnlyDictionary<string, string> hotkeys)
    {
        return new TrainerAppSettings(
            string.Empty,
            "-ui",
            30,
            ResourceValueSettings.Default,
            Array.Empty<ReinforcementPreset>(),
            hotkeys,
            string.Empty,
            string.Empty,
            null);
    }

    private static TrainerAppSettings Normalize(JsonElement root, TrainerAppSettings defaults)
    {
        var launcherPath = ReadString(root, nameof(TrainerAppSettings.LauncherPath)) ?? defaults.LauncherPath;
        var launcherArguments = ReadString(root, nameof(TrainerAppSettings.LauncherArguments))
            ?? defaults.LauncherArguments;
        var modsRootPath = ReadString(root, nameof(TrainerAppSettings.ModsRootPath))
            ?? defaults.ModsRootPath;
        var selectedModSkudefPath = ReadString(root, nameof(TrainerAppSettings.SelectedModSkudefPath))
            ?? defaults.SelectedModSkudefPath;
        var attachTimeoutSeconds = ReadInt32(root, nameof(TrainerAppSettings.AttachTimeoutSeconds))
            ?? defaults.AttachTimeoutSeconds;
        var resourceValues = ReadResourceValues(root) ?? defaults.ResourceValues;
        var reinforcementPresets = ReadReinforcementPresets(root);
        var secretProtocolPresets = ReadSecretProtocolPresets(root);
        var hotkeys = ReadHotkeys(root, defaults.Hotkeys);
        var hidePrimaryActionCard = ReadBool(root, nameof(TrainerAppSettings.HidePrimaryActionCard))
            ?? defaults.HidePrimaryActionCard;

        if (string.IsNullOrWhiteSpace(launcherArguments))
        {
            launcherArguments = defaults.LauncherArguments;
        }

        if (attachTimeoutSeconds <= 0)
        {
            attachTimeoutSeconds = defaults.AttachTimeoutSeconds;
        }

        return new TrainerAppSettings(
            launcherPath,
            launcherArguments,
            attachTimeoutSeconds,
            resourceValues,
            reinforcementPresets,
            hotkeys,
            modsRootPath,
            selectedModSkudefPath,
            secretProtocolPresets,
            hidePrimaryActionCard);
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static bool? ReadBool(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;
    }

    private TrainerAppSettings ResetUnsupportedSettings(TrainerAppSettings defaults)
    {
        try
        {
            var backupPath = Path.Combine(
                Path.GetDirectoryName(_path) ?? string.Empty,
                "RayaTrainer.settings.legacy.json");
            File.Move(_path, backupPath, overwrite: true);
            Save(defaults);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return defaults;
    }

    private static ResourceValueSettings? ReadResourceValues(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(TrainerAppSettings.ResourceValues), out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var defaults = ResourceValueSettings.Default;
        var moneyAmount = ReadInt32(value, nameof(ResourceValueSettings.MoneyAmount)) ?? defaults.MoneyAmount;
        var powerValue = ReadInt32(value, nameof(ResourceValueSettings.PowerValue)) ?? defaults.PowerValue;
        var scPointValue = ReadInt32(value, nameof(ResourceValueSettings.ScPointValue)) ?? defaults.ScPointValue;
        try
        {
            return new ResourceValueSettings(moneyAmount, powerValue, scPointValue);
        }
        catch (ArgumentOutOfRangeException)
        {
            return defaults;
        }
    }

    private static IReadOnlyList<ReinforcementPreset> ReadReinforcementPresets(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(TrainerAppSettings.ReinforcementPresets), out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ReinforcementPreset>();
        }

        var presets = new List<ReinforcementPreset>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(item, nameof(ReinforcementPreset.Name)) ?? string.Empty;
            if (item.TryGetProperty(nameof(ReinforcementPreset.Entries), out var entriesElement) &&
                entriesElement.ValueKind == JsonValueKind.Array)
            {
                var entries = new List<ReinforcementPresetEntry>();
                foreach (var entryItem in entriesElement.EnumerateArray())
                {
                    if (entryItem.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var entryName = ReadString(entryItem, nameof(ReinforcementPresetEntry.Name)) ?? string.Empty;
                    var entryUnitId = ReadUInt32(entryItem, nameof(ReinforcementPresetEntry.UnitId));
                    var entryCount = ReadInt32(entryItem, nameof(ReinforcementPresetEntry.Count));
                    var entryRank = ReadInt32(entryItem, nameof(ReinforcementPresetEntry.Rank));
                    if (entryUnitId is null || entryCount is null || entryRank is null)
                    {
                        continue;
                    }

                    try
                    {
                        entries.Add(new ReinforcementPresetEntry(
                            entryName,
                            entryUnitId.Value,
                            entryCount.Value,
                            entryRank.Value));
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                }

                if (entries.Count > 0)
                {
                    presets.Add(new ReinforcementPreset(name, entries));
                }
                continue;
            }

        }

        return presets;
    }

    private static IReadOnlyList<SecretProtocolQueuePreset> ReadSecretProtocolPresets(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(TrainerAppSettings.SecretProtocolPresets), out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SecretProtocolQueuePreset>();
        }

        var presets = new List<SecretProtocolQueuePreset>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                var name = ReadString(item, nameof(SecretProtocolQueuePreset.Name)) ?? string.Empty;
                if (!item.TryGetProperty(nameof(SecretProtocolQueuePreset.Entries), out var entriesElement) ||
                    entriesElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var entries = new List<SecretProtocolPresetEntry>();
                foreach (var entryItem in entriesElement.EnumerateArray())
                {
                    if (entryItem.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var mod = ReadString(entryItem, nameof(SecretProtocolPresetEntry.Mod)) ?? string.Empty;
                    var faction = ReadString(entryItem, nameof(SecretProtocolPresetEntry.Faction)) ?? string.Empty;
                    var entryName = ReadString(entryItem, nameof(SecretProtocolPresetEntry.Name)) ?? string.Empty;
                    var playerTechId = ReadUInt32(entryItem, nameof(SecretProtocolPresetEntry.PlayerTechId));
                    var upgradeId = ReadUInt32(entryItem, nameof(SecretProtocolPresetEntry.UpgradeId));
                    if (playerTechId is null || upgradeId is null)
                    {
                        continue;
                    }

                    entries.Add(new SecretProtocolPresetEntry(mod, faction, entryName, playerTechId.Value, upgradeId.Value));
                }

                presets.Add(new SecretProtocolQueuePreset(name, entries));
            }
            catch
            {
                // Skip malformed preset entries
            }
        }

        return presets;
    }

    private static IReadOnlyDictionary<string, string> ReadHotkeys(JsonElement root, IReadOnlyDictionary<string, string> defaultHotkeys)
    {
        var hotkeys = new Dictionary<string, string>(defaultHotkeys, StringComparer.Ordinal);
        if (!root.TryGetProperty(nameof(TrainerAppSettings.Hotkeys), out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return hotkeys;
        }

        foreach (var item in value.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
            {
                hotkeys[item.Name] = item.Value.GetString() ?? string.Empty;
            }
        }

        return hotkeys;
    }

    private static uint? ReadUInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var result)
            ? result
            : null;
    }

    private static float? ReadFloat(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var result)
            ? result
            : null;
    }

}
