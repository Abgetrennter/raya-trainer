namespace RayaTrainer.Core.Features;

/// <summary>
/// Snapshot of OBJECT-type upgrades available on the first selected unit.
/// Hashes come from the unit's module TriggeredBy lists (dynamic read, so
/// mod-added upgrades are reflected). Up to 20 entries; Count=0 means the
/// unit has no upgrade-triggered modules or no unit is selected.
/// </summary>
public readonly record struct SelectedUnitUpgradesSnapshot(
    uint UnitTypeId,
    uint ThingTemplateAddress,
    uint Count,
    IReadOnlyList<uint> Hashes)
{
    public static SelectedUnitUpgradesSnapshot Empty { get; } = new(0, 0, 0, Array.Empty<uint>());
}
