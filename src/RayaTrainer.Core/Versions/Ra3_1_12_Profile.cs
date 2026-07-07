using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

internal static partial class Ra3_1_12_Profile
{
    public static Ra3VersionProfile Create()
    {
        return new Ra3VersionProfile
        {
            Id = "ra3_1.12",
            DisplayName = "RA3 1.12",
            ProcessName = GameTarget.ProcessName,
            FileVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                GameTarget.ExpectedVersion
            },
            SupportsSignatureScanning = true,
            Hooks = BuildHooks(),
            RemoteGlobals = BuildRemoteGlobals(),
            EngineFunctions = BuildEngineFunctions(),
            NativeAgentRefs = BuildNativeAgentRefs(),
            OptionalSignatureSymbols = UprisingOnlySignatureSymbols()
        };
    }

    private static IReadOnlySet<string> UprisingOnlySignatureSymbols() =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_BackChallengeModeTime",
            "_BackChallengeModeMoney",
            "Rva_88DFD0"
        };

    private static IReadOnlyDictionary<string, VersionedAddress> BuildHooks()
    {
        return TrainerRuntimeAssets.LoadManifest().PatchManifest.Hooks
            .Where(hook => hook.SupportsProfile("ra3_1.12"))
            .ToDictionary(HookKey, hook => Verified(HookKey(hook), ParseRva(hook.Address), "trainer_report.json"), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, VersionedAddress> BuildRemoteGlobals()
    {
        return new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameClientPointer"] = Verified("GameClientPointer", 0x8D8CE4, "Native profile"),
            ["MouseWorldPointer"] = Verified("MouseWorldPointer", 0x8DAEFC, "bootstrap"),
            ["SelectionManager"] = Verified("SelectionManager", 0x8DB73C, "bootstrap"),
            ["ScienceStore"] = Verified("ScienceStore", 0x8DBBF0, "bootstrap"),
            ["ThingTemplateStore"] = Verified("ThingTemplateStore", 0x8E6C58, "bootstrap"),
            ["PlayerManager"] = Verified("PlayerManager", 0x8E8C9C, "bootstrap"),
            ["SelectedUnitCode"] = Verified("SelectedUnitCode", 0x8E9838, "Native profile")
        };
    }

    private static IReadOnlyDictionary<string, VersionedAddress> BuildEngineFunctions()
    {
        return new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase)
        {
            ["ScienceStore_FindScience"] = Verified("ScienceStore_FindScience", 0x1456F0, "bootstrap"),
            ["ScienceStore_FindUpgrade"] = Verified("ScienceStore_FindUpgrade", 0x147260, "bootstrap"),
            ["MouseWorld_ToMapPosition"] = Verified("MouseWorld_ToMapPosition", 0x1ED4A0, "bootstrap"),
            ["CreateUnit"] = Verified("CreateUnit", 0x205240, "bootstrap/native-agent"),
            ["LevelUpSelected"] = Verified("LevelUpSelected", 0x35C200, "bootstrap/native-agent"),
            ["KillUnit"] = Verified("KillUnit", 0x39EA50, "bootstrap/native-agent"),
            ["Object_SetPosition"] = Verified("Object_SetPosition", 0x3E3D00, "IDA/CreateUnit caller cross-check"),
            ["ThingTemplateStore_Find"] = Verified("ThingTemplateStore_Find", 0x3E4230, "bootstrap/native-agent"),
            ["Player_GetScienceManager"] = Verified("Player_GetScienceManager", 0x4393E0, "bootstrap"),
            ["ScienceManager_FindScience"] = Verified("ScienceManager_FindScience", 0x43C300, "bootstrap"),
            ["Player_GetUpgradeStore"] = Verified("Player_GetUpgradeStore", 0x44A2D0, "bootstrap"),
            ["ScienceManager_HasScience"] = Verified("ScienceManager_HasScience", 0x44D7C0, "bootstrap"),
            ["Player_GrantScience"] = Verified("Player_GrantScience", 0x454300, "bootstrap"),
            ["HashLookup"] = Verified("HashLookup", 0xE2C10, "bootstrap")
        };
    }

    private static IReadOnlyDictionary<string, VersionedAddress> BuildNativeAgentRefs()
    {
        return new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameClientPointer"] = Verified("GameClientPointer", 0x8D8CE4, "AgentGameApi.cpp"),
            ["GetThingClass"] = Verified("GetThingClass", 0x3E4230, "AgentGameApi.cpp"),
            ["LevelUpSelected"] = Verified("LevelUpSelected", 0x35C200, "AgentGameApi.cpp"),
            ["CreateUnit"] = Verified("CreateUnit", 0x205240, "AgentGameApi.cpp"),
            ["KillUnit"] = Verified("KillUnit", 0x39EA50, "AgentGameApi.cpp"),
            ["PlayerManager"] = Verified("PlayerManager", 0x8E8C9C, "AgentGameApi.cpp"),
            ["GetCurrentPlayer"] = Verified("GetCurrentPlayer", 0x4393E0, "AgentGameApi.cpp"),
            ["ThingTemplateStore"] = Verified("ThingTemplateStore", 0x8E6C58, "AgentGameApi.cpp"),
            ["SelectedUnitCode"] = Verified("SelectedUnitCode", 0x8E9838, "AgentGameApi.cpp"),
            ["ScienceStore"] = Verified("ScienceStore", 0x8DBBF0, "AgentGameApi.cpp"),
            ["ScienceStoreFindScience"] = Verified("ScienceStoreFindScience", 0x1456F0, "AgentGameApi.cpp"),
            ["ScienceStoreFindUpgrade"] = Verified("ScienceStoreFindUpgrade", 0x147260, "AgentGameApi.cpp"),
            ["ScienceManagerFindScience"] = Verified("ScienceManagerFindScience", 0x43C300, "AgentGameApi.cpp"),
            ["PlayerGetUpgradeStore"] = Verified("PlayerGetUpgradeStore", 0x44A2D0, "AgentGameApi.cpp"),
            ["ScienceManagerHasScience"] = Verified("ScienceManagerHasScience", 0x44D7C0, "AgentGameApi.cpp"),
            ["PlayerGrantScience"] = Verified("PlayerGrantScience", 0x454300, "AgentGameApi.cpp"),
            ["PlayerScienceManagerOffset"] = Verified("PlayerScienceManagerOffset", 0x1320, "AgentGameApi.cpp"),
            ["SelectionManager"] = Verified("SelectionManager", 0x8DB73C, "AgentGameApi.cpp"),
            ["SelectionListHeadOffset"] = Verified("SelectionListHeadOffset", 0x50, "AgentGameApi.cpp"),
            ["SelectionCountOffset"] = Verified("SelectionCountOffset", 0x5C, "AgentGameApi.cpp"),
            ["MouseWorldPointer"] = Verified("MouseWorldPointer", 0x8DAEFC, "AgentGameApi.cpp"),
            ["MouseWorldToMapPosition"] = Verified("MouseWorldToMapPosition", 0x1ED4A0, "AgentGameApi.cpp"),
            ["ObjectSetPosition"] = Verified("ObjectSetPosition", 0x3E3D00, "AgentGameApi.cpp"),
            ["MovementModuleOffset"] = Verified("MovementModuleOffset", 0x374, "AgentGameApi.cpp"),
            ["MovementContainerOffset"] = Verified("MovementContainerOffset", 0x200, "AgentGameApi.cpp"),
            ["ObjectOwnerOffset"] = Verified("ObjectOwnerOffset", 0x418, "AgentGameApi.cpp"),
            ["BodyOffset"] = Verified("BodyOffset", 0x33C, "AgentGameApi.cpp"),
            ["VeterancyOffset"] = Verified("VeterancyOffset", 0x3CC, "AgentGameApi.cpp"),
            ["WeaponContainerOffset"] = Verified("WeaponContainerOffset", 0x37C, "AgentGameApi.cpp"),
            ["UnitStatePrimaryOffset"] = Verified("UnitStatePrimaryOffset", 0xBC, "AgentGameApi.cpp"),
            ["UnitStateSecondaryOffset"] = Verified("UnitStateSecondaryOffset", 0xC8, "AgentGameApi.cpp"),
            ["OneHitDamageDeltaMode"] = Verified("OneHitDamageDeltaMode", 0, "AgentNativeHooks.cpp"),
            ["OneHitCaller1"] = Verified("OneHitCaller1", 0x3AD79E, "AgentNativeHooks.cpp"),
            ["OneHitCaller2"] = Verified("OneHitCaller2", 0x3ADEE2, "AgentNativeHooks.cpp"),
            ["OneHitCaller3"] = Verified("OneHitCaller3", 0x38E651, "AgentNativeHooks.cpp"),
            ["DestroySelectionListHeadOffset"] = Verified("DestroySelectionListHeadOffset", 0x54, "AgentGameApi.cpp"),
            ["ProductionModulesOffset"] = Verified("ProductionModulesOffset", 0x310, "AgentGameApi.cpp"),
            ["LocalContextSiblingOffset"] = Verified("LocalContextSiblingOffset", 0x1360, "AgentNativeHooks.cpp"),
            ["RestoreOreCapacityMode"] = Verified("RestoreOreCapacityMode", 1, "AgentNativeHooks.cpp")
        };
    }

    private static string HookKey(PatchHook hook)
    {
        return string.IsNullOrWhiteSpace(hook.ReturnLabel)
            ? hook.Address
            : hook.ReturnLabel;
    }

    private static VersionedAddress Verified(string symbolicName, int rva, string source)
    {
        return new VersionedAddress(symbolicName, rva, AddressSupportStatus.Verified, source);
    }

    private static int ParseRva(string expression)
    {
        var marker = $"{GameTarget.ProcessName}+";
        if (!expression.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Address '{expression}' is not relative to {GameTarget.ProcessName}.");
        }

        return Convert.ToInt32(expression[marker.Length..], 16);
    }

}
