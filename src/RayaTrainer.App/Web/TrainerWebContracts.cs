using System.Text.Json.Serialization;
using RayaTrainer.Core.Features;

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
    string Message,
    string? ReasonCode = null);

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

public sealed record TrainerUnitUpgradeItem(
    uint Hash,
    string Name,
    string Description);

public sealed record TrainerUnitUpgradesResponse(
    uint UnitTypeId,
    string UnitTypeIdHex,
    string Message,
    IReadOnlyList<TrainerUnitUpgradeItem> Upgrades);

public sealed record TrainerGrantObjectUpgradeRequest(
    uint Hash);

public sealed record FeaturePresetsResponse(
    IReadOnlyList<FeaturePreset> Presets);

public sealed record FeaturePresetSaveRequest(
    string Name,
    FeatureStateSnapshot Snapshot);

// --- Product Control Plane (U4) ---
// Public, JSON-serializable projections of the internal Product Control session outcomes.
// The internal types (ProductControlOutcome<T> / ProductControlStatus / IProductControlSession)
// never appear here — every field is a primitive or an enum NAME string. The classification /
// label pair always comes from the shared ProductControlResultClassifier so the Web surface
// classifies identically to the Overlay/WPF surfaces; consumers read these fields directly and
// never re-combine booleans + reason strings into their own status.

/// <summary>
/// Shared structured status echoed on every product-control Web response. <see cref="Classification"/>
/// is the <c>ProductControlResultClassification</c> name and <see cref="Label"/> is the
/// novice-friendly label from <c>ProductControlResultClassifier.ToLabel</c>. <see cref="Detail"/>
/// carries the technical outcome detail (never a reassembled status sentence).
/// </summary>
public sealed record ProductControlStatusInfo(
    string Classification,
    string Label,
    string Detail);

/// <summary>Read-only match-context summary (Command 57 / QueryMatchContext).</summary>
public sealed record ProductControlContextResponse(
    ProductControlStatusInfo Status,
    bool Available,
    string Lifecycle,
    bool SinglePlayerProven,
    string RuntimeFlags,
    string ScopeAvailabilityMask,
    bool ActivePlayerCountKnown,
    uint ActivePlayerCount,
    ulong MapEpoch,
    ulong SnapshotGeneration);

/// <summary>
/// Submit-intent request. Web is a remote/backup surface; scope/binding/reapply and parameter
/// shape are resolved from the public generated catalog. <see cref="Amount"/> supplies the
/// single Integer slot for products that declare one and must be absent for zero-parameter products.
/// </summary>
public sealed record ProductControlSubmitRequest(
    string ProductId,
    long? Amount);

/// <summary>Submit-intent result (Command 58, classified together with the fetched result).</summary>
public sealed record ProductControlSubmitResponse(
    ProductControlStatusInfo Status,
    bool Accepted,
    ulong IntentId,
    string Acceptance,
    string ErrorCode);

/// <summary>Layered product result for one intent (Command 59 / GetProductResult).</summary>
public sealed record ProductControlResultResponse(
    ProductControlStatusInfo Status,
    ulong IntentId,
    string Availability,
    string Admission,
    string Execution,
    string Effect,
    string Compensation,
    string ErrorCode,
    string? ProductId,
    string Detail);

/// <summary>One row of the Agent Desired Intent registry (Command 60 / GetDesiredIntents).</summary>
public sealed record ProductControlDesiredItem(
    ulong IntentId,
    string ProductId,
    string BindingKind,
    string ScopeKind,
    string ReapplyPolicy,
    string DesiredState,
    ulong LastMapEpoch);

/// <summary>Paged Desired Intent registry projection.</summary>
public sealed record ProductControlDesiredResponse(
    ProductControlStatusInfo Status,
    bool Available,
    ulong PolicyRevision,
    uint TotalCount,
    uint Offset,
    uint Limit,
    IReadOnlyList<ProductControlDesiredItem> Items);

/// <summary>One typed parameter slot a product declares (name + <c>ScriptValueKind</c> name).</summary>
public sealed record ProductControlCatalogParameter(
    string Name,
    string Kind);

/// <summary>
/// One catalog product projected onto public JSON: its id, display name, kind, and the DECLARED
/// scope/binding/reapply plus typed parameter descriptors. The submit route derives its
/// <c>ContextBinding</c> from the same projection by id, so this list is the source of truth.
/// </summary>
public sealed record ProductControlCatalogProduct(
    string ProductId,
    string DisplayName,
    string Kind,
    string Scope,
    string Binding,
    string Reapply,
    IReadOnlyList<ProductControlCatalogParameter> Parameters);

/// <summary>Projection of the generated product catalog (GET /product-control/catalog).</summary>
public sealed record ProductControlCatalogResponse(
    IReadOnlyList<ProductControlCatalogProduct> Products);
