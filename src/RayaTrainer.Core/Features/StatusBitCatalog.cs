using System.Reflection;
using System.Text.Json;
using RayaTrainer.Core.Assets;

namespace RayaTrainer.Core.Features;

public enum StatusBitDomain : uint
{
    ObjectStatus = 0,
    ModelConditionFlags = 1
}

public enum StatusBitRiskLevel
{
    Normal,
    Volatile,
    Dangerous
}

public sealed record StatusBitDefinition(
    StatusBitDomain Domain,
    uint BitIndex,
    string Name,
    string Category,
    StatusBitRiskLevel RiskLevel,
    bool IsHiddenByDefault,
    bool IsRecommendedFunction,
    string HelpText)
{
    public bool IsDangerous => RiskLevel == StatusBitRiskLevel.Dangerous;
}

public static class StatusBitCatalog
{
    private static readonly Lazy<IReadOnlyList<StatusBitDefinition>> ObjectStatusesLazy =
        new(() => Create(StatusBitDomain.ObjectStatus, ObjectStatusNames!));

    private static readonly Lazy<IReadOnlyList<StatusBitDefinition>> ModelConditionsLazy =
        new(() => Create(StatusBitDomain.ModelConditionFlags, ModelConditionNames!));

    private static readonly Lazy<IReadOnlyList<StatusBitDefinition>> AllLazy =
        new(() => ObjectStatuses.Concat(ModelConditions).ToArray());

    private static readonly Lazy<IReadOnlyList<StatusBitDefinition>> DefaultVisibleLazy =
        new(() => All.Where(item => !item.IsHiddenByDefault).ToArray());

    private static readonly Lazy<IReadOnlyList<StatusBitDefinition>> RecommendedFunctionsLazy =
        new(() => All.Where(item => item.IsRecommendedFunction).ToArray());

    public static IReadOnlyList<StatusBitDefinition> ObjectStatuses => ObjectStatusesLazy.Value;

    public static IReadOnlyList<StatusBitDefinition> ModelConditions => ModelConditionsLazy.Value;

    public static IReadOnlyList<StatusBitDefinition> All => AllLazy.Value;

    public static IReadOnlyList<StatusBitDefinition> DefaultVisible => DefaultVisibleLazy.Value;

    public static IReadOnlyList<StatusBitDefinition> RecommendedFunctions => RecommendedFunctionsLazy.Value;

    private static IReadOnlyList<StatusBitDefinition> Create(StatusBitDomain domain, IReadOnlyList<string> names)
    {
        var referenceNotes = StatusBitReferenceCatalog.GetNotes(domain);
        var result = new StatusBitDefinition[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            referenceNotes.TryGetValue(name, out var referenceNote);
            var category = referenceNote?.Category ?? Classify(name);
            var risk = GetRiskLevel(name);
            var hiddenByDefault = risk == StatusBitRiskLevel.Dangerous ||
                referenceNote?.IsHiddenByDefault == true;
            var recommended = RecommendedFunctionKeys.Contains(new StatusBitKey(domain, name));
            result[i] = new StatusBitDefinition(
                domain,
                (uint)i,
                name,
                category,
                risk,
                hiddenByDefault,
                recommended,
                CreateHelpText(domain, (uint)i, name, category, risk, referenceNote));
        }

        return result;
    }

    private static string CreateHelpText(
        StatusBitDomain domain,
        uint bitIndex,
        string name,
        string category,
        StatusBitRiskLevel risk,
        StatusBitReferenceNote? referenceNote)
    {
        var domainText = domain == StatusBitDomain.ObjectStatus
            ? "ObjectStatus"
            : "ModelConditionFlags";
        var riskText = risk switch
        {
            StatusBitRiskLevel.Dangerous => "危险状态，可能导致单位消失、死亡、不可选或进入特殊终止态。",
            StatusBitRiskLevel.Volatile => "易被引擎更新逻辑覆盖；本面板只做一次性写入。",
            _ => "一次性写入当前选中单位；本面板不读取真实当前状态。"
        };
        var referenceText = referenceNote is null
            ? ""
            : $"参考：{referenceNote.CreateSummary()}";
        var hiddenText = referenceNote?.IsHiddenByDefault == true
            ? "参考资料标注为未知、无实际作用、RA3 遗留项或纯瞬态表现，默认隐藏。"
            : "";
        return $"{domainText} bit {bitIndex}: {name}；分类：{category}。{referenceText}{hiddenText}{riskText}";
    }

