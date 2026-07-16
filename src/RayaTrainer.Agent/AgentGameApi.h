#pragma once

#include <cstdint>

#include "AgentProtocol.h"

namespace RayaTrainer::agent
{
enum class GameApiCallingConvention : uint8_t
{
    Cdecl = 0,
    Thiscall = 1,
    Stdcall = 2
};

enum class GameApiThreadRequirement : uint8_t
{
    GameThread = 0
};

enum class GameApiRiskLevel : uint8_t
{
    Low = 0,
    Medium = 1,
    High = 2
};

enum class GameApiSmokeStatus : uint8_t
{
    NotStarted = 0,
    Passed = 1,
    Failed = 2
};

enum class GameApiDispatchStatus : uint32_t
{
    Idle = 0,
    Pending = 1,
    Completed = 2,
    Disabled = 3,
    Failed = 4,
    TimedOut = 5,
    NoGameTick = 6,
    StaleRequest = 7,
    NoSelectedUnit = 8
};

struct GameApiFunctionSpec
{
    const char* Name;
    uint32_t Rva;
    GameApiCallingConvention CallingConvention;
    GameApiThreadRequirement ThreadRequirement;
    GameApiRiskLevel RiskLevel;
    bool EnabledByDefault;
    GameApiSmokeStatus SmokeStatus;
    const char* IdaPrototype;
    const char* ThisPointerSource;
    const char* SmokeProcedure;
};

const GameApiFunctionSpec* GetGameApiCatalog(uint32_t& count);

const GameApiFunctionSpec* FindGameApiSpec(const char* name);

bool IsGameApiInvocationAllowed(
    const GameApiFunctionSpec& spec,
    bool isGameThread,
    bool smokeVerified);

// --- Native agent catalog (runtime-injected game RVAs) ----------------------
//
// Replaces the compile-time 1.12 RVAs (kGameClientPointerRva, kGameApiCatalog[].Rva) with a
// per-profile table delivered by the host via SetNativeCatalog. Before delivery the DLL uses
// 1.12 defaults; after delivery a zero entry explicitly marks that capability unavailable.

// Initializes the runtime catalog to the 1.12 compile-time defaults. Called once at startup.
void InitializeNativeCatalog();

// Returns true after a structurally complete SetNativeCatalog delivery. Individual entries
// may be zero to mark unsupported functions.
bool HasNativeCatalog();

// Parses a SetNativeCatalog payload (uint32 count + count * uint32 rva). Returns Ok on a
// structurally complete, well-formed catalog; otherwise leaves the catalog untouched.
AgentStatusCode SetNativeCatalogFromPayload(const unsigned char* payload, uint32_t length);

// Resolves a catalog entry RVA. Always returns a value: the runtime catalog entry when
// present, otherwise the 1.12 compile-time default.
uint32_t ResolveNativeCatalogRva(NativeCatalogEntry entry);
uint32_t ResolveCurrentPlayerPointer();
void ResetNativeGameApiRuntimeState();
bool IsAttackSpeedObject(uint32_t gameObject);
bool IsAttackRangeObject(uint32_t gameObject);
void ClearWeaponObjectFlagsForRegisteredObject(uint32_t gameObject);

// Reads the current GameClient game-mode field using the runtime native catalog.
bool TryGetGameMode(int32_t& gameMode);

#include "Generated/AgentGameApi.Declarations.generated.h"
}

#include "Generated/AgentGameApi.NativeRouting.generated.h"
