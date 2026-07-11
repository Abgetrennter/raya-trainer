using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

internal static class Ra3_Uprising_Profile
{
    private const string Source10 = "RA3_Analysis/09_多版本兼容/uprising_1.0_signature_catalog.md";
    private const string Source11 = "RA3_Analysis/09_多版本兼容/uprising_1.1_signature_catalog.md";

    public static Ra3VersionProfile Create10()
    {
        return Create(
            id: "ra3_uprising_1.0",
            displayName: "RA3 Uprising 1.0",
            processName: "ra3ep1_1.0.game",
            fileVersions: ["1.0.3313.38400"],
            source: Source10,
            verifiedHookRvas: Build10VerifiedHookRvas(),
            expectedHookBytes: Build10ExpectedHookBytes(),
            remoteGlobals: Build10VerifiedRemoteGlobals(),
            engineFunctions: Build10VerifiedEngineFunctions(),
            nativeAgentRefs: Build10VerifiedNativeAgentRefs());
    }

    public static Ra3VersionProfile Create11()
    {
        return Create(
            id: "ra3_uprising_1.1",
            displayName: "RA3 Uprising 1.1",
            processName: "ra3ep1_1.1.game",
            fileVersions: ["1.01.0.0"],
            // PlayerManager RVA corrected after stage-3 signature analysis. IDA reads the
            // absolute operand 0xD05DD4; profile catalogs store RVA, hence 0x905DD4.
            // analysis: the Rva_8E8C9C anchor matched a call site (0x503610) that feeds
            // dword_D05DD4 into sub_86E690, a player-mask iterator that walks the player
            // array at [ecx+0x30]. 0xD05DD4 is therefore the real PlayerManager root.
            source: Source11,
            verifiedHookRvas: Build11VerifiedHookRvas(),
            expectedHookBytes: Build11ExpectedHookBytes(),
            remoteGlobals: Build11VerifiedRemoteGlobals(),
            engineFunctions: Build11VerifiedEngineFunctions(),
            nativeAgentRefs: Build11VerifiedNativeAgentRefs());
    }

    // 1.12 manifest RVA -> Uprising 1.0 RVA. Every active entry is a unique hit in
    // ra3ep1_1.0.game 1.0.3313.38400 (SHA256 ABA6A882...EA2580B).
    private static IReadOnlyDictionary<int, int> Build10VerifiedHookRvas()
    {
        return new Dictionary<int, int>
        {
            [0x0FF95B] = 0x14915B,
            [0x6CFDFE] = 0x67C7FA,
            [0x6CFD0D] = 0x67C6F8,
            [0x6CFE6C] = 0x67C868,
            [0x6CFEB5] = 0x67C8B1,
            [0x30F440] = 0x360920,
            [0x30F42E] = 0x36090E,
            [0x30F38C] = 0x36086C,
            [0x2F6CFF] = 0x34221F,
            [0x2EBB69] = 0x33BB19,
            [0x43EC19] = 0x48C3C9,
            [0x43EC70] = 0x48C420,
            [0x30A580] = 0x35BB90,
            [0x0E7563] = 0x130CD3,
            [0x30A6CD] = 0x35BCDD,
            [0x3DF770] = 0x3132A0,
            [0x3BA8F7] = 0x407A47,
            [0x3BA935] = 0x407A85,
            [0x6BB455] = 0x666C9F,
            [0x14674D] = 0x19013D,
            [0x38E511] = 0x3E24C1,
            [0x4451F0] = 0x492450,
            [0x445250] = 0x4924B0,
            [0x149740] = 0x1930F0,
            [0x1EC7CD] = 0x2395ED,
            [0x0FDBD0] = 0x0DB620,
            [0x3C226C] = 0x40B58D,
            [0x128735] = 0x171525,
            [0x438ED0] = 0x486940,
            [0x2D4933] = 0x3229B0,
            [0x30F530] = 0x360A1D,
            [0x12EEDF] = 0x17772F,
            [0x3651AE] = 0x3B86FE,
            [0x6E3DAA] = 0x678D4A,
            [0x6E3EF7] = 0x678E97,
            [0x2F8FE0] = 0x349550,
            // WeaponTemplate_GetEffectiveRange (1.12 sub_6F9460). Signature
            // 51 53 8B 5C 24 0C 85 DB 55 56 57 89 4C 24 10 matches uniquely at 0x749960.
            [0x2F9460] = 0x349960,
            [0x1437] = 0x001457
        };
    }