    private static string Classify(string name)
    {
        if (name.Contains("WEAPON", StringComparison.Ordinal) || name.Contains("FIRING", StringComparison.Ordinal) || name.Contains("ATTACK", StringComparison.Ordinal))
        {
            return "武器/攻击";
        }

        if (name.Contains("ARMOR", StringComparison.Ordinal) || name.Contains("INVULNERABLE", StringComparison.Ordinal) || name.Contains("IRON", StringComparison.Ordinal) || name.Contains("SHIELD", StringComparison.Ordinal))
        {
            return "防护";
        }

        if (name.Contains("STEALTH", StringComparison.Ordinal) || name.Contains("INVISIBLE", StringComparison.Ordinal) || name.Contains("HIDDEN", StringComparison.Ordinal) || name.Contains("DETECTED", StringComparison.Ordinal) || name.Contains("DISGUISED", StringComparison.Ordinal))
        {
            return "隐形/探测";
        }

        if (name.Contains("MOV", StringComparison.Ordinal) || name.Contains("LOCOMOTOR", StringComparison.Ordinal) || name.Contains("FLYING", StringComparison.Ordinal) || name.Contains("LANDING", StringComparison.Ordinal) || name.Contains("DOCKING", StringComparison.Ordinal))
        {
            return "移动/姿态";
        }

        if (name.Contains("POWER", StringComparison.Ordinal) || name.Contains("SPECIAL", StringComparison.Ordinal) || name.Contains("UPGRADE", StringComparison.Ordinal))
        {
            return "能力/升级";
        }

        if (name.Contains("HEALTH", StringComparison.Ordinal) || name.Contains("DAMAGED", StringComparison.Ordinal) || name.Contains("DEATH", StringComparison.Ordinal) || name.Contains("RUBBLE", StringComparison.Ordinal) || name.Contains("DESTROYED", StringComparison.Ordinal))
        {
            return "生命/死亡";
        }

        if (name.Contains("CAPTURE", StringComparison.Ordinal) || name.Contains("GARRISON", StringComparison.Ordinal) || name.Contains("CONTAIN", StringComparison.Ordinal) || name.Contains("PASSENGER", StringComparison.Ordinal))
        {
            return "驻扎/占领";
        }

        if (name.Contains("EMP", StringComparison.Ordinal) || name.Contains("FROZEN", StringComparison.Ordinal) || name.Contains("PARALYZED", StringComparison.Ordinal) || name.Contains("STUNNED", StringComparison.Ordinal) || name.Contains("SCRAMBLED", StringComparison.Ordinal))
        {
            return "控制效果";
        }

        return "通用";
    }

    private static StatusBitRiskLevel GetRiskLevel(string name)
    {
        if (DangerousExactNames.Contains(name) ||
            name.StartsWith("DEATH_", StringComparison.Ordinal) ||
            name.StartsWith("DYING", StringComparison.Ordinal) ||
            name.Contains("RUBBLE", StringComparison.Ordinal) ||
            name.Contains("COLLAPS", StringComparison.Ordinal))
        {
            return StatusBitRiskLevel.Dangerous;
        }

        if (VolatileExactNames.Contains(name) ||
            name.Contains("FIRING", StringComparison.Ordinal) ||
            name.Contains("RELOADING", StringComparison.Ordinal) ||
            name.Contains("PREATTACK", StringComparison.Ordinal) ||
            name.Contains("MOVING", StringComparison.Ordinal) ||
            name.Contains("DOOR_", StringComparison.Ordinal))
        {
            return StatusBitRiskLevel.Volatile;
        }

        return StatusBitRiskLevel.Normal;
    }

    private static readonly HashSet<string> DangerousExactNames = new(StringComparer.Ordinal)
    {
        "ALL",
        "INVALID",
        "DESTROYED",
        "DESTROYED_WHILST_BEING_CONSTRUCTED",
        "SOLD",
        "NOT_IN_WORLD",
        "UNSELECTABLE",
        "RUBBLE",
        "POST_RUBBLE",
        "POST_COLLAPSE",
        "COLLAPSING",
        "TOPPLED",
        "FRONTCRUSHED",
        "BACKCRUSHED",
        "HEALTH_PERCENT_0",
        "SINKING"
    };

    private static readonly HashSet<string> VolatileExactNames = new(StringComparer.Ordinal)
    {
        "STEALTHED",
        "DETECTED",
        "INVISIBLE_STEALTH",
        "INVISIBLE_CAMOUFLAGE",
        "ATTACKING",
        "IS_ATTACKING",
        "IS_FIRING_WEAPON",
        "IS_RELOADING_WEAPON",
        "USING_ABILITY",
        "USING_SPECIAL_ABILITY",
        "SELECTED",
        "MOVING",
        "WALKING",
        "LANDING",
        "TAKING_OFF"
    };

    private readonly record struct StatusBitKey(StatusBitDomain Domain, string Name);

    private static readonly HashSet<StatusBitKey> RecommendedFunctionKeys =
    [
        new(StatusBitDomain.ObjectStatus, "STEALTHED"),
        new(StatusBitDomain.ObjectStatus, "CAN_ATTACK_WHILE_STEALTHED"),
        new(StatusBitDomain.ModelConditionFlags, "INVISIBLE_STEALTH"),
        new(StatusBitDomain.ObjectStatus, "NON_AUTOACQUIRABLE"),
        new(StatusBitDomain.ObjectStatus, "SKIRMISH_AI_DO_NOT_ATTACK"),
        new(StatusBitDomain.ObjectStatus, "NO_ATTACK_FROM_AI"),
        new(StatusBitDomain.ObjectStatus, "IMMOBILE"),
        new(StatusBitDomain.ObjectStatus, "IMMOBILE_ALLOW_ROTATE"),
        new(StatusBitDomain.ObjectStatus, "NO_SPECIAL_ABILITY"),
        new(StatusBitDomain.ObjectStatus, "SCRAMBLED"),
        new(StatusBitDomain.ObjectStatus, "IGNORING_POWER_DOWN"),
        new(StatusBitDomain.ObjectStatus, "UNDER_IRON_CURTAIN"),
        new(StatusBitDomain.ModelConditionFlags, "WEAPONSET_VETERAN"),
        new(StatusBitDomain.ModelConditionFlags, "WEAPONSET_ELITE"),
        new(StatusBitDomain.ModelConditionFlags, "WEAPONSET_HERO"),
        new(StatusBitDomain.ModelConditionFlags, "ARMORSET_VETERAN"),
        new(StatusBitDomain.ModelConditionFlags, "ARMORSET_ELITE"),
        new(StatusBitDomain.ModelConditionFlags, "ARMORSET_HERO"),
        new(StatusBitDomain.ModelConditionFlags, "PARALYZED"),
        new(StatusBitDomain.ModelConditionFlags, "AFFECTED_BY_EMP"),
        new(StatusBitDomain.ObjectStatus, "NO_BRIBE"),
        new(StatusBitDomain.ObjectStatus, "IMMUNE_TO_BARK"),
        new(StatusBitDomain.ObjectStatus, "UNATTACKABLE"),
        new(StatusBitDomain.ObjectStatus, "DEFLECT_INCOMING_FIRE"),
        new(StatusBitDomain.ObjectStatus, "REPAIR_ALLIES_WHEN_IDLE")
    ];

