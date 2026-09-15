namespace RayaTrainer.Core.Features;

/// <summary>
/// 武器直挂（cmd 34）设置：按武器名哈希解析 WeaponTemplate，改写目标模板
/// Hardpoint 槽定义中匹配槽位序（Ordering）的默认（无升级条件）WeaponSlot_WeaponData 条目。
/// </summary>
public sealed record WeaponAttachSettings
{
    // WeaponSlotType：0 PRIMARY..4 QUINARY；5 NO_WEAPON 不是合法目标。
    public const uint MaxOrdering = 4u;

    public static WeaponAttachSettings Parse(
        string targetUnitIdText, string weaponHashText, uint ordering)
    {
        return new WeaponAttachSettings(
            UnitCodeParser.Parse(targetUnitIdText),
            UnitCodeParser.Parse(weaponHashText),
            ordering);
    }

    public WeaponAttachSettings(uint targetUnitId, uint weaponHash, uint ordering)
    {
        if (targetUnitId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetUnitId), "Target unit id must be non-zero.");
        }

        if (weaponHash == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weaponHash), "Weapon hash must be non-zero.");
        }

        if (ordering > MaxOrdering)
        {
            throw new ArgumentOutOfRangeException(nameof(ordering), "Ordering must be within PRIMARY..QUINARY (0..4).");
        }

        TargetUnitId = targetUnitId;
        WeaponHash = weaponHash;
        Ordering = ordering;
    }

    public uint TargetUnitId { get; }

    public uint WeaponHash { get; }

    public uint Ordering { get; }
}
