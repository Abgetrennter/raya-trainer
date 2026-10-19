namespace RayaTrainer.Core.Features;

/// <summary>
/// 一条武器槽绑定（WeaponSlot_WeaponData 条目）的只读快照。
/// </summary>
public readonly record struct TemplateWeaponSlotEntry(
    uint Ordering,
    int ListIndex,
    bool UpgradeGated,
    uint WeaponTemplateAddress);

/// <summary>
/// 单位模板武器槽快照（cmd 74）。Count=0 表示该模板没有武器集模块或所选列表为空。
/// </summary>
public sealed record TemplateWeaponSetSnapshot(
    uint TargetUnitId,
    bool Truncated,
    IReadOnlyList<TemplateWeaponSlotEntry> Entries)
{
    public static TemplateWeaponSetSnapshot Empty(uint targetUnitId) =>
        new(targetUnitId, false, Array.Empty<TemplateWeaponSlotEntry>());
}
