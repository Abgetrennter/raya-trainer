namespace RayaTrainer.Core.Agent;

public enum AgentCommand : ushort
{
    Ping = 1,
    GetStatus = 2,
    RestorePatches = 4,
    SetFeatureStates = 5,
    SetRuntimePatchSet = 6,
    GetFeatureStates = 7,
    ReadSelectedUnitCode = 8,
    SmokeGetThingClass = 10,
    LevelUpSelected = 11,
    CreateUnit = 12,
    CopyForMe = 14,
    GetMeBase = 15,
    WeNeedBack = 16,
    SetUnitState = 17,
    GetCurrentPlayer = 18,
    LookupScienceByHash = 19,
    GrantPlayerTech = 20,
    GrantUpgradeToPlayer = 21,
    HasUpgrade = 22,
    LookupTemplateByHash = 23,
    LookupUpgradeByHash = 24,
    GrantSecretProtocol = 25,
    GrantSelectedUpgrade = 26,
    ClearPlayerTechLocks = 27,
    SecretProtocolBindingProbe = 28,
    ReplaceTemplateModel = 29,
    ReplaceTemplateWeapon = 30,
    SetSelectedStatusBit = 31,
    SetSelectedUnitHealth = 32,
    GetGameMode = 36,
    ExpandProductionQueue = 37,
    TeleportSelectedUnitsToMouse = 38,
    SetSelectedUnitAmmo = 41,
    GetSelectedUnitUpgrades = 46,
    GrantObjectUpgradeOnSelectedSameType = 47,
    ListScriptOperations = 48,
    DescribeScriptOperation = 49,
    ExecuteScriptAction = 50,
    EvaluateScriptCondition = 51,
    InvokeLuaBinding = 52,
    ProbeLuaObjectBridge = 53,
    ExecuteRecipePlan = 54,
    SetOverlayState = 55,
    GetOverlayStatus = 56,
    QueryMatchContext = 57,
    SubmitProductIntent = 58,
    GetProductResult = 59,
    GetDesiredIntents = 60,
    ApplyDurableProductPolicy = 61,
    ReplaceReinforcementPresetProjection = 62,
    GetReinforcementPresetConsoleState = 63,
    ReplaceSecretProtocolPresetProjection = 64,
    GetSecretProtocolPresetConsoleState = 65,
    GetCapabilitySnapshot = 67,
    GetRuntimeDiagnostics = 68,
    LoadBundledRuntimeAssets = 69,
    // Reads the current selection's engine ObjectIDs from the published Match Context
    // snapshot; additive within v13, used by the WPF Product Intent route to compose
    // Captured bindings.
    GetSelectedObjectIds = 70,
    // Reads the committed ascension policy table ((attributeType, valueBits, scopeMask,
    // faction) per entry) for the ascension matrix's manual read-back; additive within v14.
    GetAscensionPolicies = 71
}

// Retired command IDs stay permanently reserved and are never re-used or renumbered:
// 3 (InstallPatches), 9 (ReadMemory), 33 (SetNativeCatalog), 35 (ScanSignatures),
// 34 (GetMismatchDiagnostics), 39 (SetSelectedUnitSpeed), 42-45 (attack speed/range flag
// commands), 13 (KillUnit), 40 (CaptureSelectedUnits), 66 (GetRuntimeStatus).

public enum ScriptOperationCommand : ushort
{
    ListScriptOperations = 48,
    DescribeScriptOperation = 49,
    ExecuteScriptAction = 50,
    EvaluateScriptCondition = 51,
    InvokeLuaBinding = 52,
    ProbeLuaObjectBridge = 53,
    ExecuteRecipePlan = 54,
}
