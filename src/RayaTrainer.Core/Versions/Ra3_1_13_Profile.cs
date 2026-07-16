using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

internal static class Ra3_1_13_Profile
{
    private const string Source = "RA3_Analysis/09_多版本兼容/ra3_1.13_hook_verification.md";

    public static Ra3VersionProfile Create()
    {
        return new Ra3VersionProfile
        {
            Id = "ra3_1.13",
            DisplayName = "RA3 1.13",
            ProcessName = "ra3_1.13.game",
            FileVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "1.13.0.0",
                "1.13.3444.25830"
            },
            IsPatchInstallable = true,
            SupportsAgentBackend = true,
            SupportsDirectGameApi = true,
            SupportsSignatureScanning = true,
            Hooks = Ra3VersionProfileFactory.BuildHooks(
                BuildVerifiedHookRvas(), Source, BuildExpectedHookBytes(), profileId: "ra3_1.13"),
            RemoteGlobals = Ra3VersionProfileFactory.BuildRemoteGlobals(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["GameClientPointer"] = 0x8E21A4,
                ["MouseWorldPointer"] = 0x8E43BC,
                ["SelectionManager"] = 0x8E4BFC,
                ["ScienceStore"] = 0x8E50B0,
                ["ThingTemplateStore"] = 0x8F0108,
                ["PlayerManager"] = 0x8E6650,
                ["SelectedUnitCode"] = 0x8F2CE8
            }, Source),
            EngineFunctions = Ra3VersionProfileFactory.BuildEngineFunctions(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["ScienceStore_FindScience"] = 0x16FC00,
                ["ScienceStore_FindUpgrade"] = 0x171740,
                ["MouseWorld_ToMapPosition"] = 0x20ECF0,
                ["CreateUnit"] = 0x22E3E0,
                ["LevelUpSelected"] = 0x385600,
                ["KillUnit"] = 0x3C7190,
                ["Object_SetPosition"] = 0x40CFC0,
                ["ThingTemplateStore_Find"] = 0x40D5B0,
                ["Player_GetScienceManager"] = 0x4627D0,
                ["ScienceManager_FindScience"] = 0x4657A0,
                ["Player_GetUpgradeStore"] = 0x4739C0,
                ["ScienceManager_HasScience"] = 0x476D80,
                ["Player_GrantScience"] = 0x47D650,
                ["HashLookup"] = 0x10CB10
            }, Source),
            NativeAgentRefs = Ra3VersionProfileFactory.BuildNativeAgentRefs(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    // GameClientPointer mirrors RemoteGlobals; GetThingClass is the same function as
                    // ThingTemplateStore_Find uses ecx=ThingTemplateStore.
                    // LevelUpSelected/CreateUnit/KillUnit mirror EngineFunctions. All five 1.13 RVAs were
                    // verified against the 1.13 IDB in ra3_1.13_hook_verification.md (2026-06-22/24).
                    ["GameClientPointer"] = 0x8E21A4,
                    ["GetThingClass"] = 0x40D5B0,
                    ["LevelUpSelected"] = 0x385600,
                    ["CreateUnit"] = 0x22E3E0,
                    ["KillUnit"] = 0x3C7190,
                    ["PlayerManager"] = 0x8E6650,
                    ["GetCurrentPlayer"] = 0x4627D0,
                    ["ThingTemplateStore"] = 0x8F0108,
                    ["SelectedUnitCode"] = 0x8F2CE8,
                    ["ScienceStore"] = 0x8E50B0,
                    ["ScienceStoreFindScience"] = 0x16FC00,
                    ["ScienceStoreFindUpgrade"] = 0x171740,
                    ["ScienceManagerFindScience"] = 0x4657A0,
                    ["PlayerGetUpgradeStore"] = 0x4739C0,
                    ["ScienceManagerHasScience"] = 0x476D80,
                    ["PlayerGrantScience"] = 0x47D650,
                    ["PlayerScienceManagerOffset"] = 0x1320,
                    ["SelectionManager"] = 0x8E4BFC,
                    ["SelectionListHeadOffset"] = 0x50,
                    ["SelectionCountOffset"] = 0x5C,
                    ["MouseWorldPointer"] = 0x8E43BC,
                    ["MouseWorldToMapPosition"] = 0x20ECF0,
                    ["ObjectSetPosition"] = 0x40CFC0,
                    ["MovementModuleOffset"] = 0x374,
                    ["MovementContainerOffset"] = 0x200,
                    ["ObjectOwnerOffset"] = 0x418,
                    ["BodyOffset"] = 0x33C,
                    ["VeterancyOffset"] = 0x3CC,
                    ["WeaponContainerOffset"] = 0x37C,
                    ["UnitStatePrimaryOffset"] = 0xBC,
                    ["UnitStateSecondaryOffset"] = 0xC8,
                    ["OneHitDamageDeltaMode"] = 0,
                    ["OneHitCaller1"] = 0x3EBABE,
                    ["OneHitCaller2"] = 0x3EC202,
                    ["OneHitCaller3"] = 0x3CC9F1,
                    ["DestroySelectionListHeadOffset"] = 0x54,
                    ["ProductionModulesOffset"] = 0x310,
                    ["LocalContextSiblingOffset"] = 0x1360,
                    ["RestoreOreCapacityMode"] = 1,
                    // GameObjectAddUpgrade: VA 0x79C190 (signature-verified unique match 2026-07-14).
                    ["GameObjectAddUpgrade"] = 0x39C190,
                    // UpgradeTemplateTypeOffset: Type field within UpgradeTemplateDefinition,
                    // verified identical to 1.12 (engine struct layout unchanged).
                    ["UpgradeTemplateTypeOffset"] = 0x24
                },
                Source),
            OptionalSignatureSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "_BackChallengeModeTime",
                "_BackChallengeModeMoney",
                "_BackFrameRateUnlockGameUpdate",
                "Rva_88DFD0"
            }
        };
    }

    private static IReadOnlyDictionary<int, int> BuildVerifiedHookRvas()
    {
        return new Dictionary<int, int>
        {
            [0x0FF95B] = 0x1294AB,
            [0x6CFDFE] = 0x6699CE,
            [0x6CFD0D] = 0x6698DD,
            [0x6CFE6C] = 0x669A3C,
            [0x6CFEB5] = 0x669A85,
            [0x30F440] = 0x3386E0,
            [0x30F42E] = 0x3386CE,
            [0x30F38C] = 0x33862C,
            [0x2F6CFF] = 0x31FEDF,
            [0x2EBB69] = 0x314D19,
            [0x43EC19] = 0x467FC9,
            [0x43EC70] = 0x468020,
            [0x30A580] = 0x333810,
            [0x30A6CD] = 0x33395D,
            [0x0E7563] = 0x111953,
            [0x3DF770] = 0x408A30,
            [0x3BA8F7] = 0x3E38F7,
            [0x3BA935] = 0x3E3935,
            [0x6BB455] = 0x654D05,
            [0x14674D] = 0x170C1D,
            [0x38E511] = 0x3B6751,
            [0x4451F0] = 0x46E440,
            [0x445250] = 0x46E4A0,
            [0x149740] = 0x173DE0,
            [0x1EC7CD] = 0x216C7D,
            [0x0FDBD0] = 0x0D81E0,
            [0x3C226C] = 0x3EB50C,
            [0x128735] = 0x152115,
            [0x438ED0] = 0x4622C0,
            [0x2D4933] = 0x2FDBB3,
            [0x30F530] = 0x3387D0,
            [0x12EEDF] = 0x1588FF,
            [0x3651AE] = 0x38CB4E,
            [0x3FE714] = 0x427B34,
            [0x2E24E3] = 0x30B6C3,
            [0x38E2F3] = 0x3B6533,
            [0x38E360] = 0x3B65A0,
            // Run In Background: WM_ACTIVATEAPP loss-of-focus DoPause call in the
            // main WndProc (verified 2026-06-29, ra3_1.13_hook_verification.md).
            // Signature 6A 00 6A 03 8B CE E8 ?? ?? ?? ?? 8B matches uniquely at 0x401427.
            [0x1437] = 0x1427,
            // WeaponStateMachine_ScaleDuration. The entry shape is shared by
            // 1.12/1.13/Uprising; each profile supplies its own verified RVA and bytes.
            [0x2F8FE0] = 0x322190
        };
    }

    private static IReadOnlyDictionary<int, byte[]> BuildExpectedHookBytes()
    {
        return new Dictionary<int, byte[]>
        {
            [0x30A580] = [0xA1, 0xA4, 0x21, 0xCE, 0x00, 0x80, 0xB8, 0xA5, 0x00, 0x00, 0x00, 0x00],
            [0x30A6CD] = [0x8D, 0x54, 0x24, 0x14, 0x52, 0xE8, 0xA9, 0x91, 0xDD, 0xFF],
            [0x3DF770] = [0x51, 0x56, 0x8B, 0xF1, 0x8B, 0x0D, 0xA4, 0x21, 0xCE, 0x00],
            [0x3BA8F7] = [0xE8, 0x14, 0x92, 0xD2, 0xFF],
            [0x3BA935] = [0xE8, 0xD6, 0x91, 0xD2, 0xFF],
            [0x6BB455] = [0xE8, 0x06, 0x7E, 0xAB, 0xFF],
            [0x14674D] = [0xE8, 0xEE, 0xBE, 0xF9, 0xFF],
            [0x38E511] = [0xE8, 0xBA, 0x63, 0xD5, 0xFF],
            [0x2F8FE0] = [0x83, 0xEC, 0x08, 0xF3, 0x0F, 0x10, 0x05, 0x50, 0xD2, 0xBE, 0x00]
        };
    }

}
