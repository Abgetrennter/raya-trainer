namespace RayaTrainer.Core.Features;

/// <summary>
/// 槽位级武器集替换（cmd 33）设置：把供体模板 WeaponSetUpdateModuleData 中
/// 由 <see cref="ListSelector"/> 选中的槽定义列表描述符整体接管到目标模板。
/// 位含义：bit0 Hardpoint、bit1 Turret、bit2 HierarchicalTurret、bit3 Contained。
/// </summary>
public sealed record TemplateWeaponSetReplacementSettings
{
    public const uint ListHardpoint = 0x1u;
    public const uint ListTurret = 0x2u;
    public const uint ListHierarchicalTurret = 0x4u;
    public const uint ListContained = 0x8u;
    public const uint ListAll = ListHardpoint | ListTurret | ListHierarchicalTurret | ListContained;

    public static TemplateWeaponSetReplacementSettings Parse(
        string targetUnitIdText, string donorUnitIdText, uint listSelector)
    {
        return new TemplateWeaponSetReplacementSettings(
            UnitCodeParser.Parse(targetUnitIdText),
            UnitCodeParser.Parse(donorUnitIdText),
            listSelector);
    }

    public TemplateWeaponSetReplacementSettings(uint targetUnitId, uint donorUnitId, uint listSelector)
    {
        if (targetUnitId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetUnitId), "Target unit id must be non-zero.");
        }

        if (donorUnitId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(donorUnitId), "Donor unit id must be non-zero.");
        }

        if (listSelector == 0 || (listSelector & ~ListAll) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(listSelector), "List selector must be a non-empty subset of the four WeaponSetUpdate lists.");
        }

        TargetUnitId = targetUnitId;
        DonorUnitId = donorUnitId;
        ListSelector = listSelector;
    }

    public uint TargetUnitId { get; }

    public uint DonorUnitId { get; }

    public uint ListSelector { get; }
}