    private sealed record StatusBitReferenceNote(
        string Category,
        string Condition,
        string Description,
        bool IsHiddenByDefault)
    {
        public string CreateSummary()
        {
            var parts = new List<string>(capacity: 2);
            if (HasText(Condition))
            {
                parts.Add($"获取条件：{Condition}。");
            }

            if (HasText(Description))
            {
                parts.Add($"效果/备注：{Description}。");
            }

            return parts.Count == 0 ? "参考资料未给出明确作用。" : string.Concat(parts);
        }
    }

    private static class StatusBitReferenceCatalog
    {
        private const string ModelStateResourceName = "RayaTrainer.Core.Reference.ModelState.md";
        private const string ObjectStatusResourceName = "RayaTrainer.Core.Reference.ObjectStatus.md";

        private static readonly Lazy<IReadOnlyDictionary<string, StatusBitReferenceNote>> ObjectStatusNotesLazy =
            new(() => LoadFromAssetPacks("ObjectStatusNotes") ?? Load(ObjectStatusResourceName));

        private static readonly Lazy<IReadOnlyDictionary<string, StatusBitReferenceNote>> ModelConditionNotesLazy =
            new(() => LoadFromAssetPacks("ModelStateNotes") ?? Load(ModelStateResourceName));

        public static IReadOnlyDictionary<string, StatusBitReferenceNote> GetNotes(StatusBitDomain domain)
        {
            return domain == StatusBitDomain.ObjectStatus
                ? ObjectStatusNotesLazy.Value
                : ModelConditionNotesLazy.Value;
        }

        private static IReadOnlyDictionary<string, StatusBitReferenceNote>? LoadFromAssetPacks(string kind)
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Assets", "Catalogs");
            foreach (var packDir in AssetPackLoader.EnumeratePackDirs(root))
            {
                AssetPackManifest manifest;
                try { manifest = AssetPackLoader.LoadManifest(packDir); }
                catch (AssetPackException) { continue; }

                foreach (var entry in manifest.Assets.Where(a => a.Kind == kind))
                {
                    using var s = AssetPackLoader.OpenAsset(packDir, entry);
                    using var sr = new StreamReader(s);
                    var json = sr.ReadToEnd();
                    var notes = JsonSerializer.Deserialize<Dictionary<string, StatusBitReferenceNote>>(json);
                    if (notes is not null)
                        return notes;
                }
            }
            return null;
        }

