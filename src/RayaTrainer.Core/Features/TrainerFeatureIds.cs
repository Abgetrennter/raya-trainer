namespace RayaTrainer.Core.Features;

public static class TrainerFeatureIds
{
    public const string Money = "Money";
    public const string Power = "Power";
    public const string SecretProtocolPoints = "SC POINT";
    public const string GetBase = "Get Me Base";
    public const string Reinforcement = "We Need Back";
    public const string CopySelectedUnit = "Select Unit Copy For Me";
    public const string GrantSecretProtocol = "Grant Secret Protocol";
    public const string GrantSelectedObjectUpgrade = "Grant Selected Object Upgrade";
    public const string ReplaceTemplateModel = "Replace Template Model";
    public const string ReplaceTemplateWeapon = "Replace Template Weapon";
    public const string SetSelectedUnitTargetHealth = "Set Selected Unit Target Health";
    public const string SecretProtocolBindingProbe = "Secret Protocol Binding Probe";
    public const string AutoRepair = "Player Auto Repair";
    public const string SuperPower = "SUPER POWER";
    public const string SecretProtocolDependencyBypass = "Secret Protocol Dependency Bypass";
    public const string DisableAllSecretProtocols = "Disable ALL SP";
    public const string ExpandProductionQueue = "Expand Production Queue";
    public const string RestoreProductionQueue = "Restore Production Queue";
    public const string TeleportSelectedUnitsToMouse = "Teleport Selected Units To Mouse";
    public const string ExecuteReinforcementQueue = "ExecuteReinforcementQueue";
    public const string ReadSelectedUnitCode = "ReadSelectedUnitCode";
    public const string ClearSelectedAttackSpeedEffects = "Clear Selected Attack Speed Effects";
    public const string ClearSelectedAttackRangeEffects = "Clear Selected Attack Range Effects";
    public const string SelectedUnitObjectUpgrade = "Selected Unit Object Upgrade";

    // ── Health modes ────────────────────────────────────────────────────────
    public const string SelectUnitHpMax = "Select Unit HP MAX";
    public const string SelectUnitHpMin = "Select Unit HP MIN";
    public const string RestoreSelectUnitNormalHp = "Restore Select Unit Normal HP";

    // ── Speed / freeze ─────────────────────────────────────────────────────
    public const string SelectUnitSuperSpeed = "Select Unit Super Speed";
    public const string SelectUnitSlowSpeed = "Select Unit Slow Speed";
    public const string SelectUnitFreeze = "Select Unit Freeze";
    public const string RestoreSelectUnitSpeed = "Restore Select Unit Speed";

    // ── Misc selected unit actions ──────────────────────────────────────────
    public const string SelectUnitLevelUp = "Select Unit Level UP";
    public const string SelectUnitChangeId = "Select Unit Change ID";
    public const string DestorySelectUnit = "Destory Select Unit";
    public const string SetUnitSupportState = "Set Unit Support State";

    // ── Secret protocol / tech ──────────────────────────────────────────────
    public const string SovietOrbitalRefuseRankOneProbe = "Soviet Orbital Refuse Rank 1 Probe";
    public const string ClearPlayerTechLocks = "Clear Player Tech Locks";

    // ── Ammo ────────────────────────────────────────────────────────────────
    public const string FillSelectedUnitAmmo = "Fill Selected Unit Ammo";
    public const string ResetSelectedUnitAmmo = "Reset Selected Unit Ammo";

    // ── Weapon toggles ──────────────────────────────────────────────────────
    public const string ToggleSelectedUnitAttackSpeed = "Toggle Selected Unit Attack Speed";
    public const string ToggleSelectedUnitAttackRange = "Toggle Selected Unit Attack Range";

    // ── Pulses ──────────────────────────────────────────────────────────────
    public const string ChallengeMoney = "Challenge Money";
    public const string RestoreSelectOreMine = "Restore Select Ore Mine";
    public const string FreeBuild = "Free Build";

    // ── Danger level ────────────────────────────────────────────────────────
    public const string DangerLevelMax = "Danger Level MAX";
    public const string DangerLevelMin = "Danger Level MIN";
    public const string RestoreDangerLevelNormal = "Restore Danger Level Normal";

    // ── Toggle features with native state IDs ───────────────────────────────
    public const string HaveAllSc = "HAVE ALL SC";
    public const string FastBuild = "FAST BUILD";
    public const string Zoom = "Zoom";
    public const string Map = "MAP";
    public const string EnemyCantBuild = "Enemy Can't Build";
    public const string PlayerGodMode = "Player God Mode";
    public const string PlayerOneKillMode = "Player One Kill Mode";
    public const string ChallengeTime = "Challenge Time";
    public const string RunInBackground = "Run In Background";
    public const string FrameRateUnlock60fps = "Frame Rate Unlock 60fps";
    public const string LogicTimeFreeze = "Logic Time Freeze";
    public const string LogicTimeSlowMotion = "Logic Time Slow Motion";
    public const string IgnorePrerequisites = "Ignore Prerequisites";
    public const string IgnoreQuantityLimit = "Ignore Quantity Limit";

    // 主控操作（非游戏内动作）：通过 Win32 RegisterHotKey 注册为全局热键，
    // 与游戏窗口前台无关。修改器最小化时也能触发。
    public const string DetectProcess = "DetectProcess";
    public const string LaunchAndLoad = "LaunchAndLoad";
}