    // 1.12 manifest RVA -> Uprising 1.1 RVA. Verified VA = RVA + 0x400000, from
    // cross-version signature scans and follow-up static verification in the catalog.
    // Hooks absent here fall through to NeedsReanalysis. Uprising's legacy one-kill capture
    // group is intentionally superseded by the damage-delta gate below.
    private static IReadOnlyDictionary<int, int> Build11VerifiedHookRvas()
    {
        return new Dictionary<int, int>
        {
            [0x0FF95B] = 0x12EF6B, // _BackPlayerID @ 0x52EF6B
            [0x6CFDFE] = 0x664A3A, // _BackPlayerMoney @ 0xA64A3A
            [0x6CFD0D] = 0x664938, // _BackPlayerPower @ 0xA64938 (offset +0x30 applied in .asm)
            [0x6CFE6C] = 0x664AA8, // _BackPlayerSCPoint @ 0xA64AA8
            [0x6CFEB5] = 0x664AF1, // _BackPlayerHaveAllSC @ 0xA64AF1
            [0x30F440] = 0x3488B0, // _BackPlayerFastBuildContext @ 0x7488B0
            [0x30F42E] = 0x34889E, // _BackPlayerFastBuild @ 0x74889E
            [0x30F38C] = 0x3487FC, // _BackPlayerFastBuild2 @ 0x7487FC
            [0x2F6CFF] = 0x32A36F, // _BackPlayerFastBuild3 @ 0x72A36F
            [0x2EBB69] = 0x323C59, // _BackPlayerSuperPower @ 0x723C59 (owner +0x428)
            [0x43EC19] = 0x473E69, // _BackPlayerSuperPower2 @ 0x873E69
            [0x43EC70] = 0x473EC0, // _BackDisableAllSuperPower @ 0x873EC0
            [0x30A580] = 0x343B40, // _BackDisableAllSuperPower2 @ 0x743B40
            [0x0E7563] = 0x116BC3, // _BackPlayerFreeBuild @ 0x516BC3
            [0x30A6CD] = 0x343C8D, // _BackSecretProtocolDependency @ 0x743C8D
            [0x3DF770] = 0x2FB260, // _BackIgnorePrerequisites @ 0x6FB260
            [0x3BA8F7] = 0x3EF657, // _BackIgnorePrerequisitesUiDependency @ 0x7EF657
            [0x3BA935] = 0x3EF695, // _BackIgnorePrerequisitesUiChildDependency @ 0x7EF695
            [0x6BB455] = 0x64EB3F, // _BackIgnorePrerequisitesCommandStatusDependency @ 0xA4EB3F
            [0x14674D] = 0x17617D, // _BackIgnorePrerequisitesPlacementDependency @ 0x57617D
            [0x38E511] = 0x3CA241, // _BackIgnorePrerequisitesProductionDependency @ 0x7CA241
            [0x4451F0] = 0x47A030, // _BackIgnorePrerequisitesScience @ 0x87A030
            [0x445250] = 0x47A090, // _BackIgnorePrerequisitesBuildability @ 0x87A090
            [0x149740] = 0x1791F0, // _BackQuantityLimitGate @ 0x5791F0
            [0x1EC7CD] = 0x22081D, // _BackPlayerZoom @ 0x62081D
            [0x0FDBD0] = 0x0E44D0, // _BackZoomClamp @ 0x4E44D0
            [0x3C226C] = 0x3F319D, // _BackPlayerMap @ 0x7F319D (8-byte replay prefix unchanged)
            [0x128735] = 0x157125, // _BackSelectUnitAmmo @ 0x557125
            [0x438ED0] = 0x46E460, // _BackDangerLevel @ 0x86E460
            [0x2D4933] = 0x30A8D0, // _BackRestoreOreMine @ 0x70A8D0 (Uprising body uses [ecx+0x28])
            [0x30F530] = 0x3489AD, // _BackEnemyCantBuild @ 0x7489AD
            [0x12EEDF] = 0x15D31F, // _BackPlayerGodMode @ 0x55D31F
            [0x3651AE] = 0x3A048E, // _BackPlayerOneKillItMode @ 0x7A048E
            [0x6E3DAA] = 0x660E5A, // _BackChallengeModeTime @ 0xA60E5A
            [0x6E3EF7] = 0x660FA7, // _BackChallengeModeMoney @ 0xA60FA7
            [0x2F8FE0] = 0x331670, // _BackSelectedUnitAttackSpeedScale @ 0x731670
            // WeaponTemplate_GetEffectiveRange (1.12 sub_6F9460). Signature
            // 51 53 8B 5C 24 0C 85 DB 55 56 57 89 4C 24 10 matches uniquely at 0x731a80.
            [0x2F9460] = 0x331a80, // _BackSelectedUnitAttackRangeScale @ 0x731a80
            // Run In Background: WM_ACTIVATEAPP DoPause call site. Verified 0x401447 in Uprising 1.1
            // (signature kHook37BackgroundRunPauseGate unique hit).
            [0x1437] = 0x001447
            // Not mapped (NeedsReanalysis):
            //   _BackPlayerOneKillItModeData/Data2 (superseded by damage-delta gate),
            //   _BackPlayerOneKillItModeCaller/Caller2 (superseded by damage-delta gate).
        };
    }

