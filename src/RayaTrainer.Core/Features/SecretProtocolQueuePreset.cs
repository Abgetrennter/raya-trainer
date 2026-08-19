namespace RayaTrainer.Core.Features;

/// <summary>
/// Minimal secret protocol entry for queue presets.
/// Only stores fields needed for granting, not full catalog metadata.
/// </summary>
public sealed record SecretProtocolPresetEntry(
    string Mod,
    string Faction,
    string Name,
    uint PlayerTechId,
    uint UpgradeId)
{
    public SecretProtocolEntry ToProtocol() => new(Mod, Faction, Name, null, null, null, PlayerTechId, UpgradeId);

    public static SecretProtocolPresetEntry FromProtocol(SecretProtocolEntry protocol) =>
        new(protocol.Mod, protocol.Faction, protocol.Name, protocol.PlayerTechId, protocol.UpgradeId);
}

public sealed record SecretProtocolQueuePreset
{
    public SecretProtocolQueuePreset(string name, IReadOnlyList<SecretProtocolPresetEntry> entries)
    {
        Name = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        Entries = entries.ToArray();
    }

    public string Name { get; }
    public IReadOnlyList<SecretProtocolPresetEntry> Entries { get; }
}
