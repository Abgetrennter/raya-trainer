namespace RayaTrainer.Core.Agent;

public static class NativeHookCatalog
{
    private static readonly IReadOnlyDictionary<string, uint> HookIds =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["_BackPlayerID"] = 1,
            ["_BackPlayerMoney"] = 2,
            ["_BackPlayerPower"] = 3,
            ["_BackPlayerSCPoint"] = 4,
            ["_BackPlayerHaveAllSC"] = 5,
            ["_BackPlayerFastBuildContext"] = 6,
            ["_BackPlayerFastBuild"] = 7,
            ["_BackPlayerFastBuild2"] = 8,
            ["_BackPlayerFastBuild3"] = 9,
            ["_BackPlayerSuperPower"] = 10,
            ["_BackPlayerSuperPower2"] = 11,
            ["_BackDisableAllSuperPower"] = 12,
            ["_BackDisableAllSuperPower2"] = 13,
            ["_BackPlayerFreeBuild"] = 14,
            ["_BackSecretProtocolDependency"] = 15,
            ["_BackIgnorePrerequisites"] = 16,
            ["_BackIgnorePrerequisitesUiDependency"] = 17,
            ["_BackIgnorePrerequisitesUiChildDependency"] = 18,
            ["_BackIgnorePrerequisitesCommandStatusDependency"] = 19,
            ["_BackIgnorePrerequisitesPlacementDependency"] = 20,
            ["_BackIgnorePrerequisitesProductionDependency"] = 21,
            ["_BackIgnorePrerequisitesScience"] = 22,
            ["_BackIgnorePrerequisitesBuildability"] = 23,
            ["_BackQuantityLimitGate"] = 24,
            ["_BackPlayerZoom"] = 25,
            ["_BackPlayerMap"] = 26,
            ["_BackSelectUnitAmmo"] = 27,
            ["_BackDangerLevel"] = 28,
            ["_BackRestoreOreMine"] = 29,
            ["_BackEnemyCantBuild"] = 30,
            ["_BackPlayerGodMode"] = 31,
            ["_BackPlayerOneKillItMode"] = 32,
            ["_BackPlayerOneKillItModeData"] = 33,
            ["_BackPlayerOneKillItModeData2"] = 34,
            ["_BackPlayerOneKillItModeCaller"] = 35,
            ["_BackPlayerOneKillItModeCaller2"] = 36,
            ["_BackChallengeModeTime"] = 37,
            ["_BackChallengeModeMoney"] = 38,
            ["_BackBackgroundRunPauseGate"] = 39,
            ["_BackSelectedUnitAttackSpeedScale"] = 40,
            ["_BackFrameRateUnlockGameUpdate"] = 41,
            ["_BackZoomClamp"] = 42
        };

    public static uint GetHookId(string? returnLabel)
    {
        if (string.IsNullOrWhiteSpace(returnLabel) || !HookIds.TryGetValue(returnLabel, out var id))
        {
            throw new InvalidOperationException($"Hook '{returnLabel ?? "<unnamed>"}' has no native handler id.");
        }

        return id;
    }
}
