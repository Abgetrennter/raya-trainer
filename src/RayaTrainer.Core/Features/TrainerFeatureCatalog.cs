using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Hotkeys;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Features;

public static class TrainerFeatureCatalog
{
    internal const string ExpandProductionQueueRawName = TrainerFeatureIds.ExpandProductionQueue;
    internal const string RestoreProductionQueueRawName = TrainerFeatureIds.RestoreProductionQueue;
    internal const string TeleportSelectedUnitsToMouseRawName = TrainerFeatureIds.TeleportSelectedUnitsToMouse;
    internal const string ClearSelectedAttackSpeedEffectsRawName = TrainerFeatureIds.ClearSelectedAttackSpeedEffects;
    internal const string ClearSelectedAttackRangeEffectsRawName = TrainerFeatureIds.ClearSelectedAttackRangeEffects;

    private static readonly IReadOnlyDictionary<string, FeatureOverride> SourceTrainerOverrides =
        new Dictionary<string, FeatureOverride>(StringComparer.Ordinal)
        {
            ["Moeny"] = new("增加玩家战场资金", "Ctrl+F1", null, null, null, false),
            ["Power"] = new("无限电力", "Ctrl+F2", null, null, null, false, true),
            ["SC POINT"] = new("无限秘密协议点数", "Ctrl+F3", null, null, null, false),
            ["HAVE ALL SC"] = new("解开所有秘密协议技能", "Ctrl+F4", null, null, null, false),
            ["FAST BUILD"] = new("快速建造建筑物/单位", "Ctrl+F5", null, null, null, false),
            ["SUPER POWER"] = new("秘密协议技能与超级武器快速冷却", "Ctrl+F6", null, null, null, false),
            ["Disable ALL SP"] = new("禁止使用技能", "Ctrl+F7", null, null, null, false),
            ["Zoom"] = new("无限缩放", "Ctrl+F8", null, null, null, false),
            ["MAP"] = new("消散战争迷雾", "Ctrl+F9", null, null, null, false),
            ["Enemy Can't Build"] = new("禁止电脑建造建筑物/单位", "Ctrl+F10", null, null, null, false),
            ["Player God Mode"] = new("玩家全建筑/单位无敌", "Ctrl+F11", null, null, null, false),
            ["Player One Kill Mode"] = new("一击必杀敌方建筑物/单位", "Ctrl+F12", null, null, null, false),
            ["Challenge Money"] = new("起义时刻挑战模式增加资金", "Insert", null, null, null, false),
            ["Challenge Time"] = new("起义时刻挑战模式锁定充足时间", "Home", null, null, null, false),
            ["Select Unit Level UP"] = new("选择的单位快速升级", "P", null, null, null, false),
            ["Select Unit Super Speed"] = new("选择的单位高速移动", "-", null, null, null, false),
            ["Select Unit Slow Speed"] = new("选择的单位缓慢移动", "=", null, null, null, false),
            ["Select Unit Freeze"] = new("选择的单位暂停", "Page Up", null, null, null, false),
            ["Restore Select Unit Speed"] = new("选择的单位恢复速度", "Page Down", null, null, null, false),
            ["Select Unit HP MAX"] = new("选择的建筑物/单位无限生命值", "[", null, null, null, false),
            ["Select Unit HP MIN"] = new("选择的建筑物/单位生命值变为1", "]", null, null, null, false),
            ["Restore Select Unit Normal HP"] = new("选择的建筑物/单位恢复原本的生命值", "\\", null, null, null, false),
            ["Select Unit Ammo MAX"] = new(null, null, null, null, null, true),
            ["Select Unit Ammo MAX 2"] = new(null, null, null, null, null, true),
            ["Select Unit Change ID"] = new("俘虏选择的建筑物/单位", "O", null, null, null, false),
            ["Destory Select Unit"] = new("摧毁选择的建筑物/单位", "Delete", null, null, null, false),
            ["Danger Level MAX"] = new("威胁等级最大", ",", null, null, null, false),
            ["Danger Level MIN"] = new("威胁等级归零", ".", null, null, null, false),
            ["Restore Danger Level Normal"] = new("威胁等级恢复原状", "/", null, null, null, false),
            ["Restore Select Ore Mine"] = new("选择的矿脉恢复采集矿量", "'", null, null, null, false),
            ["Free Build"] = new(
                "建筑物可随地建造",
                "L",
                ["Free Build"],
                null,
                null,
                false,
                true),
            ["Get Me Base"] = new("给玩家基地车", "K", null, null, null, false),
            ["We Need Back"] = new("呼叫战场增援", "J", null, null, null, false),
            ["Select Unit Copy For Me"] = new("复制选择的建筑物/单位给玩家", "I", null, null, null, false)
        };