    private static Ra3VersionProfile Create(
        string id,
        string displayName,
        string processName,
        string[] fileVersions,
        string source,
        IReadOnlyDictionary<int, int> verifiedHookRvas,
        IReadOnlyDictionary<int, byte[]> expectedHookBytes,
        IReadOnlyDictionary<string, int> remoteGlobals,
        IReadOnlyDictionary<string, int> engineFunctions,
        IReadOnlyDictionary<string, int> nativeAgentRefs)
    {
        return new Ra3VersionProfile
        {
            Id = id,
            DisplayName = displayName,
            ProcessName = processName,
            FileVersions = new HashSet<string>(fileVersions, StringComparer.OrdinalIgnoreCase),
            IsPatchInstallable = true,
            SupportsAgentBackend = true,
            SupportsDirectGameApi = true,
            SupportsSignatureScanning = true,
            Hooks = Ra3VersionProfileFactory.BuildHooks(
                verifiedHookRvas, source, expectedHookBytes, profileId: id),
            RemoteGlobals = Ra3VersionProfileFactory.BuildRemoteGlobals(remoteGlobals, source),
            EngineFunctions = Ra3VersionProfileFactory.BuildEngineFunctions(engineFunctions, source),
            NativeAgentRefs = Ra3VersionProfileFactory.BuildNativeAgentRefs(nativeAgentRefs, source),
            // Uprising's health-delta hook exposes the damage sign directly and replaces
            // the 1.12 caller/data capture chain.
            SupersededHooks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "_BackPlayerOneKillItModeData",
                "_BackPlayerOneKillItModeData2",
                "_BackPlayerOneKillItModeCaller",
                "_BackPlayerOneKillItModeCaller2"
            },
            OptionalSignatureSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "_BackPlayerOneKillItModeData",
                "_BackPlayerOneKillItModeData2",
                "_BackPlayerOneKillItModeCaller",
                "_BackPlayerOneKillItModeCaller2",
                "_BackFrameRateUnlockGameUpdate",
                "Rva_38E651",
                "Rva_3AD79E",
                "Rva_3ADEE2"
            }
        };
    }

    // Hook bytes whose absolute operands, relative call displacements, or structure offsets
    // differ from the 1.12 manifest. Each version keeps its exact image bytes so patch
    // installation fails closed before reaching an otherwise valid hook site.
    private static IReadOnlyDictionary<int, byte[]> Build10ExpectedHookBytes()
    {
        var result = Build11ExpectedHookBytes()
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        result[0x30A580] = [0xA1, 0x08, 0x08, 0xCF, 0x00, 0x80, 0xB8, 0xA6, 0x00, 0x00, 0x00, 0x00];
        result[0x30A6CD] = [0x8D, 0x54, 0x24, 0x14, 0x52, 0xE8, 0xA9, 0xFE, 0xDC, 0xFF];
        result[0x3DF770] = [0x51, 0x56, 0x8B, 0xF1, 0x8B, 0x0D, 0x08, 0x08, 0xCF, 0x00];
        result[0x3BA8F7] = [0xE8, 0x44, 0x41, 0xD2, 0xFF];
        result[0x3BA935] = [0xE8, 0x06, 0x41, 0xD2, 0xFF];
        result[0x6BB455] = [0xE8, 0xEC, 0x4E, 0xAC, 0xFF];
        result[0x14674D] = [0xE8, 0x4E, 0xBA, 0xF9, 0xFF];
        result[0x38E511] = [0xE8, 0xCA, 0x96, 0xD4, 0xFF];
        result[0x6E3EF7] = [0x8B, 0x78, 0x08, 0x68, 0x48, 0xD0, 0xC8, 0x00];
        result[0x2F8FE0] = [0x83, 0xEC, 0x08, 0xF3, 0x0F, 0x10, 0x05, 0xA4, 0x1D, 0xBF, 0x00];
        // GetEffectiveRange 6-byte prologue identical across all versions.
        result[0x2F9460] = [0x51, 0x53, 0x8B, 0x5C, 0x24, 0x0C];
        return result;
    }

    private static IReadOnlyDictionary<int, byte[]> Build11ExpectedHookBytes()
    {
        return new Dictionary<int, byte[]>
        {
            [0x6CFD0D] = [0x8B, 0x40, 0x04, 0x8B, 0x8E, 0xE0, 0x03, 0x00, 0x00],
            [0x2EBB69] = [0x8B, 0x98, 0x28, 0x04, 0x00, 0x00],
            [0x30A580] = [0xA1, 0x18, 0x5C, 0xCF, 0x00, 0x80, 0xB8, 0xA6, 0x00, 0x00, 0x00, 0x00],
            [0x30A6CD] = [0x8D, 0x54, 0x24, 0x14, 0x52, 0xE8, 0xC9, 0xDE, 0xDC, 0xFF],
            [0x3DF770] = [0x51, 0x56, 0x8B, 0xF1, 0x8B, 0x0D, 0x18, 0x5C, 0xCF, 0x00],
            [0x3BA8F7] = [0xE8, 0x04, 0x25, 0xD2, 0xFF],
            [0x3BA935] = [0xE8, 0xC6, 0x24, 0xD2, 0xFF],
            [0x6BB455] = [0xE8, 0x1C, 0x30, 0xAC, 0xFF],
            [0x14674D] = [0xE8, 0xDE, 0xB9, 0xF9, 0xFF],
            [0x38E511] = [0xE8, 0x1A, 0x79, 0xD4, 0xFF],
            [0x2D4933] = [0x8B, 0x41, 0x28, 0x2B, 0x41, 0x14],
            [0x6E3DAA] = [0xF3, 0x0F, 0x2C, 0x58, 0x38],
            [0x6E3EF7] = [0x8B, 0x78, 0x08, 0x68, 0xD0, 0xDF, 0xC8, 0x00],
            [0x2F8FE0] = [0x83, 0xEC, 0x08, 0xF3, 0x0F, 0x10, 0x05, 0x5C, 0x7E, 0xBF, 0x00],
            // GetEffectiveRange 6-byte prologue identical across all versions.
            [0x2F9460] = [0x51, 0x53, 0x8B, 0x5C, 0x24, 0x0C]
        };
    }

    // 1.12 bootstrap-source RVA -> Uprising 1.0 RVA. Scraped from the Uprising source set
    // and verified against uprising_1.0_signature_catalog.md.
    private static IReadOnlyDictionary<string, int> Build10VerifiedRemoteGlobals()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameClientPointer"] = 0x8F0808,
            ["MouseWorldPointer"] = 0x8F2A2C,
            ["SelectionManager"] = 0x8F3284,
            ["ScienceStore"] = 0x8F3740,
            ["ThingTemplateStore"] = 0x8FD3C8,
            ["PlayerManager"] = 0x9009C4,
            ["SelectedUnitCode"] = 0x903648
        };
    }

    private static IReadOnlyDictionary<string, int> Build11VerifiedRemoteGlobals()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameClientPointer"] = 0x8F5C18,
            ["MouseWorldPointer"] = 0x8F7E3C,
            ["SelectionManager"] = 0x8F8694,
            ["ScienceStore"] = 0x8F8B50,
            ["ThingTemplateStore"] = 0x9027D8,
            ["PlayerManager"] = 0x905DD4,
            ["SelectedUnitCode"] = 0x908A58
        };
    }

    private static IReadOnlyDictionary<string, int> Build10VerifiedEngineFunctions()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ScienceStore_FindScience"] = 0x18F140,
            ["ScienceStore_FindUpgrade"] = 0x190D60,
            ["MouseWorld_ToMapPosition"] = 0x231730,
            ["CreateUnit"] = 0x2510B0,
            ["LevelUpSelected"] = 0x3B0680,
            ["KillUnit"] = 0x423A90,
            ["Object_SetPosition"] = 0x317B40,
            ["ThingTemplateStore_Find"] = 0x318070,
            ["Player_GetScienceManager"] = 0x486E30,
            ["ScienceManager_FindScience"] = 0x489C40,
            ["Player_GetUpgradeStore"] = 0x4975B0,
            ["ScienceManager_HasScience"] = 0x49AB30,
            ["Player_GrantScience"] = 0x4A1650,
            ["HashLookup"] = 0x12BB90
        };
    }

    private static IReadOnlyDictionary<string, int> Build11VerifiedEngineFunctions()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ScienceStore_FindScience"] = 0x175140,   // Rva_1456F0 -> 0x575140
            ["ScienceStore_FindUpgrade"] = 0x176DA0,    // Rva_147260 -> 0x576DA0
            ["MouseWorld_ToMapPosition"] = 0x218680,    // Rva_1ED4A0 -> 0x618680
            ["CreateUnit"] = 0x238220,                   // Rva_205240 -> 0x638220
            ["LevelUpSelected"] = 0x3985C0,             // Rva_35C200 -> 0x7985C0
            ["KillUnit"] = 0x40B690,                    // Rva_39EA50 -> 0x80B690
            ["Object_SetPosition"] = 0x2FFAB0,           // Rva_3E3D00 -> 0x6FFAB0
            ["ThingTemplateStore_Find"] = 0x2FFFE0,     // Rva_3E4230 -> 0x6FFFE0
            ["Player_GetScienceManager"] = 0x46E950,    // Rva_4393E0 -> 0x86E950
            ["ScienceManager_FindScience"] = 0x471780,  // Rva_43C300 -> 0x871780
            ["Player_GetUpgradeStore"] = 0x47F1C0,      // Rva_44A2D0 -> 0x87F1C0
            ["ScienceManager_HasScience"] = 0x4827B0,   // Rva_44D7C0 -> 0x8827B0
            ["Player_GrantScience"] = 0x4891B0,         // Rva_454300 -> 0x8891B0
            ["HashLookup"] = 0x111B60                   // Rva_E2C10 -> 0x511B60
        };
    }

    private static IReadOnlyDictionary<string, int> Build10VerifiedNativeAgentRefs()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameClientPointer"] = 0x8F0808,
            ["GetThingClass"] = 0x318070,
            ["LevelUpSelected"] = 0x3B0680,
            ["CreateUnit"] = 0x2510B0,
            ["KillUnit"] = 0x423A90,
            ["PlayerManager"] = 0x9009C4,
            ["GetCurrentPlayer"] = 0x486E30,
            ["ThingTemplateStore"] = 0x8FD3C8,
            ["SelectedUnitCode"] = 0x903648,
            ["ScienceStore"] = 0x8F3740,
            ["ScienceStoreFindScience"] = 0x18F140,
            ["ScienceStoreFindUpgrade"] = 0x190D60,
            ["ScienceManagerFindScience"] = 0x489C40,
            ["PlayerGetUpgradeStore"] = 0x4975B0,
            ["ScienceManagerHasScience"] = 0x49AB30,
            ["PlayerGrantScience"] = 0x4A1650,
            ["PlayerScienceManagerOffset"] = 0x1320,
            ["SelectionManager"] = 0x8F3284,
            ["SelectionListHeadOffset"] = 0x54,
            ["SelectionCountOffset"] = 0x60,
            ["MouseWorldPointer"] = 0x8F2A2C,
            ["MouseWorldToMapPosition"] = 0x231730,
            ["ObjectSetPosition"] = 0x317B40,
            ["MovementModuleOffset"] = 0x384,
            ["MovementContainerOffset"] = 0x20C,
            ["ObjectOwnerOffset"] = 0x428,
            ["BodyOffset"] = 0x34C,
            ["VeterancyOffset"] = 0x3DC,
            ["WeaponContainerOffset"] = 0x37C,
            ["UnitStatePrimaryOffset"] = 0xC0,
            ["UnitStateSecondaryOffset"] = 0xCC,
            ["OneHitDamageDeltaMode"] = 1,
            ["OneHitCaller1"] = 0,
            ["OneHitCaller2"] = 0,
            ["OneHitCaller3"] = 0,
            ["DestroySelectionListHeadOffset"] = 0x54,
            ["ProductionModulesOffset"] = 0x320,
            ["LocalContextSiblingOffset"] = 0,
            ["RestoreOreCapacityMode"] = 2
        };
    }

    private static IReadOnlyDictionary<string, int> Build11VerifiedNativeAgentRefs()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameClientPointer"] = 0x8F5C18,
            ["GetThingClass"] = 0x2FFFE0,
            ["LevelUpSelected"] = 0x3985C0,
            ["CreateUnit"] = 0x238220,
            ["KillUnit"] = 0x40B690,
            ["PlayerManager"] = 0x905DD4,
            ["GetCurrentPlayer"] = 0x46E950,
            ["ThingTemplateStore"] = 0x9027D8,
            ["SelectedUnitCode"] = 0x908A58,
            ["ScienceStore"] = 0x8F8B50,
            ["ScienceStoreFindScience"] = 0x175140,
            ["ScienceStoreFindUpgrade"] = 0x176DA0,
            ["ScienceManagerFindScience"] = 0x471780,
            ["PlayerGetUpgradeStore"] = 0x47F1C0,
            ["ScienceManagerHasScience"] = 0x4827B0,
            ["PlayerGrantScience"] = 0x4891B0,
            ["PlayerScienceManagerOffset"] = 0x1320,
            ["SelectionManager"] = 0x8F8694,
            ["SelectionListHeadOffset"] = 0x54,
            ["SelectionCountOffset"] = 0x60,
            ["MouseWorldPointer"] = 0x8F7E3C,
            ["MouseWorldToMapPosition"] = 0x218680,
            ["ObjectSetPosition"] = 0x2FFAB0,
            ["MovementModuleOffset"] = 0x384,
            ["MovementContainerOffset"] = 0x20C,
            ["ObjectOwnerOffset"] = 0x428,
            ["BodyOffset"] = 0x34C,
            ["VeterancyOffset"] = 0x3DC,
            ["WeaponContainerOffset"] = 0x37C,
            ["UnitStatePrimaryOffset"] = 0xC0,
            ["UnitStateSecondaryOffset"] = 0xCC,
            ["OneHitDamageDeltaMode"] = 1,
            ["OneHitCaller1"] = 0,
            ["OneHitCaller2"] = 0,
            ["OneHitCaller3"] = 0,
            ["DestroySelectionListHeadOffset"] = 0x54,
            ["ProductionModulesOffset"] = 0x320,
            ["LocalContextSiblingOffset"] = 0,
            ["RestoreOreCapacityMode"] = 2
        };
    }
}
