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
    public const string SelectedUnitObjectUpgrade = "Selected Unit Object Upgrade";

    // ── Health modes ────────────────────────────────────────────────────────
    public const string SelectUnitHpMax = "Select Unit HP MAX";
    public const string SelectUnitHpMin = "Select Unit HP MIN";
    public const string RestoreSelectUnitNormalHp = "Restore Select Unit Normal HP";


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

    // 游戏内面板显隐：App 侧快捷键（默认 F10），经底层键盘钩子触发后通过管道下发
    // SetOverlayState 切换面板显示。走 App 钩子而非原生 WM_KEYDOWN，可在对局内 DirectInput
    // 抢占键盘时仍然生效。
    public const string ToggleOverlay = "Toggle Overlay";

    // ── Product Intent features ────────────────────────────────────────────
    // WPF 侧统一属性修改入口：点击后经产品控制会话提交同一 Product Intent，
    // 与 Overlay/Web 走同一 AttributeModifier 路由（统一属性修改体系阶段 D）。
    public const string ProductSpeedFast = "Product Selected Unit Speed Fast";
    public const string ProductSpeedSlow = "Product Selected Unit Speed Slow";
    public const string ProductSpeedFreeze = "Product Selected Unit Speed Freeze";
    public const string ProductSpeedRestore = "Product Selected Unit Speed Restore";
    public const string ProductAttackSpeedEnable = "Product Selected Unit Attack Speed Enable";
    public const string ProductAttackSpeedDisable = "Product Selected Unit Attack Speed Disable";
    // Slot ProductAttackSpeedExtreme retired 2026-08 — replaced by infinite fire rate via AttackSpeedEnable.
    public const string ProductAttackSpeedCustom = "Product Selected Unit Attack Speed Custom";
    public const string ProductAttackRangeEnable = "Product Selected Unit Attack Range Enable";
    public const string ProductAttackRangeDisable = "Product Selected Unit Attack Range Disable";
    public const string ProductAttackRangeCustom = "Product Selected Unit Attack Range Custom";
    public const string ProductAttackDamageCustom = "Product Selected Unit Attack Damage Custom";
    public const string ProductMaxHealthCustom = "Product Selected Unit Max Health Custom";
    public const string ProductMoveSpeedCustom = "Product Selected Unit Move Speed Custom";
    public const string ProductClearAttackSpeed = "Product Clear Attack Speed Effects";
    public const string ProductClearAttackRange = "Product Clear Attack Range Effects";

    // 战场支援产品（统一属性修改体系阶段 D）：精兵学院（生产升星）、车库（治疗光环）、将军刽子手生成。
    public const string ProductVeterancyGrant = "Product Veterancy Grant";
    public const string ProductHealingAuraEnable = "Product Healing Aura Enable";
    public const string ProductHealingAuraDisable = "Product Healing Aura Disable";
    public const string ProductSpawnMechaKing = "Product Spawn Mecha King";
}
