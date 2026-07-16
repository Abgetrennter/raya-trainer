#pragma once

#include <cstdint>

#include "Generated/AgentProtocol.GameApi.generated.h"

namespace RayaTrainer::agent
{
inline constexpr uint32_t kAgentMagic = 0x41594152u;

// Bumped 1 -> 2 to add SetNativeCatalog, which delivers per-profile game RVAs to the DLL
// so it no longer depends on compile-time 1.12 addresses. Host and DLL must agree exactly.
// Bumped 2 -> 3 to add GetMismatchDiagnostics, which lets the DLL report the actual bytes
// it observed at a failing hook site so the host can emit a PatchMismatchReport instead of
// only seeing a bare PatchMismatch status code. Host and DLL must agree exactly.
// Bumped 3 -> 4 to add ScanSignatures and GetGameMode, which keep signature resolution and
// semantic game-state reads inside the DLL. Host and DLL must agree exactly.
// Bumped 4 -> 5 to add ExpandProductionQueue.
// Bumped 5 -> 6 to add the build fingerprint used for safe host reconnect.
// Bumped 6 -> 7 to add TeleportSelectedUnitsToMouse.
// Bumped 7 -> 8 for native hook ids, DLL-internal feature state, and removal of the
// remote bootstrap address contract.
// Bumped 8 -> 9 for the Ra3Trainer -> RayaTrainer rename. Magic changed from "RA3T"
// (0x54334152) to "RAYA" (0x41594152); fingerprint rotated to match. Legacy Agent pipe
// (Ra3Trainer.Agent.<pid>) is detected and refused at injection time.
// Fingerprint low 16 bits bumped 1 -> 2 to force reconnect rejection after selected-weapon-effects
// registry changes (no wire-protocol change). Host and DLL must agree exactly.
// Fingerprint low 16 bits bumped 2 -> 3 for the selected-unit auto-acquire range hook;
// an already injected range-only Agent must not be reused. Wire protocol remains v9.
// Fingerprint low 16 bits bumped 3 -> 4 for idle acquisition and final maximum-range hooks;
// an already injected Agent without these hooks must not be reused. Wire protocol remains v9.
// Fingerprint low 16 bits bumped 4 -> 5 for the post-compare idle branch and turret target-angle hook;
// an already injected Agent with the ineffective seams must not be reused. Wire protocol remains v9.
// Fingerprint low 16 bits bumped 5 -> 6 for shared turret-angle and full-circle aim-deflection hooks;
// an already injected chooser-only Agent must not be reused. Wire protocol remains v9.
// v10: protocol bumped 9 -> 10 for object-level unit upgrade grant. Adds commands
// GetSelectedUnitUpgrades (46) / GrantObjectUpgradeOnSelectedSameType (47), Native catalog
// entry UpgradeTemplateTypeOffset (EntryCount 40 -> 41), and widens AgentGameThreadResult.Values
// from 8 to 24 slots. An already-injected v9 Agent must not be reused: the catalog count,
// command set, and result layout are incompatible. Fingerprint low 32 bits reset to
// (Version=10 << 16) | 1 = 0x000A0001.
// v10 fingerprint low 16 bits bumped 1 -> 2 for per-GameObject weapon flags and the
// GameLogic_RegisterObject initializer hook. The wire protocol remains v10.
// v10 fingerprint low 16 bits bumped 2 -> 3 for the profile-aware StructureUnpackUpdate
// fast-build field. An older Agent writes the wrong Uprising module field.
inline constexpr uint16_t kAgentProtocolVersion = 10;
inline constexpr uint64_t kAgentBuildFingerprint = 0x52415941000A0003ull;
inline constexpr uint32_t kNativeRuntimeCapabilities = 0x00000007u;
inline constexpr uint32_t kMaxPayloadLength = 64u * 1024u;

enum class AgentCommand : uint16_t
{
    Ping = 1,
    GetStatus = 2,
    InstallPatches = 3,
    RestorePatches = 4,
    SetToggle = 5,
    TriggerAction = 6,
    WriteResourceValues = 7,
    ReadSelectedUnitCode = 8,
    ReadMemory = 9,
    SmokeGetThingClass = 10,
    LevelUpSelected = 11,
    CreateUnit = 12,
    KillUnit = 13,
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
    SetNativeCatalog = 33,
    GetMismatchDiagnostics = 34,
    ScanSignatures = 35,
    GetGameMode = 36,
    ExpandProductionQueue = 37,
    TeleportSelectedUnitsToMouse = 38,
    SetSelectedUnitSpeed = 39,
    CaptureSelectedUnits = 40,
    SetSelectedUnitAmmo = 41,
    ToggleSelectedAttackSpeed = 42,
    ToggleSelectedAttackRange = 43,
    ClearSelectedAttackSpeedEffects = 44,
    ClearSelectedAttackRangeEffects = 45,
    GetSelectedUnitUpgrades = 46,
    GrantObjectUpgradeOnSelectedSameType = 47
};

// Native agent catalog entry order. This is the host<->DLL contract: the C# host serializes
// NativeAgentCatalog.EntryNames in exactly this order, and the DLL stores entries by index.
// Keep in sync with RayaTrainer.Core.Agent.NativeAgentCatalog.EntryNames.
enum class NativeCatalogEntry : uint32_t
{
    GameClientPointer = 0,
    GetThingClass = 1,
    LevelUpSelected = 2,
    CreateUnit = 3,
    KillUnit = 4,
    PlayerManager = 5,
    GetCurrentPlayer = 6,
    ThingTemplateStore = 7,
    SelectedUnitCode = 8,
    ScienceStore = 9,
    ScienceStoreFindScience = 10,
    ScienceStoreFindUpgrade = 11,
    ScienceManagerFindScience = 12,
    PlayerGetUpgradeStore = 13,
    ScienceManagerHasScience = 14,
    PlayerGrantScience = 15,
    PlayerScienceManagerOffset = 16,
    SelectionManager = 17,
    SelectionListHeadOffset = 18,
    SelectionCountOffset = 19,
    MouseWorldPointer = 20,
    MouseWorldToMapPosition = 21,
    ObjectSetPosition = 22,
    MovementModuleOffset = 23,
    MovementContainerOffset = 24,
    ObjectOwnerOffset = 25,
    BodyOffset = 26,
    VeterancyOffset = 27,
    WeaponContainerOffset = 28,
    UnitStatePrimaryOffset = 29,
    UnitStateSecondaryOffset = 30,
    OneHitDamageDeltaMode = 31,
    OneHitCaller1 = 32,
    OneHitCaller2 = 33,
    OneHitCaller3 = 34,
    DestroySelectionListHeadOffset = 35,
    ProductionModulesOffset = 36,
    LocalContextSiblingOffset = 37,
    RestoreOreCapacityMode = 38,
    GameObjectAddUpgrade = 39,
    UpgradeTemplateTypeOffset = 40,
    EntryCount = 41
};

inline constexpr uint32_t kNativeCatalogEntryCount =
    static_cast<uint32_t>(NativeCatalogEntry::EntryCount);

enum class AgentMemoryAddressMode : uint32_t
{
    Direct = 0,
    DereferenceUInt32 = 1
};

enum class AgentStatusCode : uint16_t
{
    Ok = 0,
    Pending = 1,
    Consumed = 2,
    TimedOut = 3,
    VersionMismatch = 4,
    PatchMismatch = 5,
    InvalidCommand = 6,
    InternalError = 7
};

#pragma pack(push, 1)
struct AgentProtocolHeader
{
    uint32_t Magic;
    uint16_t Version;
    uint16_t Command;
    uint32_t SequenceId;
    uint32_t PayloadLength;
};

struct AgentPingPayload
{
    uint16_t StatusCode;
    uint16_t AgentVersion;
    uint32_t ProcessId;
    uint32_t ModuleBase;
    uint32_t NativeRuntimeCapabilities;
    uint64_t BuildFingerprint;
};

struct AgentStatusPayload
{
    uint16_t StatusCode;
    uint16_t AgentVersion;
    uint32_t ProcessId;
    uint32_t ModuleBase;
    uint32_t InstalledHookCount;
    uint32_t NativeRuntimeCapabilities;
    uint32_t GameThreadTick;
    uint64_t BuildFingerprint;
};

struct AgentCommandResultPayload
{
    uint16_t StatusCode;
    uint16_t AgentVersion;
    uint32_t InstalledHookCount;
};

struct AgentMemoryReadRequest
{
    uint32_t Address;
    uint32_t ByteCount;
};

struct AgentMemoryReadPayloadHeader
{
    uint16_t StatusCode;
    uint16_t AgentVersion;
    uint32_t Address;
    uint32_t ByteCount;
};

// Variable-length payload for GetMismatchDiagnostics. The trailing bytes are laid out as
// [expected][actual][dump] with the three lengths declared in the header, so the host can
// reconstruct the per-hook diagnostic the same way PatchMismatchReportWriter does for the
// external-memory backend. StatusCode is Ok when a captured mismatch is available, or
// InvalidCommand when no mismatch has been recorded since the last install.
struct AgentMismatchDiagnosticsPayloadHeader
{
    uint16_t StatusCode;
    uint16_t AgentVersion;
    uint32_t HookAddress;
    uint32_t ExpectedLength;
    uint32_t ActualLength;
    uint32_t DumpLength;
};

// Variable-length ScanSignatures response. Each entry header is followed immediately by
// NameLength bytes of ASCII/UTF-8 symbolic name data. Address is zero when the signature did
// not match uniquely; MatchedCount lets the host reject an incomplete catalog at a glance.
struct AgentSignatureScanPayloadHeader
{
    uint16_t StatusCode;
    uint16_t AgentVersion;
    uint32_t EntryCount;
    uint32_t MatchedCount;
};

struct AgentSignatureScanEntryHeader
{
    uint32_t Address;
    uint16_t NameLength;
    uint16_t Reserved;
};

struct AgentGameModePayload
{
    uint16_t StatusCode;
    uint16_t AgentVersion;
    int32_t GameMode;
};

#pragma pack(pop)

static_assert(sizeof(AgentProtocolHeader) == 16);
static_assert(sizeof(AgentPingPayload) == 24);
static_assert(sizeof(AgentStatusPayload) == 32);
static_assert(sizeof(AgentCommandResultPayload) == 8);
static_assert(sizeof(AgentMemoryReadRequest) == 8);
static_assert(sizeof(AgentMemoryReadPayloadHeader) == 12);
static_assert(sizeof(AgentMismatchDiagnosticsPayloadHeader) == 20);
static_assert(sizeof(AgentSignatureScanPayloadHeader) == 12);
static_assert(sizeof(AgentSignatureScanEntryHeader) == 8);
static_assert(sizeof(AgentGameModePayload) == 8);
}
