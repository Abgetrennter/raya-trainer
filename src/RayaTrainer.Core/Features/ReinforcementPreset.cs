namespace RayaTrainer.Core.Features;

public sealed record ReinforcementPresetEntry
{
    public ReinforcementPresetEntry(string name, uint unitId, int count, int rank)
    {
        var settings = new ReinforcementSettings(unitId, count, rank);
        Name = string.IsNullOrWhiteSpace(name) ? $"0x{unitId:X8}" : name.Trim();
        UnitId = settings.UnitId;
        Count = settings.Count;
        Rank = settings.Rank;
    }

    public string Name { get; }

    public uint UnitId { get; }

    public int Count { get; }

    public int Rank { get; }

    public ReinforcementQueueEntry ToQueueEntry() =>
        new(Name, $"0x{UnitId:X8}", Count.ToString(), Rank.ToString());

    public static ReinforcementPresetEntry FromQueueEntry(ReinforcementQueueEntry entry)
    {
        var settings = ReinforcementSettings.Parse(entry.UnitIdText, entry.CountText, entry.RankText);
        return new ReinforcementPresetEntry(entry.Name, settings.UnitId, settings.Count, settings.Rank);
    }
}

public sealed record ReinforcementPreset
{
    public ReinforcementPreset(string name, IReadOnlyList<ReinforcementPresetEntry> entries)
    {
        Entries = entries.ToArray();
        Name = string.IsNullOrWhiteSpace(name)
            ? Entries.FirstOrDefault()?.Name ?? string.Empty
            : name.Trim();
    }

    // 兼容旧版单项预设的调用方和设置迁移。
    public ReinforcementPreset(string name, uint unitId, int count, int rank)
        : this(name, [new ReinforcementPresetEntry(name, unitId, count, rank)])
    {
    }

    public string Name { get; }

    public IReadOnlyList<ReinforcementPresetEntry> Entries { get; }
}
