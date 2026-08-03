using RayaTrainer.Core.Features;

namespace RayaTrainer.App.Web;

internal static class TrainerPresetMapper
{
    internal static IReadOnlyList<T> MergePresets<T>(
        IEnumerable<T> primary,
        IEnumerable<T> secondary,
        Func<T, string> keySelector)
    {
        var merged = new List<T>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in primary.Concat(secondary))
        {
            if (seen.Add(keySelector(preset)))
            {
                merged.Add(preset);
            }
        }

        return merged;
    }

    internal static TrainerReinforcementPresetInfo ToPresetInfo(ReinforcementPreset preset)
    {
        return new TrainerReinforcementPresetInfo(
            preset.Name,
            preset.Entries.Select(ToPresetEntryInfo).ToArray());
    }

    private static TrainerReinforcementPresetEntryInfo ToPresetEntryInfo(ReinforcementPresetEntry entry)
    {
        return new TrainerReinforcementPresetEntryInfo(
            entry.Name,
            entry.UnitId,
            $"0x{entry.UnitId:X8}",
            entry.Count,
            entry.Rank);
    }

    internal static TrainerSecretProtocolPresetInfo ToPresetInfo(SecretProtocolQueuePreset preset)
    {
        return new TrainerSecretProtocolPresetInfo(
            preset.Name,
            preset.Entries.Select(ToPresetEntryInfo).ToArray());
    }

    private static TrainerSecretProtocolPresetEntryInfo ToPresetEntryInfo(SecretProtocolPresetEntry entry)
    {
        return new TrainerSecretProtocolPresetEntryInfo(
            entry.Mod,
            entry.Faction,
            entry.Name,
            entry.PlayerTechId,
            $"0x{entry.PlayerTechId:X8}",
            entry.UpgradeId,
            $"0x{entry.UpgradeId:X8}");
    }
}