    private static readonly TrainerFeature SetUnitSupportStateFeature =
        new(
            "Set Unit Support State",
            "选择的建筑物/单位设置伪装状态",
            null,
            [],
            null,
            "0x0E",
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature SecretProtocolBindingProbeFeature =
        new(
            "Secret Protocol Binding Probe",
            "秘密协议绑定验证",
            null,
            [],
            null,
            "0x0F");

    private static readonly TrainerFeature OrbitalRefuseRankOneProbeFeature =
        new(
            "Soviet Orbital Refuse Rank 1 Probe",
            "授予苏联轨道垃圾1级协议",
            null,
            [],
            null,
            "0x10");

    public static readonly TrainerFeature SecretProtocolGrantFeature =
        new(
            "Grant Secret Protocol",
            "授予秘密协议",
            null,
            [],
            null,
            "0x11");

    public static readonly TrainerFeature SelectedObjectUpgradeGrantFeature =
        new(
            "Grant Selected Object Upgrade",
            "授予选中建筑 Upgrade",
            null,
            [],
            null,
            "0x12",
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature ClearPlayerTechLocksFeature =
        new(
            "Clear Player Tech Locks",
            "清除玩家科技锁",
            null,
            [],
            null,
            "0x13");

    public static readonly TrainerFeature TemplateModelReplacementFeature =
        new(
            "Replace Template Model",
            "替换单位模板模型",
            null,
            [],
            null,
            "0x14");

    public static readonly TrainerFeature TemplateWeaponReplacementFeature =
        new(
            "Replace Template Weapon",
            "替换单位模板武器",
            null,
            [],
            null,
            "0x15");

    private static readonly TrainerFeature SetTargetHealthFeature =
        new(
            "Set Selected Unit Target Health",
            "设置选中建筑物/单位生命值为指定值",
            null,
            [],
            null,
            "0x16",
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature FillSelectedUnitAmmoFeature =
        new(
            "Fill Selected Unit Ammo",
            "选择的单位弹药填满",
            null,
            [],
            null,
            "0x17",
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature ResetSelectedUnitAmmoFeature =
        new(
            "Reset Selected Unit Ammo",
            "选择的单位弹药归1",
            null,
            [],
            null,
            "0x18",
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature ToggleSelectedUnitAttackSpeedFeature =
        new(
            "Toggle Selected Unit Attack Speed",
            "选择的单位满攻速（切换，仅当前实例）",
            ";",
            [],
            null,
            "0x19",
            SupportedProfileIds: ["ra3_1.12", "ra3_1.13", "ra3_uprising_1.0", "ra3_uprising_1.1"],
            SelectionMode: SelectionExecutionMode.SmartToggle);

    private static readonly TrainerFeature ToggleSelectedUnitAttackRangeFeature =
        new(
            "Toggle Selected Unit Attack Range",
            "选择的单位无限射程与索敌（切换，仅当前实例）",
            null,
            [],
            null,
            "0x1B",
            SupportedProfileIds: ["ra3_1.12"],
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature ClearSelectedAttackSpeedEffectsFeature =
        new(
            TrainerFeatureIds.ClearSelectedAttackSpeedEffects,
            "清空满攻速单位",
            null,
            [],
            null,
            null,
            RequiresDirectGameApi: true,
            SupportedProfileIds: ["ra3_1.12", "ra3_1.13", "ra3_uprising_1.0", "ra3_uprising_1.1"],
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature ClearSelectedAttackRangeEffectsFeature =
        new(
            TrainerFeatureIds.ClearSelectedAttackRangeEffects,
            "清空无限射程单位",
            null,
            [],
            null,
            null,
            RequiresDirectGameApi: true,
            SupportedProfileIds: ["ra3_1.12"],
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature SecretProtocolDependencyBypassFeature =
        new(
            "Secret Protocol Dependency Bypass",
            "秘密协议忽略基地需求",
            null,
            [TrainerFeatureIds.SecretProtocolDependencyBypass],
            null,
            null);

    private static readonly TrainerFeature IgnorePrerequisitesFeature =
        new(
            "Ignore Prerequisites",
            "忽略建造前置条件",
            null,
            ["Ignore Prerequisites"],
            null,
            null);

    private static readonly TrainerFeature IgnoreQuantityLimitFeature =
        new(
            "Ignore Quantity Limit",
            "解除同时存在数量上限（超武/英雄多造，英雄需配合忽略建造前置条件）",
            null,
            ["Ignore Quantity Limit"],
            null,
            null);

    private static readonly TrainerFeature ExpandProductionQueueFeature =
        new(
            ExpandProductionQueueRawName,
            "扩展选中建筑建造队列",
            null,
            [],
            null,
            "0x19",
            RequiresDirectGameApi: true,
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature RestoreProductionQueueFeature =
        new(
            RestoreProductionQueueRawName,
            "恢复选中建筑建造队列",
            null,
            [],
            null,
            "0x19",
            RequiresDirectGameApi: true,
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature TeleportSelectedUnitsToMouseFeature =
        new(
            TeleportSelectedUnitsToMouseRawName,
            "移动选中单位到鼠标位置",
            null,
            [],
            null,
            "0x1A",
            RequiresDirectGameApi: true,
            SelectionMode: SelectionExecutionMode.Apply);

    private static readonly TrainerFeature RunInBackgroundFeature =
        new(
            "Run In Background",
            "允许后台响应（失焦时修改器仍可响应，仅限单机/遭遇战）",
            null,
            ["Run In Background"],
            null,
            null);

    private static readonly TrainerFeature LogicTimeFreezeFeature =
        new(
            "Logic Time Freeze",
            "时间冻结（伪回合制，仅限单机/遭遇战）",
            null,
            ["Logic Time Freeze"],
            null,
            null);

    private static readonly TrainerFeature LogicTimeSlowMotionFeature =
        new(
            "Logic Time Slow Motion",
            "时间慢放（50% 速度，仅限单机/遭遇战）",
            null,
            ["Logic Time Slow Motion"],
            null,
            null);

    private static readonly byte[] BezierAccelerationScaleCode =
    [
        0xF3, 0x0F, 0x10, 0x8A, 0xF0, 0x00, 0x00, 0x00,
        0xF3, 0x0F, 0x59, 0x0D, 0x20, 0x64, 0xBC, 0x00,
        0xE9, 0x0A, 0xAC, 0xB1, 0xFF,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x80, 0x3E
    ];

    private static readonly TrainerFeature FrameRateUnlockFeature =
        new(
            "Frame Rate Unlock 60fps",
            "60fps 帧率解锁",
            null,
            ["Frame Rate Unlock 60fps"],
            null,
            null,
            [
                new(GameTarget.ProcessName + "+8AD5F4", BitConverter.GetBytes(60), BitConverter.GetBytes(15)),
                new(GameTarget.ProcessName + "+8AF9D0", BitConverter.GetBytes(30), BitConverter.GetBytes(15)),
                new(GameTarget.ProcessName + "+8DBC4C", BitConverter.GetBytes(30.0f * 0.001f), BitConverter.GetBytes(15.0f * 0.001f)),
                new(GameTarget.ProcessName + "+8DBC1C", BitConverter.GetBytes(1000.0f / 30.0f), BitConverter.GetBytes(1000.0f / 15.0f)),
                new(GameTarget.ProcessName + "+8DBC58", BitConverter.GetBytes(30.0f), BitConverter.GetBytes(15.0f)),
                new(GameTarget.ProcessName + "+8DBC50", BitConverter.GetBytes(60.0f), BitConverter.GetBytes(30.0f)),
                new(GameTarget.ProcessName + "+8DBC54", BitConverter.GetBytes(1000.0f / 60.0f), BitConverter.GetBytes(1000.0f / 30.0f)),
                new(GameTarget.ProcessName + "+8DBC94", BitConverter.GetBytes(1.0f / 30.0f), BitConverter.GetBytes(1.0f / 15.0f)),
                new(GameTarget.ProcessName + "+8DBD34", BitConverter.GetBytes(1.0f / 60.0f), BitConverter.GetBytes(1.0f / 30.0f)),
                new(GameTarget.ProcessName + "+8E5A5C", BitConverter.GetBytes(0), BitConverter.GetBytes(0)),
                new(GameTarget.ProcessName + "+8E176C", BitConverter.GetBytes(16), BitConverter.GetBytes(33)),
                new(GameTarget.ProcessName + "+7C63D4", BitConverter.GetBytes(0.03f), BitConverter.GetBytes(0.03f)),
                new(GameTarget.ProcessName + "+8DBC5C", BitConverter.GetBytes(30.0f * 0.001f), BitConverter.GetBytes(30.0f * 0.001f)),
                new(GameTarget.ProcessName + "+1FEC91", [0x10], [0x1D]),
                new(GameTarget.ProcessName + "+1FECA3", [0x10], [0x1D]),
                new(GameTarget.ProcessName + "+229853", [0xEB], [0x73]),
                new(GameTarget.ProcessName + "+13E90A", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(GameTarget.ProcessName + "+1FFAD1", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(GameTarget.ProcessName + "+216257", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(GameTarget.ProcessName + "+2297C9", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(GameTarget.ProcessName + "+7B30D8", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(GameTarget.ProcessName + "+7B3108", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(GameTarget.ProcessName + "+7B3138", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(GameTarget.ProcessName + "+7B3C59", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(GameTarget.ProcessName + "+2C17CC", [0xD4, 0x63, 0xBC], [0x5C, 0xBC, 0xCD]),
                new(GameTarget.ProcessName + "+8DBC5C", BitConverter.GetBytes(60.0f * 0.001f), BitConverter.GetBytes(30.0f * 0.001f)),
                new(GameTarget.ProcessName + "+7C63D4", BitConverter.GetBytes(0.03f), BitConverter.GetBytes(0.0f)),
                new(GameTarget.ProcessName + "+1EB6F6", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(GameTarget.ProcessName + "+1EB6FC", [0xF4, 0xD5], [0xD4, 0xF9]),
                new(
                    GameTarget.ProcessName + "+7C6400",
                    BezierAccelerationScaleCode,
                    BezierAccelerationScaleCode),
                new(
                    GameTarget.ProcessName + "+2E1017",
                    [0xE9, 0xE4, 0x53, 0x4E, 0x00, 0x90, 0x90, 0x90],
                    [0xF3, 0x0F, 0x10, 0x8A, 0xF0, 0x00, 0x00, 0x00])
            ],
            SupportedProfileIds: ["ra3_1.12"]);

    public static IReadOnlyList<TrainerFeature> CreateUiFeatures(IEnumerable<TrainerFeature> features) => CreateGridFeatures(features);

    public static IReadOnlyList<TrainerFeature> CreateGridFeatures(IEnumerable<TrainerFeature> features)
    {
        return features
            .Select(ApplySourceTrainerOverride)
            .Where(feature => feature is not null)
            .Cast<TrainerFeature>()
            .Concat(
            [
                SetUnitSupportStateFeature,
                ClearPlayerTechLocksFeature,
                SecretProtocolDependencyBypassFeature,
                IgnorePrerequisitesFeature,
                IgnoreQuantityLimitFeature,
                ExpandProductionQueueFeature,
                RestoreProductionQueueFeature,
                TeleportSelectedUnitsToMouseFeature,
                RunInBackgroundFeature,
                FrameRateUnlockFeature,
                LogicTimeFreezeFeature,
                LogicTimeSlowMotionFeature,
                SetTargetHealthFeature,
                FillSelectedUnitAmmoFeature,
                ResetSelectedUnitAmmoFeature,
                ToggleSelectedUnitAttackSpeedFeature,
                ToggleSelectedUnitAttackRangeFeature,
                ClearSelectedAttackSpeedEffectsFeature,
                ClearSelectedAttackRangeEffectsFeature
            ])
            .ToArray();
    }

    public static IReadOnlyList<TrainerFeature> CreatePanelActions()
    {
        return
        [
            SecretProtocolGrantFeature,
            SelectedObjectUpgradeGrantFeature,
            TemplateModelReplacementFeature,
            TemplateWeaponReplacementFeature
        ];
    }

    public static IReadOnlyDictionary<string, string> CreateDefaultHotkeys(IEnumerable<TrainerFeature> features)
    {
        // 配置字典的 key 使用 RawName（稳定标识），避免后续 UI 文案改名导致用户已配置热键失效。
        var hotkeys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var feature in features)
        {
            if (string.IsNullOrWhiteSpace(feature.RawName) ||
                !HotkeyGesture.TryParse(feature.Hotkey, out var gesture))
            {
                continue;
            }

            hotkeys[feature.RawName] = gesture.DisplayText;
        }

        return hotkeys;
    }

    public static IReadOnlyList<TrainerFeature> ApplyHotkeyOverrides(
        IEnumerable<TrainerFeature> features,
        IReadOnlyDictionary<string, string> hotkeys)
    {
        return features
            .Select(feature => feature with { Hotkey = ResolveHotkey(feature, hotkeys) })
            .ToArray();
    }

    private static TrainerFeature? ApplySourceTrainerOverride(TrainerFeature feature)
    {
        if (!SourceTrainerOverrides.TryGetValue(feature.RawName, out var featureOverride))
        {
            return feature with { SelectionMode = ResolveSelectionMode(feature.RawName) };
        }

        if (featureOverride.Hide)
        {
            return null;
        }

        return feature with
        {
            DisplayName = featureOverride.DisplayName ?? feature.DisplayName,
            Hotkey = featureOverride.Hotkey ?? feature.Hotkey,
            EnableFlags = featureOverride.EnableFlags ?? feature.EnableFlags,
            DispatchTarget = featureOverride.DispatchTarget ?? feature.DispatchTarget,
            ValueHint = featureOverride.HasValueHintOverride ? featureOverride.ValueHint : feature.ValueHint,
            ToggleBytePatches = featureOverride.ToggleBytePatches,
            SelectionMode = ResolveSelectionMode(feature.RawName)
        };
    }

    private static SelectionExecutionMode? ResolveSelectionMode(string rawName) => rawName switch
    {
        // SmartToggle: 两阶段智能统一
        "Toggle Selected Unit Attack Speed" => SelectionExecutionMode.SmartToggle,
        // SingleTarget: 仅首个选中单位
        "Destory Select Unit" => SelectionExecutionMode.SingleTarget,
        "Select Unit Copy For Me" => SelectionExecutionMode.SingleTarget,
        // Apply: 单阶段遍历直接执行
        "Select Unit HP MAX" => SelectionExecutionMode.Apply,
        "Select Unit HP MIN" => SelectionExecutionMode.Apply,
        "Restore Select Unit Normal HP" => SelectionExecutionMode.Apply,
        "Set Selected Unit Target Health" => SelectionExecutionMode.Apply,
        "Select Unit Super Speed" => SelectionExecutionMode.Apply,
        "Select Unit Slow Speed" => SelectionExecutionMode.Apply,
        "Select Unit Freeze" => SelectionExecutionMode.Apply,
        "Restore Select Unit Speed" => SelectionExecutionMode.Apply,
        "Fill Selected Unit Ammo" => SelectionExecutionMode.Apply,
        "Reset Selected Unit Ammo" => SelectionExecutionMode.Apply,
        "Select Unit Level UP" => SelectionExecutionMode.Apply,
        "Select Unit Change ID" => SelectionExecutionMode.Apply,
        "Teleport Selected Units To Mouse" => SelectionExecutionMode.Apply,
        "Expand Production Queue" => SelectionExecutionMode.Apply,
        "Restore Production Queue" => SelectionExecutionMode.Apply,
        "Set Unit Support State" => SelectionExecutionMode.Apply,
        "Grant Selected Object Upgrade" => SelectionExecutionMode.Apply,
        // Attack Range 暂不改，标为 Apply 保持现状
        "Toggle Selected Unit Attack Range" => SelectionExecutionMode.Apply,
        "Clear Selected Attack Speed Effects" => SelectionExecutionMode.Apply,
        "Clear Selected Attack Range Effects" => SelectionExecutionMode.Apply,
        _ => null,
    };

    private static string? ResolveHotkey(TrainerFeature feature, IReadOnlyDictionary<string, string> hotkeys)
    {
        if (hotkeys.TryGetValue(feature.RawName, out var hotkey))
        {
            return HotkeyGesture.TryParse(hotkey, out var gesture)
                ? gesture.DisplayText
                : null;
        }

        return feature.Hotkey;
    }

    private sealed record FeatureOverride(
        string? DisplayName,
        string? Hotkey,
        IReadOnlyList<string>? EnableFlags,
        string? DispatchTarget,
        string? ValueHint,
        bool Hide,
        bool HasValueHintOverride = false,
        IReadOnlyList<TrainerFeatureBytePatch>? ToggleBytePatches = null);


}
