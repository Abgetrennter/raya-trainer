using System.Text.Json.Serialization;

namespace RayaTrainer.App.Web;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrainerFeatureType
{
    Toggle,
    Action
}

public sealed record TrainerWebStatusResponse(
    bool PatchesInstalled,
    bool AgentReady,
    int? TargetProcessId,
    int InstalledHookCount);

public sealed record TrainerWebCommandResult(
    bool Success,
    string Message);

public sealed record TrainerPairingRequest(
    string? DeviceName);

public sealed record TrainerPairingResponse(
    bool Approved,
    string? Token,
    string Message);

public sealed record TrainerToggleRequest(
    string FeatureId,
    bool Enabled);

public sealed record TrainerToggleStateRequest(
    bool Enabled);

public sealed record TrainerResourceRequest(
    int MoneyAmount,
    int PowerValue,
    int ScPointValue);

public sealed record TrainerReinforcementRequest(
    uint UnitId,
    int Count,
    int Rank);

public sealed record TrainerReinforcementQueueRequest(
    IReadOnlyList<TrainerReinforcementRequest> Entries);

public sealed record TrainerSecretProtocolRequest(
    uint PlayerTechId,
    uint UpgradeId);

public sealed record TrainerSecretProtocolQueueRequest(
    IReadOnlyList<TrainerSecretProtocolRequest> Entries);

public sealed record TrainerReinforcementPresetEntryInfo(
    string Name,
    uint UnitId,
    string UnitIdText,
    int Count,
    int Rank);

public sealed record TrainerReinforcementPresetInfo(
    string Name,
    IReadOnlyList<TrainerReinforcementPresetEntryInfo> Entries);

public sealed record TrainerSecretProtocolPresetEntryInfo(
    string Mod,
    string Faction,
    string Name,
    uint PlayerTechId,
    string PlayerTechIdText,
    uint UpgradeId,
    string UpgradeIdText);

public sealed record TrainerSecretProtocolPresetInfo(
    string Name,
    IReadOnlyList<TrainerSecretProtocolPresetEntryInfo> Entries);

public sealed record TrainerPresetsResponse(
    IReadOnlyList<TrainerReinforcementPresetInfo> ReinforcementPresets,
    IReadOnlyList<TrainerSecretProtocolPresetInfo> SecretProtocolPresets);

public sealed record TrainerFeatureInfo(
    string Id,
    string DisplayName,
    TrainerFeatureType Type,
    bool? IsEnabled,    // 仅 toggle 类型有效
    string? Hotkey,
    string? ValueHint,
    bool RequiresParameters,
    string CapabilityState,
    string CapabilityReasonCode,
    string CapabilityReason);

public sealed record TrainerFeaturesResponse(
    IReadOnlyList<TrainerFeatureInfo> Features);

public sealed record TrainerActionRequest(
    uint? UnitId,
    int? Count,
    int? Rank,
    float? TargetHealth,
    uint? PlayerTechId,
    uint? UpgradeId,
    string? TemplateName,
    string? ModelPath,
    string? WeaponName);

public sealed record TrainerSelectedUnitResponse(
    uint UnitCode,
    string UnitCodeHex,
    int GameMode,
    string GameModeName);

public sealed record TrainerGameStateResponse(
    int GameMode,
    string GameModeName,
    bool IsInGame,
    TrainerWebStatusResponse? SessionStatus);

public sealed record TrainerTemplateModelReplacementRequest(
    string TemplateName,
    string NewModelPath);

public sealed record TrainerTemplateWeaponReplacementRequest(
    string TemplateName,
    string NewWeaponName);

public sealed record ReinforcementCatalogEntry(
    string Mod,
    string Faction,
    string CodeText,
    uint Code,
    string Name,
    string? SourceId);

public sealed record ReinforcementCatalogResponse(
    IReadOnlyList<ReinforcementCatalogEntry> Entries);

public sealed record SecretProtocolCatalogEntry(
    string Mod,
    string Faction,
    string Name,
    string PlayerTechIdText,
    string UpgradeIdText,
    uint PlayerTechId,
    uint UpgradeId,
    bool CanGrant);

public sealed record SecretProtocolCatalogResponse(
    IReadOnlyList<SecretProtocolCatalogEntry> Entries);

public sealed record TrainerQueueItemResult(
    int Index,
    string Status,
    string Message);

public sealed record TrainerWebQueueResult(
    bool Success,
    string Message,
    IReadOnlyList<TrainerQueueItemResult> Items);
