using System;
using System.Collections.Generic;

namespace RayaTrainer.Core.RuntimeAssets.AttributeModifiers;

// AscensionModifierCatalog — the full official RA3 AttributeType roster (34 usable kinds, NONE
// excluded) presented by the Ascension matrix page. Enumeration values follow the official
// AssetTypeAttributeModifier.xsd order, which matches the runtime AttributeType enum observed in
// the bin serialization evidence (atlas:evidence.asset_bin_field_serialization).
//
// Aggregation classes each kind into the two quantizer ladders: Speed is the confirmed
// multiplicative aggregate (products of factors); every other kind is provisionally registered
// additive (1 + signed increment) pending live evidence — the asset-pack plan keeps both ladders
// available per kind so a reclassification never requires a re-pack (design doc 2026-08-17).
public enum AscensionAggregation
{
    Additive = 0,
    Multiplicative = 1,
}

public sealed record AscensionModifierEntry(
    int AttributeType,
    string EnumName,
    string DisplayName,
    string GroupId,
    AscensionAggregation Aggregation);

public static class AscensionModifierCatalog
{
    public const string GroupWeapon = "weapon";
    public const string GroupMobility = "mobility";
    public const string GroupSurvival = "survival";
    public const string GroupResist = "resist";
    public const string GroupVision = "vision";
    public const string GroupEconomy = "economy";
    public const string GroupMisc = "misc";

    public static readonly IReadOnlyList<(string GroupId, string Title)> Groups =
    [
        (GroupWeapon, "武器与伤害"),
        (GroupMobility, "机动与碾压"),
        (GroupSurvival, "生存与防御"),
        (GroupResist, "抗性"),
        (GroupVision, "视野与广播"),
        (GroupEconomy, "经济与生产"),
        (GroupMisc, "其他"),
    ];

    // XSD enumeration order; AttributeType numeric == index in the official schema.
    public static readonly IReadOnlyList<AscensionModifierEntry> All =
    [
        new(1,  "ARMOR",                          "护甲（受伤倍率）",        GroupSurvival, AscensionAggregation.Additive),
        new(2,  "DAMAGE_ADD",                     "伤害固定加值",            GroupWeapon,   AscensionAggregation.Additive),
        new(3,  "DAMAGE_MULT",                    "伤害倍率",                GroupWeapon,   AscensionAggregation.Additive),
        new(4,  "RESIST_FEAR",                    "恐惧抗性",                GroupResist,   AscensionAggregation.Additive),
        new(5,  "RESIST_TERROR",                  "恐怖抗性",                GroupResist,   AscensionAggregation.Additive),
        new(6,  "EXPERIENCE",                     "经验获取",                GroupMisc,     AscensionAggregation.Additive),
        new(7,  "RANGE",                          "攻击射程",                GroupWeapon,   AscensionAggregation.Additive),
        new(8,  "SPEED",                          "移动速度",                GroupMobility, AscensionAggregation.Multiplicative),
        new(9,  "CRUSH_DECELERATE",               "碾压时减速",              GroupMobility, AscensionAggregation.Additive),
        new(10, "RESIST_KNOCKBACK",               "击退抗性",                GroupResist,   AscensionAggregation.Additive),
        new(11, "SPELL_DAMAGE",                   "法术伤害",                GroupWeapon,   AscensionAggregation.Additive),
        new(12, "RECHARGE_TIME",                  "技能充能时间",            GroupMisc,     AscensionAggregation.Additive),
        new(13, "PRODUCTION",                     "生产速度",                GroupEconomy,  AscensionAggregation.Additive),
        new(14, "PRODUCTION_COST",                "生产费用",                GroupEconomy,  AscensionAggregation.Additive),
        new(15, "HEALTH",                         "生命值固定加值",          GroupSurvival, AscensionAggregation.Additive),
        new(16, "HEALTH_MULT",                    "最大生命倍率",            GroupSurvival, AscensionAggregation.Additive),
        new(17, "VISION",                         "索敌/锁定距离",           GroupVision,   AscensionAggregation.Additive),
        new(18, "BOUNTY_PERCENTAGE",              "击杀赏金比例",            GroupEconomy,  AscensionAggregation.Additive),
        new(19, "MIN_CRUSH_VELOCITY_PERCENTAGE",  "最小碾压速度要求",        GroupMobility, AscensionAggregation.Additive),
        new(20, "AUTO_HEAL",                      "自愈能力",                GroupSurvival, AscensionAggregation.Additive),
        new(21, "SHROUD_CLEARING",                "迷雾清除范围",            GroupVision,   AscensionAggregation.Additive),
        new(22, "RATE_OF_FIRE",                   "攻击射速",                GroupWeapon,   AscensionAggregation.Additive),
        new(23, "DAMAGE_STRUCTURE_BOUNTY_ADD",    "打建筑赏金加值",          GroupEconomy,  AscensionAggregation.Additive),
        new(24, "CRUSHER_LEVEL",                  "碾压等级",                GroupMobility, AscensionAggregation.Additive),
        new(25, "COMMAND_POINT_BONUS",            "指挥点上限",              GroupEconomy,  AscensionAggregation.Additive),
        new(26, "CRUSHABLE_LEVEL",                "抗碾压等级",              GroupMobility, AscensionAggregation.Additive),
        new(27, "CRUSHED_DECELERATE",             "被碾压时减速",            GroupMobility, AscensionAggregation.Additive),
        new(28, "INVULNERABLE",                   "无敌",                    GroupSurvival, AscensionAggregation.Additive),
        new(29, "SUPPRESSABILITY",                "受压制能力",              GroupSurvival, AscensionAggregation.Additive),
        new(30, "RESIST_EMP",                     "EMP 抗性",                GroupResist,   AscensionAggregation.Additive),
        new(31, "POWER_BOOST",                    "电力输出",                GroupEconomy,  AscensionAggregation.Additive),
        new(32, "AREA_OF_EFFECT",                 "溅射/效果范围",           GroupWeapon,   AscensionAggregation.Additive),
        new(33, "COLLISION_GEOMETRY_SIZE_MULT",   "碰撞体积倍率",            GroupMisc,     AscensionAggregation.Additive),
        new(34, "BROADCAST_RANGE",                "广播/光环范围",           GroupVision,   AscensionAggregation.Additive),
    ];

    public static string GroupTitle(string groupId)
    {
        foreach (var (id, title) in Groups)
        {
            if (id == groupId)
            {
                return title;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(groupId), groupId, "未知属性修改分组。");
    }
}