        private static IReadOnlyDictionary<string, StatusBitReferenceNote> Load(string resourceName)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return new Dictionary<string, StatusBitReferenceNote>(StringComparer.Ordinal);
            }

            using var reader = new StreamReader(stream);

            var notes = new Dictionary<string, StatusBitReferenceNote>(StringComparer.Ordinal);
            var category = "通用";
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    category = line[3..].Trim();
                    continue;
                }

                if (!line.StartsWith('|') || line.Contains("---", StringComparison.Ordinal))
                {
                    continue;
                }

                var columns = line
                    .Split('|', StringSplitOptions.None)
                    .Skip(1)
                    .SkipLast(1)
                    .Select(column => column.Trim())
                    .ToArray();
                if (columns.Length < 3 ||
                    columns[0].Equals("ObjectStatus", StringComparison.OrdinalIgnoreCase) ||
                    columns[0].Equals("Enum Value", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = columns[0].Trim('`');
                var condition = NormalizeReferenceText(columns[1]);
                var description = NormalizeReferenceText(columns[2]);
                notes[name] = new StatusBitReferenceNote(
                    category,
                    condition,
                    description,
                    ShouldHideByDefault(condition, description));
            }

            return notes;
        }
    }

    private static bool ShouldHideByDefault(string condition, string description)
    {
        if (!HasText(condition) && !HasText(description))
        {
            return true;
        }

        var text = $"{condition} {description}";
        return text.Contains("无实际作用", StringComparison.Ordinal) ||
            text.Contains("与ra3无关", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ra3无关", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("疑似失效", StringComparison.Ordinal) ||
            text.Contains("未使用", StringComparison.Ordinal) ||
            text.Contains("作用不明", StringComparison.Ordinal) ||
            text.Contains("未知", StringComparison.Ordinal);
    }

    private static string NormalizeReferenceText(string value)
    {
        var text = value
            .Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&nbsp;", " ", StringComparison.Ordinal)
            .Trim();
        return HasText(text) ? text : "";
    }

    private static bool HasText(string value)
    {
        var text = value.Trim();
        return text.Length > 0 &&
            !text.Equals("—", StringComparison.Ordinal) &&
            !text.Equals("-", StringComparison.Ordinal);
    }

    private static readonly string[] ObjectStatusNames =
    [
        "DESTROYED",
        "CAN_ATTACK",
        "UNDER_CONSTRUCTION",
        "UNSELECTABLE",
        "NO_COLLISIONS",
        "NO_ATTACK",
        "NO_SPECIAL_ABILITY",
        "AIRBORNE_TARGET",
        "PARACHUTING",
        "REPULSOR",
        "HIJACKED",
        "AFLAME",
        "BURNED",
        "CANNOT_BE_SOLD",
        "IS_FIRING_WEAPON",
        "IS_RELOADING_WEAPON",
        "IS_BRAKING",
        "STEALTHED",
        "HIDDEN",
        "DETECTED",
        "CAN_STEALTH",
        "SOLD",
        "UNDERGOING_REPAIR",
        "RECONSTRUCTING",
        "IS_ATTACKING",
        "NO_AUTO_ACQUIRE",
        "USING_ABILITY",
        "IS_AIMING_WEAPON",
        "NO_ATTACK_FROM_AI",
        "IGNORING_STEALTH",
        "IS_MELEE_ATTACKING",
        "FOLLOWING_THROUGH",
        "LEASHED_RETURNING",
        "DEATH_1",
        "DEATH_2",
        "DEATH_3",
        "DEATH_4",
        "DEATH_5",
        "CONTESTED",
        "CONTESTING_BUILDING",
        "HORDE_MEMBER",
        "TRANSPORT_MOVING_TO_CONTAIN",
        "RIDER_IS_PILOT",
        "DAMAGED",
        "REALLYDAMAGED",
        "RUBBLE",
        "IRRADIATED",
        "NO_SHADOW",
        "IN_STASIS",
        "OUT_OF_PHASE",
        "NEXT_MOVE_IS_REVERSE",
        "IMMOBILE",
        "IMMOBILE_ALLOW_ROTATE",
        "PAUSING",
        "FLEE_OFF_MAP",
        "NOT_IN_WORLD",
        "INAUDIBLE",
        "CHANTING",
        "ENRAGED",
        "SINKING",
        "RAMPAGING",
        "INSIDE_GARRISON",
        "DEPLOYED",
        "UNATTACKABLE",
        "ENCLOSED",
        "TEMPORARILY_DEFECTED",
        "TAGGED",
        "DEPLOYING",
        "TOPPLING",
        "PORTER_TAGGED",
        "NO_SQUISHCOLLIDE_DELAY",
        "STAND_GROUND",
        "UNCONTROLLABLY_SCARED",
        "SPECIAL_ABILITY_PACKING_UNPACKING_OR_USING",
        "TIBERIUM_VIBRATING",
        "UPDATING_AI",
        "CLONED",
        "IGNORE_AI_COMMAND",
        "RUNNING_DOWN_FROM_BEHIND",
        "DO_NOT_SCORE",
        "CAN_NOT_WALK_ON",
        "MARCH_OF_DEATH",
        "DO_NOT_PICK_ME",
        "INHERITED_FROM_ALLY_TEAM",
        "SWITCHED_WEAPONS",
        "END_FIRE_STATE",
        "BOOKENDING",
        "GENERIC_TOGGLE_STATE",
        "CAN_CONTEST_GARRISON",
        "BUILD_BEING_CANCELED",
        "PENDING_CONSTRUCTION",
        "PHANTOM_STRUCTURE",
        "IN_FORMATION_TEMPLATE",
        "IS_LEAVING_FACTORY",
        "MOVING_TO_DISMOUNT",
        "NO_HERO_PROPERTIES",
        "CAN_ENTER_ANYTHING",
        "ENTERING_GOAL",
        "CHARGING_BASE_DEFENSES",
        "INVISIBLE_DETECTED_BY_FRIEND",
        "INVISIBLE_DETECTED",
        "SHRUNK",
        "ATTACHED",
        "WONT_RIDE_WITH_YOU",
        "COMMAND_BUTTON_TOGGLED",
        "OCLMONITOR_COMPLETED_TASK",
        "OCLMONITOR_MONITOR_RELEASED",
        "USER_POWERED_DOWN",
        "GARRISONED",
        "BOOBY_TRAPPED",
        "IS_HIDEOUT",
        "EXECUTING_STORED_COMMAND",
        "UNIT_WANTS_TO_REGARRISON",
        "IN_COVER",
        "DONT_CLEAR_FOR_BUILD",
        "MATCH_TARGETS_SPEED",
        "HAS_TIBERIUM_GROWTH_MOD",
        "HAS_TIBERIUM_UPGRADE",
        "STEAL_NEXT_UNIT_TRAPPED",
        "DOES_CONTAIN_TIBERIUM",
        "IS_BEING_HARVESTED",
        "NO_REFUND",
        "SHIELDBODY_ENABLED",
        "HEALTH_PERCENT_0",
        "HEALTH_PERCENT_25",
        "HEALTH_PERCENT_50",
        "HEALTH_PERCENT_75",
        "HEALTH_PERCENT_100",
        "WEAPON_UPGRADED_01",
        "WEAPON_UPGRADED_02",
        "WEAPON_UPGRADED_03",
        "COMBINED_PARENT",
        "COMBINED_CHILD",
        "COMBINED_ATTACHED",
        "LOADED_FROM_MAP",
        "ATTACKING_GARRISONED_STRUCTURE",
        "EXITING_COMBINED",
        "DELAYED_ENTER_STRUCTURE",
        "DOCKING",
        "HARVESTING",
        "BRIDGE_IMPASSABLE",
        "POWERED_DOWN_EMP",
        "FORCE_ATTACKING",
        "FORCE_ATTACK_MOVING",
        "SCARED_CIVILIAN_CAR",
        "USER_PARALYZED",
        "BOOBY_TRAP_EXPLODE",
        "IS_ENGAGED",
        "CARRYING_FLAG",
        "IS_MOVING_TO_RALLY_POINT",
        "SPECIAL_ARMOR_ACTIVE",
        "AIRCRAFT_IGNORE_SAMEPLAYER_HANGAR_RULE",
        "NEXT_MOVE_IS_FORCE_ATTACK_MOVE",
        "SPECIALABILITY_ACTIVE",
        "CAN_STEALTH_FROM_PRODUCER",
        "GARRISON_CAN_STEALTH",
        "CAN_ATTACK_WHILE_STEALTHED",
        "RESET_GOAL_POSITION",
        "RIFT_OCCUPIED",
        "WATER_LOCOMOTOR_ACTIVE",
        "GROUND_LOCOMOTOR_ACTIVE",
        "AIR_LOCOMOTOR_ACTIVE",
        "UNPACKING",
        "OVER_WATER",
        "CONTAINER_OCCUPIED",
        "PLACED_BY_PLAYER",
        "DEFLECT_INCOMING_FIRE",
        "DEPART_FROM_BOARDING",
        "WAITING_FOR_BOARDING",
        "HAS_SECONDARY_DAMAGE",
        "IS_BEING_DRAGGED",
        "CLEARED_FOR_LANDING",
        "VEHICLE_ATTACHED",
        "PARKED_AT_AIRFIELD",
        "IN_SHIELD_SPHERE",
        "HIT_SHIELD_SPHERE",
        "NON_AUTOACQUIRABLE",
        "CHARGING_WEAPON",
        "OVERCHARGING_WEAPON",
        "ENABLE_TRACER_DRAW",
        "PREEMPT_PRE_ATTACK",
        "LINE_SEGMENT_DAMAGE_ACTIVE",
        "ADVANCED_MISSILE_PACKS",
        "LEECHED_TARGET_ACTIVE",
        "PLAYER_POWER_1",
        "PLAYER_POWER_2",
        "PLAYER_POWER_3",
        "PLAYER_POWER_4",
        "PLAYER_POWER_5",
        "GRAPPLE_UPDATE_ACTIVE",
        "SHROUD_REVEAL_TO_ALL",
        "SURFACED",
        "SUBMERGED",
        "SKIRMISH_AI_DO_NOT_ATTACK",
        "SKIRMISH_AI_DO_NOT_TAKE",
        "TRANSFORMATION_TOGGLE_STATE",
        "REPAIR_ALLIES_WHEN_IDLE",
        "DO_NOT_AVOID_TALLBUILDING",
        "ABSORBED_DAMAGE",
        "NO_BRIBE",
        "IGNORING_POWER_DOWN",
        "EXCEEDED_PATH_NEIGHBOR_COUNT",
        "CANNOT_LAND_AT_AIRFIELD",
        "WALL_SEGMENT",
        "IMMUNE_TO_BARK",
        "UNDER_RUSH_ATTACK",
        "UNDER_FROZEN",
        "LANDING_IN_PROGRESS",
        "UNDER_IRON_CURTAIN",
        "EXPLOSIVES_ATTACHED",
        "EXPLOSIVES_DETONATING",
        "LIFTED_INTO_AIR",
        "IN_NANOHIVE",
        "SURFACED_IMMOBILE",
        "BROADCASTING_INVISIBILITY",
        "SCRAMBLED",
        "MAGNETIZED",
        "RIOT_SHIELDED",
        "POINT_DEFENSE_DRONE_ATTACHED",
        "GRAPPLE_UPDATE_BEING_MOVED",
        "TARGETED_FOR_REPAIR",
        "IN_SPIDER_HOLE",
        "ANTI_GARRISON"
    ];

    private static readonly string[] ModelConditionNames =
    [
        "INVALID",
        "TOPPLED",
        "FRONTCRUSHED",
        "BACKCRUSHED",
        "DAMAGED",
        "REALLYDAMAGED",
        "RUBBLE",
        "POST_RUBBLE",
        "POST_COLLAPSE",
        "DECAY",
        "DESTROYED_WHILST_BEING_CONSTRUCTED",
        "COLLAPSING",
        "SOLD",
        "STRUCTURE_UNPACKING",
        "SPECIAL_DAMAGED",
        "NIGHT",
        "SNOW",
        "FREEFALL",
        "PARACHUTING",
        "PARACHUTE_LAND",
        "LAUNCHED",
        "GARRISONED",
        "INSIDE_GARRISON",
        "WEAPONSET_VETERAN",
        "WEAPONSET_ELITE",
        "WEAPONSET_HERO",
        "WEAPONSET_PASSENGER_TYPE_ONE",
        "WEAPONSET_PASSENGER_TYPE_TWO",
        "WEAPONSET_PLAYER_UPGRADE",
        "WEAPONSET_TOGGLE_1",
        "WEAPONSET_TOGGLE_2",
        "WEAPONSET_TOGGLE_3",
        "WEAPONSET_SPECIAL_UPGRADE",
        "WEAPONSET_GARRISONED",
        "WEAPONSET_CLOSE_RANGE",
        "WEAPONSET_CONTESTING_BUILDING",
        "WEAPONSET_RIDER1",
        "WEAPONSET_RIDER2",
        "WEAPONSET_RIDER3",
        "WEAPONSET_RIDER4",
        "WEAPONSET_SPECIAL_ONE",
        "WEAPONSET_SPECIAL_TWO",
        "WEAPONSET_CONTAINED",
        "WEAPONSET_MOUNTED",
        "SWAPPING_TO_WEAPONSET_1",
        "SWAPPING_TO_WEAPONSET_2",
        "SWAPPING_TO_WEAPONSET_3",
        "WEAPONSTATE_ONE",
        "WEAPONSTATE_TWO",
        "WEAPONSTATE_THREE",
        "WEAPONSTATE_CLOSE_RANGE",
        "WEAPONSTATE_CONTAINED",
        "WEAPONSLOTID_01",
        "WEAPONSLOTID_02",
        "WEAPONSLOTID_03",
        "WEAPONSLOTID_04",
        "WEAPONSLOTID_05",
        "SPECIAL_WEAPON_ONE",
        "SPECIAL_WEAPON_TWO",
        "SPECIAL_WEAPON_THREE",
        "SPECIAL_WEAPON_FOUR",
        "SPECIAL_WEAPON_FIVE",
        "SPECIAL_WEAPON_SIX",
        "DESTROYED_WEAPON",
        "WEAPON_TOGGLING",
        "ATTACKING",
        "ATTACKING_STRUCTURE",
        "ATTACKING_POSITION",
        "ENGAGED",
        "PREATTACK_A",
        "PREATTACK_B",
        "PREATTACK_C",
        "PREATTACK_D",
        "PREATTACK_E",
        "FIRING_OR_PREATTACK_A",
        "FIRING_OR_PREATTACK_B",
        "FIRING_OR_PREATTACK_C",
        "FIRING_OR_PREATTACK_D",
        "FIRING_OR_PREATTACK_E",
        "FIRING_A",
        "FIRING_B",
        "FIRING_C",
        "FIRING_D",
        "FIRING_E",
        "FIRING_OR_RELOADING_A",
        "FIRING_OR_RELOADING_B",
        "FIRING_OR_RELOADING_C",
        "FIRING_OR_RELOADING_D",
        "FIRING_OR_RELOADING_E",
        "RELOADING_A",
        "RELOADING_B",
        "RELOADING_C",
        "RELOADING_D",
        "RELOADING_E",
        "BETWEEN_FIRING_SHOTS_A",
        "BETWEEN_FIRING_SHOTS_B",
        "BETWEEN_FIRING_SHOTS_C",
        "BETWEEN_FIRING_SHOTS_D",
        "BETWEEN_FIRING_SHOTS_E",
        "USING_WEAPON_A",
        "USING_WEAPON_B",
        "USING_WEAPON_C",
        "USING_WEAPON_D",
        "USING_WEAPON_E",
        "DOOR_1_OPENING",
        "DOOR_1_CLOSING",
        "DOOR_1_WAITING_OPEN",
        "DOOR_1_WAITING_TO_CLOSE",
        "DOOR_2_OPENING",
        "DOOR_2_CLOSING",
        "DOOR_2_WAITING_OPEN",
        "DOOR_2_WAITING_TO_CLOSE",
        "DOOR_3_OPENING",
        "DOOR_3_CLOSING",
        "DOOR_3_WAITING_OPEN",
        "DOOR_3_WAITING_TO_CLOSE",
        "DOOR_4_OPENING",
        "DOOR_4_CLOSING",
        "DOOR_4_WAITING_OPEN",
        "DOOR_4_WAITING_TO_CLOSE",
        "DOOR_HELIPAD_OPENING",
        "DOOR_HELIPAD_CLOSING",
        "DOOR_HELIPAD_WAITING_OPEN",
        "DOOR_HELIPAD_WAITING_TO_CLOSE",
        "PARKINGPLACE_1_DOOR_OPENING",
        "PARKINGPLACE_1_DOOR_OPEN",
        "PARKINGPLACE_1_DOOR_CLOSING",
        "PARKINGPLACE_1_DOOR_CLOSED",
        "PARKINGPLACE_2_DOOR_OPENING",
        "PARKINGPLACE_2_DOOR_OPEN",
        "PARKINGPLACE_2_DOOR_CLOSING",
        "PARKINGPLACE_2_DOOR_CLOSED",
        "PARKINGPLACE_3_DOOR_OPENING",
        "PARKINGPLACE_3_DOOR_OPEN",
        "PARKINGPLACE_3_DOOR_CLOSING",
        "PARKINGPLACE_3_DOOR_CLOSED",
        "PARKINGPLACE_4_DOOR_OPENING",
        "PARKINGPLACE_4_DOOR_OPEN",
        "PARKINGPLACE_4_DOOR_CLOSING",
        "PARKINGPLACE_4_DOOR_CLOSED",
        "PARKINGPLACE_HELIPAD_DOOR_OPENING",
        "PARKINGPLACE_HELIPAD_DOOR_OPEN",
        "PARKINGPLACE_HELIPAD_DOOR_CLOSING",
        "PARKINGPLACE_HELIPAD_DOOR_CLOSED",
        "MOVING",
        "DYING",
        "DYING_WASMOVING",
        "DYING_WASATTACKING",
        "WANDER",
        "WALKING",
        "CHARGING",
        "MOVING_OUT_OF_THE_WAY",
        "RUNNING_OFF_MAP",
        "COMING_OUT_OF_FACTORY",
        "ATTACK_MOVING",
        "DIVING",
        "SWOOPING",
        "MARCHING",
        "BACKING_UP",
        "CLIMBING",
        "RAPPELLING",
        "WADING",
        "SWIMMING",
        "SCALING_WALL",
        "SCALING_WALL_HORDE",
        "FLYING",
        "TAKING_OFF",
        "LANDING",
        "AWAITING_CONSTRUCTION",
        "PARTIALLY_CONSTRUCTED",
        "ACTIVELY_BEING_CONSTRUCTED",
        "UNIT_ACTIVELY_BEING_CONSTRUCTED",
        "ACTIVELY_CONSTRUCTING",
        "RADAR_EXTENDING",
        "RADAR_UPGRADED",
        "PANICKING",
        "AFLAME",
        "SMOLDERING",
        "BURNED",
        "BURNT_MODEL",
        "BURNT_TEXTURE",
        "BURNINGDEATH",
        "DOCKING",
        "DOCKING_BEGINNING",
        "DOCKING_ACTIVE",
        "DOCKING_RETURN",
        "DOCKING_MOVEBACK",
        "DOCKING_FILL",
        "DOCKING_EXTRACT",
        "DOCKING_ENDING",
        "DOCKING_PRE_DOCK",
        "HARVEST_PREPARATION",
        "HARVEST_ACTION",
        "CARRYING",
        "PASSENGER",
        "TRANSPORT_MOVING",
        "TRANSPORT_STOPPED",
        "SIEGE_CONTAIN",
        "JETAFTERBURNER",
        "JETEXHAUST",
        "PACKING",
        "PREPARING",
        "UNPACKING",
        "PACKING_TYPE_1",
        "PACKING_TYPE_2",
        "PACKING_TYPE_3",
        "PACKING_TYPE_4",
        "PACKING_TYPE_5",
        "PACKING_TYPE_6",
        "OVER_WATER",
        "POWER_PLANT_UPGRADED",
        "POWER_PLANT_UPGRADING",
        "BUILD_PLACEMENT_CURSOR",
        "PHANTOM_STRUCTURE",
        "FORMATION_PREVIEW",
        "WORLD_BUILDER",
        "DEBUG",
        "START_CAPTURE",
        "CANCEL_CAPTURE",
        "CAPTURING",
        "CAPTURED",
        "RAISING_FLAG",
        "CONTINUOUS_FIRE_SLOW",
        "CONTINUOUS_FIRE_MEAN",
        "CONTINUOUS_FIRE_FAST",
        "PREORDER",
        "SPECIAL_CHEERING",
        "IMPENDING_DOOM",
        "EATING",
        "STUNNED_FLAILING",
        "STUNNED",
        "STUNNED_STANDING_UP",
        "SPLATTED",
        "THROWN_PROJECTILE",
        "ABOUT_TO_HIT",
        "EXPLODED_FLAILING",
        "EXPLODED_BOUNCING",
        "ACCELERATE",
        "DECELERATE",
        "TURN_LEFT",
        "TURN_RIGHT",
        "TURN_LEFT_HIGH_SPEED",
        "TURN_RIGHT_HIGH_SPEED",
        "DESTROYED_FRONT",
        "DESTROYED_RIGHT",
        "DESTROYED_BACK",
        "DESTROYED_LEFT",
        "WEAPONLOCK_PRIMARY",
        "WEAPONLOCK_SECONDARY",
        "WEAPONLOCK_TERTIARY",
        "WEAPONLOCK_QUATERNARY",
        "WEAPONLOCK_QUINARY",
        "DEATH_1",
        "DEATH_2",
        "DEATH_3",
        "DEATH_4",
        "DEATH_5",
        "SELECTED",
        "GUARDING",
        "LEVELED",
        "MOUNTED",
        "RESURRECTED",
        "ATTACHED",
        "DRAFTED",
        "FLOODED",
        "LOADED",
        "DEPLOYED",
        "JUST_BUILT",
        "BASE_BUILD",
        "REACT_1",
        "REACT_2",
        "REACT_3",
        "REACT_4",
        "REACT_5",
        "REACT_6",
        "HIT_REACTION",
        "HIT_LEVEL_1",
        "HIT_LEVEL_2",
        "HIT_LEVEL_3",
        "AIM_HIGH",
        "AIM_STRAIGHT",
        "AIM_LOW",
        "AIM_NEAR",
        "AIM_FAR",
        "USER_1",
        "USER_2",
        "USER_3",
        "USER_4",
        "USER_5",
        "USER_6",
        "USER_7",
        "USER_8",
        "USER_9",
        "USER_10",
        "PASSENGER_VARIATION_1",
        "PASSENGER_VARIATION_2",
        "PASSENGER_VARIATION_3",
        "PASSENGER_VARIATION_4",
        "PASSENGER_VARIATION_5",
        "SPECIAL_POWER_1",
        "SPECIAL_POWER_2",
        "SPECIAL_POWER_3",
        "SPECIALPOWER1_READY",
        "SPECIALPOWER2_READY",
        "SPECIALPOWER3_READY",
        "SPECIALPOWER4_READY",
        "SPECIALPOWER5_READY",
        "SPECIALPOWER6_READY",
        "SPECIALPOWER7_READY",
        "SPECIALPOWER8_READY",
        "SPECIALPOWER9_READY",
        "USING_SPECIAL_ABILITY",
        "DEFLECT_SPECIAL_POWER",
        "RIDER1",
        "RIDER2",
        "RIDER3",
        "RIDER4",
        "RIDER5",
        "RIDER6",
        "RIDER7",
        "RIDER8",
        "RIDERLESS",
        "HORDE_EMPTY",
        "PRIMARY_FORMATION",
        "ALTERNATE_FORMATION",
        "CAPTURE_100",
        "CAPTURE_75",
        "CAPTURE_50",
        "CAPTURE_25",
        "CAPTURE_0",
        "DISGUISED",
        "HIDDEN",
        "INVISIBLE_STEALTH",
        "INVISIBLE_CAMOUFLAGE",
        "PRONE",
        "COVER",
        "SUPPRESSED",
        "TURRET_ANGLE_0",
        "TURRET_ANGLE_90",
        "TURRET_ANGLE_180",
        "TURRET_ANGLE_270",
        "TURRET_ROTATE",
        "ARMORSET_VETERAN",
        "ARMORSET_ELITE",
        "ARMORSET_HERO",
        "ARMORSET_WEAK_VERSUS_BASEDEFENSES",
        "ARMORSET_ALTERNATE_FORMATION",
        "ARMORSET_MOUNTED",
        "ARMORSET_PLAYER_UPGRADE",
        "ARMORSET_PLAYER_UPGRADE_2",
        "ARMORSET_PLAYER_UPGRADE_3",
        "ARMORSET_UNBESIEGEABLE",
        "UPGRADED_ARMOR",
        "EMOTION_TAUNTING",
        "EMOTION_DOOM",
        "EMOTION_POINTING",
        "EMOTION_GUNG_HO",
        "EMOTION_LOOK_TO_SKY",
        "EMOTION_CELEBRATING",
        "EMOTION_AMUSED",
        "EMOTION_MORALE_HIGH",
        "EMOTION_MORALE_LOW",
        "EMOTION_COWER",
        "EMOTION_DISSIDENT",
        "EMOTION_UNCONTROLLABLY_AFRAID",
        "EMOTION_BRACE_FOR_BEING_CRUSHED",
        "EMOTION_CHEER_FOR_ABOUT_TO_CRUSH",
        "EMOTION_ALERT",
        "EMOTION_AFRAID",
        "EMOTION_TERROR",
        "EMOTION_PANIC",
        "INVULNERABLE",
        "SPECIAL_ENEMY_NEAR",
        "UNCONTROLLABLE",
        "LEASHED_RETURNING",
        "SAIL_FLAPPING",
        "SAIL_BLOWN_RIGHT",
        "SAIL_BLOWN_LEFT",
        "BUILD_VARIATION_ONE",
        "BUILD_VARIATION_TWO",
        "PARALYZED",
        "AFFECTED_BY_EMP",
        "UNDERPOWERED",
        "TIBERIUM_CRYSTAL_TYPE1",
        "TIBERIUM_CRYSTAL_TYPE2",
        "TIBERIUM_CRYSTAL_TYPE3",
        "TIBERIUM_CRYSTAL_TYPE4",
        "TIBERIUM_CRYSTAL_TYPE5",
        "TIBERIUM_GROWING",
        "MARKED_FOR_HUNT_TACTIC",
        "MARKED_FOR_NO_SKIRMISH_RECRUIT",
        "LOCOMOTOR_NORMAL_UPGRADED",
        "LOCOMOTOR_FREEFALL",
        "LOCOMOTOR_WANDER",
        "LOCOMOTOR_PANIC",
        "LOCOMOTOR_TAXIING",
        "LOCOMOTOR_SUPERSONIC",
        "LOCOMOTOR_MOUNTED",
        "LOCOMOTOR_ENRAGED",
        "LOCOMOTOR_SCARED",
        "LOCOMOTOR_CONTAINED",
        "LOCOMOTOR_COMBO",
        "LOCOMOTOR_COMBO2",
        "LOCOMOTOR_COMBO3",
        "LOCOMOTOR_WALL_SCALING",
        "LOCOMOTOR_CHANGING_FIRINGARC",
        "LOCOMOTOR_BURNINGDEATH",
        "USING_COMBO_LOCOMOTOR",
        "MONEY_STORED_AMOUNT_1",
        "MONEY_STORED_AMOUNT_2",
        "MONEY_STORED_AMOUNT_3",
        "MONEY_STORED_AMOUNT_4",
        "VEHICLE_CRUSH_LEFT",
        "VEHICLE_CRUSH_RIGHT",
        "VEHICLE_CRUSH_CENTER",
        "HEALTH_PERCENT_0",
        "HEALTH_PERCENT_25",
        "HEALTH_PERCENT_50",
        "HEALTH_PERCENT_75",
        "HEALTH_PERCENT_100",
        "COMBINED_PARENT",
        "COMBINED_CHILD",
        "REPAIRING_DISABLED",
        "WALL_SEGMENT",
        "SHRINK_EFFECT",
        "HIGH_TECH_EFFECT",
        "AIR_POWER_EFFECT",
        "PLAYER_POWER_1",
        "BEING_MAULED",
        "FROZEN",
        "IRONCURTAIN",
        "CHRONORIFT",
        "INFILTRATED_DISABLE",
        "INFILTRATED_RADAR_FREEZE",
        "INFILTRATED_STEAL_MONEY",
        "INFILTRATED_VISION",
        "INFILTRATED_ENERGY",
        "INFILTRATED_RESET",
        "INFILTRATED_VISION_UNITS",
        "WEAPONSLOTID_06",
        "WEAPONSLOTID_07",
        "WEAPONSLOTID_08",
        "WEAPONSLOTID_09",
        "WEAPONSLOTID_10",
        "BUILD_PLACEMENT_HINT",
        "DEATH_6",
        "DEATH_7",
        "DEATH_8",
        "DEATH_9",
        "DEATH_10",
        "DEATH_11",
        "DEATH_12",
        "DEATH_13",
        "DEATH_14",
        "DEATH_15",
        "ALL",
        "SUCKED_UP_HIGH"
    ];
}
