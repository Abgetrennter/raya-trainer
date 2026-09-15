namespace RayaTrainer.Core.Features;

using RayaTrainer.Core.Agent;

/// <summary>
/// 武器槽随机打乱编排器：对模板池按槽位序（Ordering）做跨模板默认武器指针置换。
/// 模板级改动只影响之后创建的单位——即"后续生产单位武器随机打乱"的错误科技式效果。
/// 每个槽位序独立做 Fisher-Yates 置换；指针相同的条目跳过直挂。
/// </summary>
public static class WeaponSetShuffler
{
    public sealed record ShuffleOutcome(
        int TemplatesRead,
        int AttachAttempted,
        int AttachSucceeded,
        IReadOnlyList<string> Log);

    public const uint ListSelectorAll = TemplateWeaponSetReplacementSettings.ListAll;

    public static ShuffleOutcome ShuffleDefaults(
        ITrainerFeatureController controller,
        IReadOnlyList<uint> poolUnitIds,
        uint listSelector,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (poolUnitIds.Count < 2)
        {
            throw new ArgumentException("打乱池至少需要两个单位模板。", nameof(poolUnitIds));
        }
        ArgumentNullException.ThrowIfNull(random);

        var log = new List<string>();
        var snapshots = new List<TemplateWeaponSetSnapshot>();
        foreach (var unitId in poolUnitIds)
        {
            var snapshot = controller.ReadTemplateWeaponSetSnapshot(unitId, listSelector);
            snapshots.Add(snapshot);
            if (snapshot.Entries.Count == 0)
            {
                log.Add($"0x{unitId:X8}: 无武器槽（跳过）。");
            }
        }

        var attachAttempted = 0;
        var attachSucceeded = 0;
        // 每个槽位序独立置换：模板 → 当前默认武器指针。
        for (uint ordering = 0; ordering <= WeaponAttachSettings.MaxOrdering; ordering++)
        {
            var participants = new List<(uint UnitId, uint Weapon)>();
            foreach (var snapshot in snapshots)
            {
                uint? weapon = snapshot.Entries
                    .Where(e => e.Ordering == ordering && !e.UpgradeGated)
                    .Select(e => (uint?)e.WeaponTemplateAddress)
                    .FirstOrDefault();
                if (weapon is not null)
                {
                    participants.Add((snapshot.TargetUnitId, weapon.Value));
                }
            }

            if (participants.Count < 2)
            {
                continue;
            }

            // Fisher-Yates 置换指针。
            var weapons = participants.Select(p => p.Weapon).ToList();
            for (var i = weapons.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (weapons[i], weapons[j]) = (weapons[j], weapons[i]);
            }

            for (var i = 0; i < participants.Count; i++)
            {
                var (unitId, currentWeapon) = participants[i];
                var newWeapon = weapons[i];
                if (newWeapon == currentWeapon)
                {
                    continue;
                }

                attachAttempted++;
                try
                {
                    var status = controller.AttachWeaponTemplate(unitId, newWeapon, ordering, listSelector);
                    if (status == GameApiDispatchStatus.Completed)
                    {
                        attachSucceeded++;
                        log.Add($"0x{unitId:X8}: 槽位序 {ordering} -> 武器 0x{newWeapon:X8}。");
                    }
                    else
                    {
                        log.Add($"0x{unitId:X8}: 槽位序 {ordering} 直挂超时（{status}）。");
                    }
                }
                catch (Exception ex)
                {
                    log.Add($"0x{unitId:X8}: 槽位序 {ordering} 直挂失败（{ex.Message}）。");
                }
            }
        }

        return new ShuffleOutcome(
            snapshots.Count(s => s.Entries.Count > 0), attachAttempted, attachSucceeded, log);
    }
}
