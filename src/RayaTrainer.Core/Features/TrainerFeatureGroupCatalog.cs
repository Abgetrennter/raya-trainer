using RayaTrainer.Core.Manifest;

namespace RayaTrainer.Core.Features;

public sealed record TrainerFeatureGroupDefinition(
    string Name,
    IReadOnlyList<string> FeatureDisplayNames,
    bool IsExpanded = true);

public static class TrainerFeatureGroupCatalog
{
    public static IReadOnlyList<TrainerFeatureGroupDefinition> Groups { get; } =
    [
        new("玩家资源", ["增加玩家战场资金", "无限电力", "无限秘密协议点数", "解开所有秘密协议技能"]),
        new("建造与地图", ["快速建造建筑物/单位", "消散战争迷雾", "无限缩放", "禁止电脑建造建筑物/单位", "建筑物可随地建造", "忽略建造前置条件", "解除同时存在数量上限（超武/英雄多造，英雄需配合忽略建造前置条件）", "扩展选中建筑建造队列", "恢复选中建筑建造队列", "清除玩家科技锁"]),
        new("渲染与性能", ["60fps 帧率解锁"]),
        new("其他操作", ["威胁等级最大", "威胁等级归零", "威胁等级恢复原状", "选择的矿脉恢复采集矿量", "允许后台响应（失焦时修改器仍可响应，仅限单机/遭遇战）", "时间冻结（伪回合制，仅限单机/遭遇战）", "时间慢放（50% 速度，仅限单机/遭遇战）"], false)
    ];

    /// <summary>
    /// 选中单位独立选项卡所包含的 4 个分组的所有 DisplayName。
    /// 供 MainViewModel 预过滤 feature 列表时使用。
    /// </summary>
    public static IReadOnlyList<string> SelectedUnitGroupingNames { get; } =
    [
        // 伤害与无敌
        "玩家全建筑/单位无敌",
        "一击必杀敌方建筑物/单位",
        // 选中单位 · 生命值
        "选择的建筑物/单位无限生命值",
        "选择的建筑物/单位生命值变为1",
        "选择的建筑物/单位恢复原本的生命值",
        "设置选中建筑物/单位生命值为指定值",
        // 选中单位 · 速度
        "选择的单位高速移动",
        "选择的单位缓慢移动",
        "选择的单位暂停",
        "选择的单位恢复速度",
        // 选中单位 · 其他
        "选择的单位快速升级",
        "移动选中单位到鼠标位置",
        "选择的单位满攻速（切换，仅当前实例）",
        "清空满攻速单位",
        "选择的单位无限射程与索敌（切换，仅当前实例）",
        "清空无限射程单位",
        "选择的单位弹药填满",
        "选择的单位弹药归1",
        "俘虏选择的建筑物/单位",
        "摧毁选择的建筑物/单位",
        "选择的建筑物/单位设置伪装状态"
    ];

    public static string GetGroupName(TrainerFeature feature)
    {
        return Groups.FirstOrDefault(group => group.FeatureDisplayNames.Contains(feature.DisplayName, StringComparer.Ordinal))?.Name
            ?? "秘密协议与扩展操作";
    }
}
