using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RayaTrainer.Core.Features;

public sealed record UpgradeNameEntry(string Name, string Description);

/// <summary>
/// Resolves upgrade instance-id hashes to human-readable names using an embedded
/// table generated from the original RA3 upgrade.xml. Mod-added upgrades fall back
/// to a hex display and do not block usage.
/// </summary>
public sealed class UpgradeNameResolver
{
    private readonly Dictionary<uint, UpgradeNameEntry> _entries;

    public UpgradeNameResolver()
    {
        _entries = LoadEmbedded();
    }

    /// <summary>Internal test hook: load from a custom JSON string.</summary>
    internal UpgradeNameResolver(string json)
    {
        _entries = Parse(json);
    }

    public UpgradeNameEntry? TryResolveName(uint upgradeHash)
    {
        return _entries.TryGetValue(upgradeHash, out var entry) ? entry : null;
    }

    public string ResolveDisplayNameOrFallback(uint upgradeHash)
    {
        return TryResolveName(upgradeHash)?.Name ?? $"升级 #0x{upgradeHash:X8}";
    }

    private static Dictionary<uint, UpgradeNameEntry> LoadEmbedded()
    {
        const string resourceName = "RayaTrainer.Core.Assets.UpgradeNames.json";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new Dictionary<uint, UpgradeNameEntry>();
        }
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static Dictionary<uint, UpgradeNameEntry> Parse(string json)
    {
        var result = new Dictionary<uint, UpgradeNameEntry>();
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!uint.TryParse(prop.Name, out var hash)) continue;
            var name = prop.Value.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() ?? $"#{hash:X8}"
                : $"#{hash:X8}";
            var desc = prop.Value.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() ?? ""
                : "";
            result[hash] = new UpgradeNameEntry(name, desc);
        }
        return result;
    }
}
