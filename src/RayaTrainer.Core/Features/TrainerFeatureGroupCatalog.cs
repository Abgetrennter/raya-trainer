using RayaTrainer.Core.Manifest;

namespace RayaTrainer.Core.Features;

public sealed record TrainerFeatureGroupDefinition(
    string GroupId,
    string Name,
    IReadOnlyList<string> FeatureRawNames,
    bool IsExpanded = true);

public static class TrainerFeatureGroupCatalog
{
    public static IReadOnlyList<TrainerFeatureGroupDefinition> Groups { get; } =
    [
        new("player-resources", "玩家资源", ["Money", "Power", "SC POINT", "HAVE ALL SC"]),
        new("build-map", "建造与地图", ["FAST BUILD", "MAP", "Zoom", "Enemy Can't Build", "Free Build", "Ignore Prerequisites", "Ignore Quantity Limit", "Expand Production Queue", "Restore Production Queue", "Clear Player Tech Locks"]),
        new("render-perf", "渲染与性能", ["Frame Rate Unlock 60fps"]),
        new("other-ops", "其他操作", ["Danger Level MAX", "Danger Level MIN", "Restore Danger Level Normal", "Restore Select Ore Mine", "Run In Background", "Logic Time Freeze", "Logic Time Slow Motion", "Challenge Money", "Challenge Time"], false),
        new("battlefield-support", "战场支援", ["Product Veterancy Grant", "Product Healing Aura Enable", "Product Healing Aura Disable", "Product Spawn Mecha King", "Product Spawn Arsenal MCV", "Product Spawn Ore Node"])
    ];

    /// <summary>
    /// 选中单位独立选项卡所包含的 4 个分组的所有 RawName。
    /// 供 MainViewModel 预过滤 feature 列表时使用。
    /// </summary>
    public static IReadOnlyList<string> SelectedUnitGroupingRawNames { get; } =
    [
        // 伤害与无敌
        "Player God Mode",
        "Player One Kill Mode",
        // 选中单位 · 生命值
        "Select Unit HP MAX",
        "Select Unit HP MIN",
        "Restore Select Unit Normal HP",
        "Set Selected Unit Target Health",
        // 选中单位 · 其他
        "Select Unit Level UP",
        "Teleport Selected Units To Mouse",
        "Fill Selected Unit Ammo",
        "Reset Selected Unit Ammo",
        "Select Unit Change ID",
        "Destory Select Unit",
        "Set Unit Support State",
        // 选中单位 · 速度（AttributeModifier 产品入口）
        "Product Selected Unit Speed Fast",
        "Product Selected Unit Speed Slow",
        "Product Selected Unit Speed Freeze",
        "Product Selected Unit Speed Restore",
        "Product Selected Unit Move Speed Custom",
        // 选中单位 · 其他（属性修改产品入口）
        "Product Selected Unit Attack Speed Enable",
        "Product Selected Unit Attack Speed Disable",
        "Product Selected Unit Attack Speed Custom",
        "Product Selected Unit Attack Range Enable",
        "Product Selected Unit Attack Range Disable",
        "Product Selected Unit Attack Range Custom",
        "Product Selected Unit Attack Damage Custom",
        "Product Selected Unit Max Health Custom",
        "Product Clear Attack Speed Effects",
        "Product Clear Attack Range Effects"
    ];

    public static string GetGroupName(TrainerFeature feature)
    {
        return Groups.FirstOrDefault(group => group.FeatureRawNames.Contains(feature.RawName, StringComparer.Ordinal))?.Name
            ?? "秘密协议与扩展操作";
    }
}

public static class GroupIds
{
    public const string SelectedUnitDamage = "selected-unit.damage";
    public const string SelectedUnitHealth = "selected-unit.health";
    public const string SelectedUnitSpeed = "selected-unit.speed";
    public const string SelectedUnitOther = "selected-unit.other";
}
