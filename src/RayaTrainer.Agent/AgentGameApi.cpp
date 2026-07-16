#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstring>

#include "AgentGameApi.h"
#include "AgentGameThreadDispatcher.h"
#include "AgentNativeHooks.h"

namespace RayaTrainer::agent
{
namespace
{
// Compile-time 1.12 defaults. The runtime native catalog (g_nativeCatalogRvas) starts as a
// copy of these and is overwritten by SetNativeCatalog when the host delivers a per-profile
// catalog. Kept as the fallback so 1.12 behavior is preserved without a catalog delivery.
constexpr uint32_t kGameClientPointerRva = 0x8D8CE4u;
constexpr uint32_t kGameModeFieldOffset = 0x148u;
constexpr int32_t kGameModeShell = 9;
constexpr uint32_t kMaxGameApiSmokeTimeoutMilliseconds = 5000u;
constexpr uint32_t kGameLogicFirstObjectOffset = 0xB4u;
constexpr uint32_t kGameObjectNextOffset = 0x7Cu;
constexpr uint32_t kMaximumGameObjectWalkCount = 65536u;
constexpr uint16_t kAttackSpeedObjectFlag = 1u << 0;
constexpr uint16_t kAttackRangeObjectFlag = 1u << 1;
LONG g_nextGameApiRequestId = 0;
volatile LONG g_weaponObjectFlagsInitialized = 0;

constexpr GameApiFunctionSpec kGameApiCatalog[] = {
    {
        "GetThingClass",
        0x3E4230u,
        GameApiCallingConvention::Thiscall,
        GameApiThreadRequirement::GameThread,
        GameApiRiskLevel::Low,
        false,
        GameApiSmokeStatus::NotStarted,
        "int __thiscall(char *this, char)",
        "ecx source must be confirmed before enabling; IDA shows object/registry-relative access at this+40.",
        "Resolve a known unit type through a verified game-thread caller without mutating world state."
    },
    {
        "LevelUpSelected",
        0x35C200u,
        GameApiCallingConvention::Thiscall,
        GameApiThreadRequirement::GameThread,
        GameApiRiskLevel::Medium,
        false,
        GameApiSmokeStatus::NotStarted,
        "char __thiscall(void *this, int, int, int)",
        "this pointer appears to be a unit/veterancy context; selected-object source is not yet proven.",
        "Run only after selected-unit context is proven; compare against existing action dispatch upgrade behavior."
    },
    {
        "CreateUnit",
        0x205240u,
        GameApiCallingConvention::Cdecl,
        GameApiThreadRequirement::GameThread,
        GameApiRiskLevel::High,
        false,
        GameApiSmokeStatus::NotStarted,
        "int __cdecl(int, int, int, int)",
        "caller must provide ThingClass/object template, owner/player and position context.",
        "Keep disabled until GetThingClass and owner/position sources are smoke-tested."
    },
    {
        "KillUnit",
        0x39EA50u,
        GameApiCallingConvention::Thiscall,
        GameApiThreadRequirement::GameThread,
        GameApiRiskLevel::High,
        false,
        GameApiSmokeStatus::NotStarted,
        "int __thiscall(_DWORD *this, int, int, int)",
        "selected object pointer source and destruction side effects are not yet proven.",
        "Do not smoke before non-destructive read and upgrade wrappers are stable."
    }
};

// Default native catalog RVAs (1.12). Order matches NativeCatalogEntry and the C#
// NativeAgentCatalog.EntryNames. g_nativeCatalogRvas is initialized from this on startup
// and overwritten by SetNativeCatalog.
constexpr uint32_t kDefaultNativeCatalogRvas[kNativeCatalogEntryCount] = {
    kGameClientPointerRva,          // GameClientPointer
    0x3E4230u,                      // GetThingClass
    0x35C200u,                      // LevelUpSelected
    0x205240u,                      // CreateUnit
    0x39EA50u,                      // KillUnit
    0x8E8C9Cu,                      // PlayerManager
    0x4393E0u,                      // GetCurrentPlayer
    0x8E6C58u,                      // ThingTemplateStore
    0x8E9838u,                      // SelectedUnitCode
    0x8DBBF0u,                      // ScienceStore
    0x1456F0u,                      // ScienceStoreFindScience
    0x147260u,                      // ScienceStoreFindUpgrade
    0x43C300u,                      // ScienceManagerFindScience
    0x44A2D0u,                      // PlayerGetUpgradeStore
    0x44D7C0u,                      // ScienceManagerHasScience
    0x454300u,                      // PlayerGrantScience
    0x1320u,                        // PlayerScienceManagerOffset
    0x8DB73Cu,                      // SelectionManager
    0x50u,                          // SelectionListHeadOffset
    0x5Cu,                          // SelectionCountOffset
    0x8DAEFCu,                      // MouseWorldPointer
    0x1ED4A0u,                      // MouseWorldToMapPosition
    0x3E3D00u,                      // ObjectSetPosition
    0x374u,                         // MovementModuleOffset
    0x200u,                         // MovementContainerOffset
    0x418u,                         // ObjectOwnerOffset
    0x33Cu,                         // BodyOffset
    0x3CCu,                         // VeterancyOffset
    0x37Cu,                         // WeaponContainerOffset
    0xBCu,                          // UnitStatePrimaryOffset
    0xC8u,                          // UnitStateSecondaryOffset
    0x0u,                           // OneHitDamageDeltaMode
    0x3AD79Eu,                      // OneHitCaller1
    0x3ADEE2u,                      // OneHitCaller2
    0x38E651u,                      // OneHitCaller3
    0x54u,                          // DestroySelectionListHeadOffset
    0x310u,                         // ProductionModulesOffset
    0x1360u,                        // LocalContextSiblingOffset
    1u,                             // RestoreOreCapacityMode (1=EAX+8, 2=ECX+0x28)
    0x379650u,                      // GameObjectAddUpgrade (RVA = IDA VA 0x779650 - base 0x400000)
    0x24u,                          // UpgradeTemplateTypeOffset (Type field within UpgradeTemplateDefinition, via [tpl+0xC])
};

uint32_t g_nativeCatalogRvas[kNativeCatalogEntryCount] = {};
bool g_nativeCatalogReady = false;



bool TryReadUInt32(const unsigned char* payload, uint32_t length, uint32_t& offset, uint32_t& value)
{
    if (offset > length || length - offset < sizeof(uint32_t))
    {
        return false;
    }

    std::memcpy(&value, payload + offset, sizeof(uint32_t));
    offset += sizeof(uint32_t);
    return true;
}

bool SafeReadU32(uint32_t address, uint32_t& value)
{
    __try
    {
        std::memcpy(&value, reinterpret_cast<const void*>(static_cast<uintptr_t>(address)), sizeof(value));
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

bool SafeReadU8(uint32_t address, uint8_t& value)
{
    __try
    {
        value = *reinterpret_cast<const uint8_t*>(static_cast<uintptr_t>(address));
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

bool SafeReadU16(uint32_t address, uint16_t& value)
{
    __try
    {
        std::memcpy(&value, reinterpret_cast<const void*>(static_cast<uintptr_t>(address)), sizeof(value));
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

bool SafeWriteU8(uint32_t address, uint8_t value)
{
    __try
    {
        *reinterpret_cast<uint8_t*>(static_cast<uintptr_t>(address)) = value;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

bool SafeWriteU16(uint32_t address, uint16_t value)
{
    __try
    {
        std::memcpy(reinterpret_cast<void*>(static_cast<uintptr_t>(address)), &value, sizeof(value));
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

uint32_t ResolveStructureOffset(NativeCatalogEntry entry)
{
    return ResolveNativeCatalogRva(entry);
}

bool SafeWriteU32(uint32_t address, uint32_t value)
{
    __try
    {
        std::memcpy(reinterpret_cast<void*>(static_cast<uintptr_t>(address)), &value, sizeof(value));
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

bool SafeReadStructureU32(
    uint32_t base,
    NativeCatalogEntry entry,
    uint32_t& value)
{
    const auto offset = ResolveStructureOffset(entry);
    return base != 0 && offset != 0 && SafeReadU32(base + offset, value);
}

bool SafeWriteStructureU32(
    uint32_t base,
    NativeCatalogEntry entry,
    uint32_t value)
{
    const auto offset = ResolveStructureOffset(entry);
    return base != 0 && offset != 0 && SafeWriteU32(base + offset, value);
}

uint32_t FindThingClassOnGameThread(uint32_t unitTypeId);

void GetCurrentPlayerOnGameThread(const uint32_t*, uint32_t* results)
{
    results[0] = 0;
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto managerRva = ResolveNativeCatalogRva(NativeCatalogEntry::PlayerManager);
    const auto getCurrentPlayerRva = ResolveNativeCatalogRva(NativeCatalogEntry::GetCurrentPlayer);
    if (moduleBase == 0 || managerRva == 0 || getCurrentPlayerRva == 0)
    {
        return;
    }

    uint32_t manager = 0;
    if (!SafeReadU32(static_cast<uint32_t>(moduleBase + managerRva), manager) || manager == 0)
    {
        return;
    }

    using GetCurrentPlayerFunction = uint32_t(__thiscall*)(void*);
    const auto function = reinterpret_cast<GetCurrentPlayerFunction>(moduleBase + getCurrentPlayerRva);
    results[0] = function(reinterpret_cast<void*>(static_cast<uintptr_t>(manager)));
}

uint32_t GetCurrentPlayerPointer()
{
    uint32_t results[8] = {};
    GetCurrentPlayerOnGameThread(nullptr, results);
    return results[0];
}

uint32_t GetMutationOwnerPlayer()
{
    // Ownership-changing legacy actions used the player object captured by the PlayerID
    // hook. GetCurrentPlayer may substitute an alternate context for special game states,
    // which is valid for queries but not for owner fields or CreateUnit arguments.
    const auto capturedPlayer = ReadCapturedPlayerObject();
    return capturedPlayer != 0 ? capturedPlayer : GetCurrentPlayerPointer();
}

uint32_t ResolveProfileGlobal(NativeCatalogEntry entry)
{
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto rva = ResolveNativeCatalogRva(entry);
    if (moduleBase == 0 || rva == 0)
    {
        return 0;
    }

    uint32_t value = 0;
    return SafeReadU32(static_cast<uint32_t>(moduleBase + rva), value) ? value : 0;
}

uint32_t CallContextHashCallerClean(uint32_t functionAddress, uint32_t context, uint32_t hash)
{
    uint32_t result = 0;
#if defined(_M_IX86)
    __asm
    {
        mov ecx, context
        push hash
        mov eax, functionAddress
        call eax
        add esp, 4
        mov result, eax
    }
#endif
    return result;
}

uint32_t LookupStoreEntry(NativeCatalogEntry functionEntry, uint32_t hash, bool& invoked)
{
    invoked = false;
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto store = ResolveProfileGlobal(NativeCatalogEntry::ScienceStore);
    const auto functionRva = ResolveNativeCatalogRva(functionEntry);
    if (moduleBase == 0 || store == 0 || functionRva == 0 || hash == 0)
    {
        return 0;
    }

    const uint32_t key[2] = { 0, hash };
    using StoreFindFunction = uint32_t(__thiscall*)(void*, const uint32_t*);
    const auto function = reinterpret_cast<StoreFindFunction>(moduleBase + functionRva);
    invoked = true;
    return function(reinterpret_cast<void*>(static_cast<uintptr_t>(store)), key);
}

void LookupScienceByHashOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[0] = 0;
    results[1] = 0;
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto player = GetCurrentPlayerPointer();
    const auto scienceManagerOffset = ResolveNativeCatalogRva(NativeCatalogEntry::PlayerScienceManagerOffset);
    const auto functionRva = ResolveNativeCatalogRva(NativeCatalogEntry::ScienceManagerFindScience);
    uint32_t scienceManager = 0;
    if (moduleBase == 0 || player == 0 || scienceManagerOffset == 0 || functionRva == 0 ||
        !SafeReadU32(player + scienceManagerOffset, scienceManager) || scienceManager == 0)
    {
        return;
    }

    results[0] = CallContextHashCallerClean(
        static_cast<uint32_t>(moduleBase + functionRva),
        scienceManager,
        arguments[0]);
    results[1] = 1;
}

void LookupTemplateByHashOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    bool invoked = false;
    results[0] = LookupStoreEntry(NativeCatalogEntry::ScienceStoreFindScience, arguments[0], invoked);
    results[1] = invoked ? 1u : 0u;
}

void LookupUpgradeByHashOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    bool invoked = false;
    results[0] = LookupStoreEntry(NativeCatalogEntry::ScienceStoreFindUpgrade, arguments[0], invoked);
    results[1] = invoked ? 1u : 0u;
}

void HasUpgradeOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[0] = 0;
    results[1] = 0;
    bool invoked = false;
    const auto definition = LookupStoreEntry(
        NativeCatalogEntry::ScienceStoreFindScience,
        arguments[0],
        invoked);
    if (!invoked)
    {
        return;
    }

    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto player = GetCurrentPlayerPointer();
    const auto functionRva = ResolveNativeCatalogRva(NativeCatalogEntry::PlayerGetUpgradeStore);
    if (moduleBase == 0 || player == 0 || functionRva == 0)
    {
        return;
    }

    results[1] = 1;
    if (definition == 0)
    {
        return;
    }

    using PlayerGetUpgradeStoreFunction = uint32_t(__thiscall*)(void*, uint32_t);
    const auto function = reinterpret_cast<PlayerGetUpgradeStoreFunction>(moduleBase + functionRva);
    results[0] = function(
        reinterpret_cast<void*>(static_cast<uintptr_t>(player)), definition) != 0 ? 1u : 0u;
}

bool GrantScienceToCurrentPlayer(uint32_t hash)
{
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto player = GetCurrentPlayerPointer();
    const auto managerOffset = ResolveNativeCatalogRva(NativeCatalogEntry::PlayerScienceManagerOffset);
    const auto findRva = ResolveNativeCatalogRva(NativeCatalogEntry::ScienceManagerFindScience);
    const auto grantRva = ResolveNativeCatalogRva(NativeCatalogEntry::PlayerGrantScience);
    uint32_t manager = 0;
    if (moduleBase == 0 || player == 0 || managerOffset == 0 || findRva == 0 || grantRva == 0 ||
        !SafeReadU32(player + managerOffset, manager) || manager == 0)
    {
        return false;
    }
    const auto science = CallContextHashCallerClean(
        static_cast<uint32_t>(moduleBase + findRva), manager, hash);
    if (science == 0)
    {
        return false;
    }
    using GrantScienceFunction = void(__thiscall*)(void*, uint32_t);
    const auto grant = reinterpret_cast<GrantScienceFunction>(moduleBase + grantRva);
    grant(reinterpret_cast<void*>(static_cast<uintptr_t>(manager)), science);
    return true;
}

bool GrantUpgradeToContext(uint32_t context, uint32_t definition, uint32_t flag)
{
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto grantRva = ResolveNativeCatalogRva(NativeCatalogEntry::ScienceManagerHasScience);
    if (moduleBase == 0 || context == 0 || definition == 0 || grantRva == 0)
    {
        return false;
    }
    using GrantUpgradeFunction = void(__thiscall*)(void*, uint32_t, uint32_t, uint32_t);
    const auto grant = reinterpret_cast<GrantUpgradeFunction>(moduleBase + grantRva);
    grant(reinterpret_cast<void*>(static_cast<uintptr_t>(context)), definition, 2, flag);
    return true;
}

void GrantPlayerTechOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[0] = GrantScienceToCurrentPlayer(arguments[0]) ? 1u : 0u;
}

void GrantUpgradeToPlayerOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    bool invoked = false;
    const auto definition = LookupStoreEntry(
        NativeCatalogEntry::ScienceStoreFindScience, arguments[0], invoked);
    const auto player = GetCurrentPlayerPointer();
    results[0] = invoked && GrantUpgradeToContext(player, definition, 0) ? 1u : 0u;
}

void GrantSecretProtocolOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[0] = 0;
    if (arguments[0] != 0 && !GrantScienceToCurrentPlayer(arguments[0]))
    {
        return;
    }
    bool invoked = false;
    const auto definition = LookupStoreEntry(
        NativeCatalogEntry::ScienceStoreFindScience, arguments[1], invoked);
    const auto player = GetCurrentPlayerPointer();
    results[0] = invoked && GrantUpgradeToContext(player, definition, 1) ? 1u : 0u;
}

// Selection nodes store Drawable*. The corresponding GameObject* is reached via
// Drawable+0x138. Keep both pointers explicit because hook-side identity checks
// must use the same GameObject* observed by the weapon subsystem.
using SelectedObjectVisitor = uint32_t(*)(uint32_t selectedDrawable, uint32_t gameObject, const uint32_t* arguments);

bool VisitSelectedObjects(
    SelectedObjectVisitor visitor,
    const uint32_t* arguments,
    uint32_t& visitedCount)
{
    visitedCount = 0;
    const auto selectionManager = ResolveProfileGlobal(NativeCatalogEntry::SelectionManager);
    const auto headOffset = ResolveNativeCatalogRva(NativeCatalogEntry::SelectionListHeadOffset);
    const auto countOffset = ResolveNativeCatalogRva(NativeCatalogEntry::SelectionCountOffset);
    uint32_t node = 0;
    uint32_t count = 0;
    if (selectionManager == 0 || headOffset == 0 || countOffset == 0 ||
        !SafeReadU32(selectionManager + headOffset, node) ||
        !SafeReadU32(selectionManager + countOffset, count) || count == 0 || count > 4096)
    {
        return false;
    }

    for (uint32_t index = 0; index < count && node != 0; ++index)
    {
        uint32_t selectedDrawable = 0;
        uint32_t gameObject = 0;
        if (SafeReadU32(node + 0x08u, selectedDrawable) && selectedDrawable != 0 &&
            SafeReadU32(selectedDrawable + 0x138u, gameObject) && gameObject != 0)
        {
            visitedCount += visitor(selectedDrawable, gameObject, arguments);
        }

        uint32_t next = 0;
        if (!SafeReadU32(node, next))
        {
            break;
        }
        node = next;
    }
    return true;
}

// RA3 GameObject leaves an aligned two-byte padding word unused at +0x4DE;
// Uprising moves the same word to +0x4EE as the object grows by 0x10 bytes.
// ObjectOwnerOffset is already profile-versioned, so accept only the two layouts
// proven in all four supported IDBs and fail closed for any future layout.
uint32_t WeaponObjectFlagsOffset()
{
    if (!HasNativeCatalog())
    {
        return 0;
    }

    switch (ResolveNativeCatalogRva(NativeCatalogEntry::ObjectOwnerOffset))
    {
    case 0x418u:
        return 0x4DEu;
    case 0x428u:
        return 0x4EEu;
    default:
        return 0;
    }
}

bool AreWeaponObjectFlagsInitialized()
{
    return InterlockedCompareExchange(&g_weaponObjectFlagsInitialized, 0, 0) != 0;
}

bool ReadWeaponObjectFlags(uint32_t gameObject, uint16_t& flags)
{
    const auto offset = WeaponObjectFlagsOffset();
    return gameObject != 0 && offset != 0 && SafeReadU16(gameObject + offset, flags);
}

bool WriteWeaponObjectFlags(uint32_t gameObject, uint16_t flags)
{
    const auto offset = WeaponObjectFlagsOffset();
    return gameObject != 0 && offset != 0 && SafeWriteU16(gameObject + offset, flags);
}

// Walks GameLogic::firstObject (+0xB4) through GameObject::nextObject (+0x7C).
// GameLogic_RegisterObject appends to this null-terminated intrusive list. The
// hard cap prevents corrupted list state from turning a toggle into an infinite loop.
bool UpdateAllWeaponObjectFlags(bool initialize, uint16_t clearMask, uint32_t& visitedCount)
{
    visitedCount = 0;
    const auto gameLogic = ResolveProfileGlobal(NativeCatalogEntry::GameClientPointer);
    if (gameLogic == 0 || WeaponObjectFlagsOffset() == 0)
    {
        return false;
    }

    uint32_t gameObject = 0;
    if (!SafeReadU32(gameLogic + kGameLogicFirstObjectOffset, gameObject))
    {
        return false;
    }

    while (gameObject != 0 && visitedCount < kMaximumGameObjectWalkCount)
    {
        uint32_t nextObject = 0;
        uint16_t flags = 0;
        if (!SafeReadU32(gameObject + kGameObjectNextOffset, nextObject) ||
            (!initialize && !ReadWeaponObjectFlags(gameObject, flags)) ||
            !WriteWeaponObjectFlags(
                gameObject,
                initialize ? 0u : static_cast<uint16_t>(flags & ~clearMask)))
        {
            return false;
        }

        gameObject = nextObject;
        ++visitedCount;
    }

    return gameObject == 0;
}

bool EnsureWeaponObjectFlagsInitialized()
{
    if (AreWeaponObjectFlagsInitialized())
    {
        return true;
    }

    uint32_t visitedCount = 0;
    if (!UpdateAllWeaponObjectFlags(true, 0, visitedCount))
    {
        return false;
    }

    InterlockedExchange(&g_weaponObjectFlagsInitialized, 1);
    return true;
}

bool ClearWeaponObjectFlagFromAllObjects(uint16_t clearMask, uint32_t& visitedCount)
{
    if (!EnsureWeaponObjectFlagsInitialized())
    {
        visitedCount = 0;
        return false;
    }

    return UpdateAllWeaponObjectFlags(false, clearMask, visitedCount);
}

// Thread-local selection buffers keep the two-pass smart-toggle decision atomic
// with respect to the chosen object set. They are scratch storage, not ownership state.
static thread_local uint32_t g_tempObjectBuffer[4096];
static thread_local uint32_t g_tempObjectCount;
static thread_local uint16_t g_tempObjectFlags[4096];

static uint32_t CollectWeaponObjectsVisitor(
    uint32_t /*selectedDrawable*/,
    uint32_t gameObject,
    const uint32_t*)
{
    if (gameObject != 0 && g_tempObjectCount < 4096)
    {
        g_tempObjectBuffer[g_tempObjectCount++] = gameObject;
        return 1;
    }
    return 0;
}

uint32_t SetSelectedStatusBitVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    const auto domain = arguments[0];
    const auto bitIndex = arguments[1];
    const auto enabled = arguments[2];
    const auto baseOffset = domain == 0 ? 0x80u : 0xA0u;
    const auto address = weaponStoreUser + baseOffset + ((bitIndex >> 5u) * sizeof(uint32_t));
    uint32_t value = 0;
    if (!SafeReadU32(address, value))
    {
        return 0;
    }
    const auto mask = 1u << (bitIndex & 31u);
    return SafeWriteU32(address, enabled != 0 ? value | mask : value & ~mask) ? 1u : 0u;
}

void SetSelectedStatusBitOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[1] = VisitSelectedObjects(SetSelectedStatusBitVisitor, arguments, results[0]) ? 1u : 0u;
}

uint32_t SetSelectedUnitHealthVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    uint32_t body = 0;
    const auto bodyOffset = ResolveStructureOffset(NativeCatalogEntry::BodyOffset);
    if (bodyOffset == 0 || !SafeReadU32(weaponStoreUser + bodyOffset, body) || body == 0)
    {
        return 0;
    }

    switch (arguments[0])
    {
    case 1:
        if (!SafeWriteU32(body + 0x04u, arguments[1])) return 0;
        return arguments[2] == 0 || SafeWriteU32(body + 0x0Cu, arguments[2]) ? 1u : 0u;
    case 2:
        return SafeWriteU32(body + 0x04u, 0x497423F0u) &&
            SafeWriteU32(body + 0x0Cu, 0x497423F0u) ? 1u : 0u;
    case 3:
        return SafeWriteU32(body + 0x04u, 0x3F800000u) ? 1u : 0u;
    case 4:
    {
        uint32_t original = 0;
        return SafeReadU32(body + 0x10u, original) &&
            SafeWriteU32(body + 0x04u, original) &&
            SafeWriteU32(body + 0x0Cu, original) ? 1u : 0u;
    }
    default:
        return 0;
    }
}

void SetSelectedUnitHealthOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[1] = VisitSelectedObjects(SetSelectedUnitHealthVisitor, arguments, results[0]) ? 1u : 0u;
}

bool IsProductionUpdateName(uint32_t address)
{
    static constexpr char kName[] = "ProductionUpdate";
    __try
    {
        return std::memcmp(reinterpret_cast<const void*>(static_cast<uintptr_t>(address)), kName, sizeof(kName)) == 0;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

uint32_t ExpandProductionQueueVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    uint32_t modules = 0;
    if (!SafeReadStructureU32(weaponStoreUser, NativeCatalogEntry::ProductionModulesOffset, modules) || modules == 0)
    {
        return 0;
    }

    uint32_t changed = 0;
    for (uint32_t index = 0; index < 4096; ++index)
    {
        uint32_t module = 0;
        if (!SafeReadU32(modules + index * sizeof(uint32_t), module) || module == 0)
        {
            break;
        }
        uint32_t vtable = 0;
        uint32_t nameFunctionAddress = 0;
        if (!SafeReadU32(module, vtable) || vtable == 0 ||
            !SafeReadU32(vtable + 0x08u, nameFunctionAddress) || nameFunctionAddress == 0)
        {
            continue;
        }

        using GetModuleNameFunction = uint32_t(__thiscall*)(void*);
        const auto getName = reinterpret_cast<GetModuleNameFunction>(static_cast<uintptr_t>(nameFunctionAddress));
        const auto name = getName(reinterpret_cast<void*>(static_cast<uintptr_t>(module)));
        if (!IsProductionUpdateName(name))
        {
            continue;
        }

        uint32_t moduleData = 0;
        if (SafeReadU32(module + 0x04u, moduleData) && moduleData != 0 &&
            SafeWriteU32(moduleData + 0x08u, arguments[0]))
        {
            ++changed;
        }
    }
    return changed;
}

void ExpandProductionQueueOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[1] = VisitSelectedObjects(ExpandProductionQueueVisitor, arguments, results[0]) ? 1u : 0u;
}

struct NativeVector3
{
    float X = 0;
    float Y = 0;
    float Z = 0;
};

bool SafeReadFloat(uint32_t address, float& value)
{
    uint32_t bits = 0;
    if (!SafeReadU32(address, bits))
    {
        return false;
    }
    std::memcpy(&value, &bits, sizeof(value));
    return true;
}

bool ReadMouseMapPosition(NativeVector3& result)
{
    struct MouseWorldInput
    {
        float X;
        float Y;
        uint32_t Reserved;
        float Z;
    } input = {};

    const auto mouseWorld = ResolveProfileGlobal(NativeCatalogEntry::MouseWorldPointer);
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto functionRva = ResolveNativeCatalogRva(NativeCatalogEntry::MouseWorldToMapPosition);
    if (mouseWorld == 0 || moduleBase == 0 || functionRva == 0 ||
        !SafeReadFloat(mouseWorld + 0x3Cu, input.X) ||
        !SafeReadFloat(mouseWorld + 0x40u, input.Y) ||
        !SafeReadFloat(mouseWorld + 0x44u, input.Z))
    {
        return false;
    }

    using ToMapPositionFunction = void(__cdecl*)(const MouseWorldInput*, NativeVector3*, uint32_t, uint32_t);
    const auto function = reinterpret_cast<ToMapPositionFunction>(moduleBase + functionRva);
    function(&input, &result, 0, 0);
    return true;
}

uint32_t FindFirstMovableSelectedComponent()
{
    const auto selectionManager = ResolveProfileGlobal(NativeCatalogEntry::SelectionManager);
    const auto headOffset = ResolveNativeCatalogRva(NativeCatalogEntry::SelectionListHeadOffset);
    const auto countOffset = ResolveNativeCatalogRva(NativeCatalogEntry::SelectionCountOffset);
    uint32_t node = 0;
    uint32_t count = 0;
    if (selectionManager == 0 || headOffset == 0 || countOffset == 0 ||
        !SafeReadU32(selectionManager + headOffset, node) ||
        !SafeReadU32(selectionManager + countOffset, count) || count == 0 || count > 4096)
    {
        return 0;
    }

    for (uint32_t index = 0; index < count && node != 0; ++index)
    {
        uint32_t object = 0;
        uint32_t component = 0;
        uint32_t movable = 0;
        if (SafeReadU32(node + 0x08u, object) && object != 0 &&
            SafeReadU32(object + 0x138u, component) && component != 0 &&
            SafeReadStructureU32(component, NativeCatalogEntry::MovementModuleOffset, movable) && movable != 0)
        {
            return component;
        }
        uint32_t next = 0;
        if (!SafeReadU32(node, next))
        {
            break;
        }
        node = next;
    }
    return 0;
}

uint32_t FindFirstSelectedComponent()
{
    const auto selectionManager = ResolveProfileGlobal(NativeCatalogEntry::SelectionManager);
    const auto headOffset = ResolveNativeCatalogRva(NativeCatalogEntry::SelectionListHeadOffset);
    const auto countOffset = ResolveNativeCatalogRva(NativeCatalogEntry::SelectionCountOffset);
    uint32_t node = 0;
    uint32_t object = 0;
    uint32_t component = 0;
    uint32_t count = 0;
    if (selectionManager == 0 || headOffset == 0 || countOffset == 0 ||
        !SafeReadU32(selectionManager + countOffset, count) || count == 0 ||
        !SafeReadU32(selectionManager + headOffset, node) || node == 0 ||
        !SafeReadU32(node + 0x08u, object) || object == 0 ||
        !SafeReadU32(object + 0x138u, component))
    {
        return 0;
    }
    return component;
}

uint32_t FindFirstSelectedObjectComponent()
{
    const auto selectionManager = ResolveProfileGlobal(NativeCatalogEntry::SelectionManager);
    const auto headOffset = ResolveNativeCatalogRva(NativeCatalogEntry::DestroySelectionListHeadOffset);
    const auto countOffset = ResolveNativeCatalogRva(NativeCatalogEntry::SelectionCountOffset);
    uint32_t count = 0;
    uint32_t node = 0;
    uint32_t object = 0;
    uint32_t component = 0;
    if (selectionManager == 0 || headOffset == 0 || countOffset == 0 ||
        !SafeReadU32(selectionManager + countOffset, count) || count == 0 ||
        !SafeReadU32(selectionManager + headOffset, node) || node == 0 ||
        !SafeReadU32(node + 0x08u, object) || object == 0 ||
        !SafeReadU32(object + 0x138u, component))
    {
        return 0;
    }
    return component;
}

uint32_t LevelUpSelectedVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    uint32_t veterancy = 0;
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto functionRva = ResolveNativeCatalogRva(NativeCatalogEntry::LevelUpSelected);
    if (moduleBase == 0 || functionRva == 0 ||
        !SafeReadStructureU32(weaponStoreUser, NativeCatalogEntry::VeterancyOffset, veterancy) || veterancy == 0)
    {
        return 0;
    }
    using LevelUpFunction = uint32_t(__thiscall*)(void*, uint32_t, uint32_t, uint32_t);
    const auto function = reinterpret_cast<LevelUpFunction>(moduleBase + functionRva);
    function(reinterpret_cast<void*>(static_cast<uintptr_t>(veterancy)), arguments[0], arguments[1], arguments[2]);
    return 1;
}

void LevelUpSelectedOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[1] = VisitSelectedObjects(LevelUpSelectedVisitor, arguments, results[0]) ? 1u : 0u;
}

void KillUnitOnGameThread(const uint32_t*, uint32_t* results)
{
    results[0] = 0;
    const auto component = FindFirstSelectedObjectComponent();
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto functionRva = ResolveNativeCatalogRva(NativeCatalogEntry::KillUnit);
    if (component == 0 || moduleBase == 0 || functionRva == 0)
    {
        return;
    }
    // sub_79EA50 is __thiscall(GameObject* this, int a2, int a3, int a4) and does retn 0Ch
    // (callee cleans 3 stack args). The engine's own kill path calls it as
    // sub_79EA50(obj, 6, 1, 0) from sub_58F1A0 and sub_79ECB0. Declaring it with fewer
    // args (the previous __thiscall(void*)) left ESP off by 12 bytes after the call and
    // triggered Run-Time Check Failure #0.
    using KillUnitFunction = uint32_t(__thiscall*)(void*, int, int, int);
    const auto function = reinterpret_cast<KillUnitFunction>(moduleBase + functionRva);
    function(reinterpret_cast<void*>(static_cast<uintptr_t>(component)), 6, 1, 0);
    results[0] = 1;
}

uint32_t SetUnitStateVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    uint32_t first = 0;
    uint32_t second = 0;
    const auto firstOffset = ResolveStructureOffset(NativeCatalogEntry::UnitStatePrimaryOffset);
    const auto secondOffset = ResolveStructureOffset(NativeCatalogEntry::UnitStateSecondaryOffset);
    if (firstOffset == 0 || secondOffset == 0 ||
        !SafeReadU32(weaponStoreUser + firstOffset, first) || !SafeReadU32(weaponStoreUser + secondOffset, second))
    {
        return 0;
    }
    const auto requested = arguments == nullptr ? 0u : arguments[0];
    const auto primaryFlags = requested == 0 ? 0x80000000u : requested;
    const auto secondaryFlags = requested == 0 ? 0x00000800u : 0u;
    return SafeWriteU32(weaponStoreUser + firstOffset, first | primaryFlags) &&
        SafeWriteU32(weaponStoreUser + secondOffset, second | secondaryFlags) ? 1u : 0u;
}

void SetUnitStateOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[1] = VisitSelectedObjects(SetUnitStateVisitor, arguments, results[0]) ? 1u : 0u;
}

uint32_t GrantSelectedUpgradeVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    uint32_t upgradeStore = 0;
    const auto definition = arguments[0];
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto hasRva = ResolveNativeCatalogRva(NativeCatalogEntry::PlayerGetUpgradeStore);
    if (definition == 0 || moduleBase == 0 || hasRva == 0 ||
        !SafeReadStructureU32(weaponStoreUser, NativeCatalogEntry::ObjectOwnerOffset, upgradeStore) || upgradeStore == 0)
    {
        return 0;
    }
    using HasUpgradeFunction = uint32_t(__thiscall*)(void*, uint32_t);
    const auto hasUpgrade = reinterpret_cast<HasUpgradeFunction>(moduleBase + hasRva);
    if (hasUpgrade(reinterpret_cast<void*>(static_cast<uintptr_t>(upgradeStore)), definition) != 0)
    {
        return 1;
    }
    return GrantUpgradeToContext(upgradeStore, definition, 0) ? 1u : 0u;
}

void GrantSelectedUpgradeOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    bool invoked = false;
    const auto definition = LookupStoreEntry(
        NativeCatalogEntry::ScienceStoreFindUpgrade, arguments[0], invoked);
    if (!invoked || definition == 0)
    {
        results[0] = 0;
        results[1] = 0;
        return;
    }
    const uint32_t visitorArguments[] = { definition };
    results[1] = VisitSelectedObjects(GrantSelectedUpgradeVisitor, visitorArguments, results[0]) ? 1u : 0u;
}

// ---------------------------------------------------------------------------
// Object-level upgrade grant. Same-type filter uses GameObject+0x4
// (ThingTemplate*); the reference template is pre-read from the first selected
// object and passed through visitor arguments — no static/global state.
//
// Shared resolver: resolves an upgrade hash to an UpgradeTemplate* and reports
// its Type (PLAYER=0, OBJECT=1). Used by both the read path (collect all) and
// the grant path (dispatch by Type). Returns false only when the template
// cannot be resolved or the Type field is out of the known enum range.
//
// UpgradeTemplate layout (1.12, IDA-verified 2026-07-14, upgrades_science.md):
//   [tpl+0xC]  = UpgradeTemplateDefinition*
//   [definition + UpgradeTemplateTypeOffset] = Type (PLAYER=0, OBJECT=1).
// ---------------------------------------------------------------------------

// Upgrade type constants (XSD UpgradeDef.xsd binary enum, IDA branch at
// ObjectGrantOrRemoveUpgrade_579640).
enum UpgradeTemplateType : uint32_t
{
    UpgradeType_Player = 0,
    UpgradeType_Object = 1,
};

bool TryResolveUpgradeTemplate(uint32_t upgradeHash, uint32_t typeOffset,
                                uint32_t& outTemplate, uint32_t& outType)
{
    outTemplate = 0;
    outType = UpgradeType_Player;
    if (upgradeHash == 0 || typeOffset == 0) return false;

    // Use ScienceStoreFindScience (RVA 0x1456F0 = UpgradeCenter_GetUpgradeTemplateByHash_5456F0)
    // to resolve by NAME HASH, not ScienceStoreFindUpgrade (RVA 0x147260 = FindUpgradeTemplateById_547260)
    // which resolves by instanceId. TriggeredBy arrays store name hashes, matching GetUpgradeTemplateByHash.
    bool invoked = false;
    const auto tpl = LookupStoreEntry(
        NativeCatalogEntry::ScienceStoreFindScience, upgradeHash, invoked);
    if (!invoked || tpl == 0) return false;

    uint32_t definition = 0;
    if (!SafeReadU32(tpl + 0xCu, definition) || definition == 0) return false;

    uint32_t typeValue = 0;
    if (!SafeReadU32(definition + typeOffset, typeValue)) return false;

    if (typeValue > UpgradeType_Object) return false;  // unknown enum value

    outTemplate = tpl;
    outType = typeValue;
    return true;
}

uint32_t GrantObjectUpgradeSameTypeVisitor(uint32_t /*selectedDrawable*/,
                                            uint32_t gameObject,
                                            const uint32_t* arguments)
{
    const uint32_t upgradeTemplate = arguments[0];
    const uint32_t referenceThingTemplate = arguments[1];

    uint32_t thisTemplate = 0;
    if (!SafeReadU32(gameObject + 0x4u, thisTemplate) || thisTemplate == 0)
    {
        return 0;
    }
    if (thisTemplate != referenceThingTemplate)
    {
        return 0;
    }

    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto addRva = ResolveNativeCatalogRva(NativeCatalogEntry::GameObjectAddUpgrade);
    if (moduleBase == 0 || addRva == 0) return 0;

    using AddUpgradeFunction = void(__thiscall*)(void*, uint32_t, uint32_t);
    const auto fn = reinterpret_cast<AddUpgradeFunction>(moduleBase + addRva);
    fn(reinterpret_cast<void*>(static_cast<uintptr_t>(gameObject)), upgradeTemplate, 0u);
    return 1;
}

void GrantObjectUpgradeOnSelectedSameTypeOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    // results[0] = grant outcome (1=success, 0=fail).
    // results[1] = traversal-established flag.
    results[0] = 0;
    results[1] = 0;

    const uint32_t upgradeHash = arguments[0];
    const auto typeOffset = ResolveStructureOffset(NativeCatalogEntry::UpgradeTemplateTypeOffset);

    uint32_t upgradeTemplate = 0;
    uint32_t upgradeType = UpgradeType_Player;
    if (!TryResolveUpgradeTemplate(upgradeHash, typeOffset, upgradeTemplate, upgradeType)) return;

    if (upgradeType == UpgradeType_Object)
    {
        // OBJECT grant: per-object via GameObject_AddUpgrade, filtered to same-type units.
        if (ResolveNativeCatalogRva(NativeCatalogEntry::GameObjectAddUpgrade) == 0) return;

        const uint32_t firstObject = FindFirstSelectedComponent();
        uint32_t referenceThingTemplate = 0;
        if (firstObject == 0 ||
            !SafeReadU32(firstObject + 0x4u, referenceThingTemplate) ||
            referenceThingTemplate == 0)
        {
            return;
        }

        const uint32_t visitorArgs[2] = { upgradeTemplate, referenceThingTemplate };
        results[1] = VisitSelectedObjects(
            GrantObjectUpgradeSameTypeVisitor, visitorArgs, results[0]) ? 1u : 0u;
    }
    else
    {
        // PLAYER grant: player-wide via Player_GrantUpgrade (catalog entry
        // ScienceManagerHasScience RVA + moduleBase = Player_GrantUpgrade VA).
        // This affects the entire player, not just selected units — engine semantics.
        // Get the player from the first selected object: [obj+ObjectOwnerOffset].
        const uint32_t firstObject = FindFirstSelectedComponent();
        if (firstObject == 0) return;

        uint32_t player = 0;
        if (!SafeReadStructureU32(firstObject, NativeCatalogEntry::ObjectOwnerOffset, player) ||
            player == 0)
        {
            return;
        }

        results[0] = GrantUpgradeToContext(player, upgradeTemplate, 0) ? 1u : 0u;
        results[1] = 1u;
    }
}

// ---------------------------------------------------------------------------
// Dynamic upgrade list reader. Reads the first selected unit's allModules
// TriggeredBy lists and collects up to 20 unique upgrade hashes (both PLAYER
// and OBJECT types). The grant handler dispatches by Type at grant time.
//
// Output is written directly into the dispatcher-owned Values[24] buffer
// (results), never into a caller-stack pointer. Layout (IDA-verified
// 2026-07-14, upgrades_science.md):
//   results[0]      = success (1) incl. empty results
//   results[1]      = UnitTypeId (SelectedUnitCode global)
//   results[2]      = ThingTemplate* of the first selected object
//   results[3]      = Count (0..20)
//   results[4..23]  = upgrade hashes
//
//   GameObject+ProductionModulesOffset = allModules (ObjectModule**, NULL-terminated)
//   ObjectModule+0xC  = embedded interface; this = module+0xC, [this] = vtable
//   vtable+0x30       = upgrade handler getter (__thiscall, ecx = module+0xC)
//   handler - 0xC     = UpgradeModuleData*
//   UpgradeModuleData+0x0 = TriggeredBy.Count, +0x4 = TriggeredBy.Array (uint32*)
//   GameObject+0x4    = ThingTemplate*
// Each candidate hash is resolved via TryResolveObjectUpgradeTemplate so only
// OBJECT upgrades are collected (PLAYER/unknown are filtered out).
// ---------------------------------------------------------------------------

uint32_t CallThiscall1(uint32_t funcAddr, uint32_t ecxArg)
{
    using ThiscallUintFn = uint32_t(__thiscall*)(void*);
    const auto fn = reinterpret_cast<ThiscallUintFn>(funcAddr);
    return fn(reinterpret_cast<void*>(static_cast<uintptr_t>(ecxArg)));
}

void GetSelectedUnitUpgradesOnGameThread(const uint32_t* /*arguments*/, uint32_t* results)
{
    // results = dispatcher-owned Values[24]: [0]success [1]UnitTypeId [2]ThingTemplate
    // [3]Count [4..23]hashes. No caller-stack pointer is read or written here.
    results[0] = 0;
    results[1] = 0;
    results[2] = 0;
    results[3] = 0;

    const auto modulesOffset = ResolveStructureOffset(NativeCatalogEntry::ProductionModulesOffset);
    const auto typeOffset = ResolveStructureOffset(NativeCatalogEntry::UpgradeTemplateTypeOffset);
    if (modulesOffset == 0 || typeOffset == 0)
    {
        results[0] = 1;
        return;
    }

    const uint32_t firstObject = FindFirstSelectedComponent();
    if (firstObject == 0)
    {
        results[0] = 1;
        return;
    }

    const auto selectedUnitCode = ResolveProfileGlobal(NativeCatalogEntry::SelectedUnitCode);
    if (selectedUnitCode != 0)
    {
        SafeReadU32(selectedUnitCode, results[1]);
    }
    SafeReadU32(firstObject + 0x4u, results[2]);

    uint32_t modulesArrayAddr = 0;
    if (!SafeReadU32(firstObject + modulesOffset, modulesArrayAddr) || modulesArrayAddr == 0)
    {
        results[0] = 1;
        return;
    }

    for (uint32_t slot = 0; slot < 4096 && results[3] < 20; ++slot)
    {
        uint32_t modulePtr = 0;
        if (!SafeReadU32(modulesArrayAddr + slot * sizeof(uint32_t), modulePtr)) break;
        if (modulePtr == 0) break;

        const uint32_t interfaceThis = modulePtr + 0xCu;
        uint32_t vtable = 0;
        if (!SafeReadU32(interfaceThis, vtable) || vtable == 0) continue;

        uint32_t getter = 0;
        if (!SafeReadU32(vtable + 0x30u, getter) || getter == 0) continue;

        const uint32_t handler = CallThiscall1(getter, interfaceThis);
        if (handler == 0) continue;

        uint32_t data = 0;
        if (!SafeReadU32(handler - 0xCu, data) || data == 0) continue;

        uint32_t tbCount = 0;
        uint32_t tbArray = 0;
        if (!SafeReadU32(data + 0xCu, tbCount) || tbCount > 64) continue;
        if (!SafeReadU32(data + 0x10u, tbArray) || tbArray == 0) continue;

        for (uint32_t i = 0; i < tbCount && results[3] < 20; ++i)
        {
            uint32_t hash = 0;
            if (!SafeReadU32(tbArray + i * sizeof(uint32_t), hash) || hash == 0) continue;

            // Resolve via GetUpgradeTemplateByHash (name-hash lookup). Collect both
            // PLAYER and OBJECT types; the grant handler dispatches by Type.
            uint32_t resolvedTemplate = 0;
            uint32_t resolvedType = UpgradeType_Player;
            if (!TryResolveUpgradeTemplate(hash, typeOffset, resolvedTemplate, resolvedType)) continue;

            bool dup = false;
            for (uint32_t j = 0; j < results[3]; ++j)
            {
                if (results[4 + j] == hash) { dup = true; break; }
            }
            if (!dup)
            {
                results[4 + results[3]++] = hash;
            }
        }
    }

    results[0] = 1;
}

void ClearPlayerTechLocksOnGameThread(const uint32_t*, uint32_t* results)
{
    results[0] = 0;
    const auto gameClient = ResolveProfileGlobal(NativeCatalogEntry::PlayerManager);
    const auto player = GetCurrentPlayerPointer();
    if (gameClient == 0 || player == 0)
    {
        return;
    }
    uint32_t lockWords = 0;
    uint32_t indexedPlayer = 0;
    uint32_t playerIndex = 0;
    if (SafeReadU32(gameClient + 0x84u, lockWords) && lockWords != 0 &&
        SafeReadU32(gameClient + 0x28u, indexedPlayer) && indexedPlayer != 0 &&
        SafeReadU32(indexedPlayer + 0x20u, playerIndex) && playerIndex <= 0x13u)
    {
        if (!SafeWriteU32(lockWords + playerIndex * sizeof(uint32_t), 0))
        {
            return;
        }
    }
    if (!SafeWriteU32(player + 0x200u, player + 0x200u) ||
        !SafeWriteU32(player + 0x204u, player + 0x200u))
    {
        return;
    }
    const auto managerOffset = ResolveNativeCatalogRva(NativeCatalogEntry::PlayerScienceManagerOffset);
    uint32_t manager = 0;
    uint32_t end = 0;
    if (managerOffset != 0 && SafeReadU32(player + managerOffset, manager) && manager != 0 &&
        SafeReadU32(manager + 0x44u, end))
    {
        SafeWriteU32(manager + 0x48u, end);
    }
    results[0] = 1;
}

uint32_t CreateUnitAtExplicitPosition(uint32_t thingClass, const NativeVector3& position)
{
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto functionRva = ResolveNativeCatalogRva(NativeCatalogEntry::CreateUnit);
    if (moduleBase == 0 || functionRva == 0 || thingClass == 0)
    {
        return 0;
    }
    uint32_t yBits = 0;
    uint32_t zBits = 0;
    std::memcpy(&yBits, &position.Y, sizeof(yBits));
    std::memcpy(&zBits, &position.Z, sizeof(zBits));
    using CreateUnitFunction = uint32_t(__cdecl*)(uint32_t, const float*, uint32_t, uint32_t, uint32_t);
    const auto function = reinterpret_cast<CreateUnitFunction>(moduleBase + functionRva);
    return function(thingClass, &position.X, yBits, zBits, 0);
}

uint32_t CreateOwnedUnit(uint32_t thingClass, const NativeVector3& position, uint32_t player)
{
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto functionRva = ResolveNativeCatalogRva(NativeCatalogEntry::CreateUnit);
    uint32_t playerContext = 0;
    if (moduleBase == 0 || functionRva == 0 || thingClass == 0 || player == 0 ||
        !SafeReadU32(player + 0x10u, playerContext))
    {
        return 0;
    }
    using CreateOwnedUnitFunction = uint32_t(__cdecl*)(uint32_t, uint32_t, const NativeVector3*, uint32_t, uint32_t);
    const auto function = reinterpret_cast<CreateOwnedUnitFunction>(moduleBase + functionRva);
    return function(0, thingClass, &position, player, playerContext);
}

void CreateUnitOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    NativeVector3 position = {};
    std::memcpy(&position.X, &arguments[1], sizeof(float));
    std::memcpy(&position.Y, &arguments[2], sizeof(float));
    std::memcpy(&position.Z, &arguments[3], sizeof(float));
    results[0] = CreateUnitAtExplicitPosition(arguments[0], position);
}

void CopyForMeOnGameThread(const uint32_t*, uint32_t* results)
{
    results[0] = 0;
    const auto selectedUnitCode = ResolveProfileGlobal(NativeCatalogEntry::SelectedUnitCode);
    const auto thingClass = FindThingClassOnGameThread(selectedUnitCode);
    const auto player = GetMutationOwnerPlayer();
    NativeVector3 position = {};
    if (thingClass == 0 || player == 0 || !ReadMouseMapPosition(position))
    {
        return;
    }
    results[0] = CreateOwnedUnit(thingClass, position, player);
}

void GetMeBaseOnGameThread(const uint32_t*, uint32_t* results)
{
    static constexpr uint32_t kBaseUnitIds[] = { 0x28DA574Eu, 0xAF4C0DA5u, 0x1C2EF767u };
    results[0] = 0;
    const auto player = GetMutationOwnerPlayer();
    NativeVector3 position = {};
    if (player == 0 || !ReadMouseMapPosition(position))
    {
        return;
    }
    for (const auto unitId : kBaseUnitIds)
    {
        const auto thingClass = FindThingClassOnGameThread(unitId);
        if (thingClass != 0 && CreateOwnedUnit(thingClass, position, player) != 0)
        {
            ++results[0];
        }
    }
}

void WeNeedBackOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[0] = 0;
    const auto thingClass = FindThingClassOnGameThread(arguments[0]);
    const auto player = GetMutationOwnerPlayer();
    NativeVector3 position = {};
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto levelUpRva = ResolveNativeCatalogRva(NativeCatalogEntry::LevelUpSelected);
    if (thingClass == 0 || player == 0 || moduleBase == 0 || levelUpRva == 0 ||
        !ReadMouseMapPosition(position))
    {
        return;
    }
    using LevelUpFunction = uint32_t(__thiscall*)(void*, uint32_t, uint32_t, uint32_t);
    const auto levelUp = reinterpret_cast<LevelUpFunction>(moduleBase + levelUpRva);
    for (uint32_t index = 0; index < arguments[1]; ++index)
    {
        const auto created = CreateOwnedUnit(thingClass, position, player);
        uint32_t veterancy = 0;
        if (created == 0 ||
            !SafeReadStructureU32(created, NativeCatalogEntry::VeterancyOffset, veterancy) || veterancy == 0)
        {
            continue;
        }
        levelUp(reinterpret_cast<void*>(static_cast<uintptr_t>(veterancy)), arguments[2], 1, 0);
        ++results[0];
    }
}

uint32_t FindTemplateReplacementSlot(uint32_t thingClass, uint32_t baseOffset)
{
    for (uint32_t index = 0; index < 3; ++index)
    {
        const auto slot = thingClass + baseOffset + index * 0x24u;
        uint32_t begin = 0;
        uint32_t end = 0;
        if (SafeReadU32(slot + 0x14u, begin) && SafeReadU32(slot + 0x18u, end) &&
            begin != end && begin != 0)
        {
            return begin;
        }
    }
    return 0;
}

bool ReplaceTemplateData(uint32_t targetHash, uint32_t donorHash, uint32_t baseOffset)
{
    const auto target = FindThingClassOnGameThread(targetHash);
    const auto donor = FindThingClassOnGameThread(donorHash);
    if (target == 0 || donor == 0)
    {
        return false;
    }
    const auto targetSlot = FindTemplateReplacementSlot(target, baseOffset);
    const auto donorSlot = FindTemplateReplacementSlot(donor, baseOffset);
    uint32_t targetData = 0;
    if (targetSlot == 0 || donorSlot == 0 || !SafeReadU32(targetSlot + 0x04u, targetData) || targetData == 0)
    {
        return false;
    }
    for (uint32_t offset = 0x08u; offset <= 0x24u; offset += sizeof(uint32_t))
    {
        uint32_t value = 0;
        if (!SafeReadU32(donorSlot + offset, value) || !SafeWriteU32(targetSlot + offset, value))
        {
            return false;
        }
    }
    return true;
}

void ReplaceTemplateModelOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[0] = ReplaceTemplateData(arguments[0], arguments[1], 0x1B8u) ? 1u : 0u;
}

void ReplaceTemplateWeaponOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[0] = ReplaceTemplateData(arguments[0], arguments[1], 0x1DCu) ? 1u : 0u;
}

bool PlayerUpgradeListContains(uint32_t player, uint32_t hash)
{
    uint32_t node = 0;
    if (!SafeReadU32(player + 0x34u, node))
    {
        return false;
    }
    for (uint32_t index = 0; index < 4096 && node != 0; ++index)
    {
        uint32_t first = 0;
        uint32_t second = 0;
        uint32_t third = 0;
        uint32_t value = 0;
        if (SafeReadU32(node + 0x04u, first) && first != 0 &&
            SafeReadU32(first + 0x0Cu, second) && second != 0 &&
            SafeReadU32(second, third) && third != 0 &&
            SafeReadU32(third + 0x04u, value) && value == hash)
        {
            return true;
        }
        uint32_t next = 0;
        if (!SafeReadU32(node + 0x0Cu, next))
        {
            break;
        }
        node = next;
    }
    return false;
}

void SecretProtocolBindingProbeOnGameThread(const uint32_t*, uint32_t* results)
{
    results[0] = 1;
    const auto player = GetCurrentPlayerPointer();
    const auto managerOffset = ResolveNativeCatalogRva(NativeCatalogEntry::PlayerScienceManagerOffset);
    uint32_t manager = 0;
    if (player == 0 || managerOffset == 0 ||
        !SafeReadU32(player + managerOffset, manager) || manager == 0)
    {
        return;
    }
    if (!GrantScienceToCurrentPlayer(0xDD6C4C5Bu))
    {
        results[0] = 2;
        return;
    }
    if (!GrantScienceToCurrentPlayer(0xFBE46678u))
    {
        results[0] = 2;
        return;
    }
    if (!PlayerUpgradeListContains(player, 0x5F7C162Fu))
    {
        bool invoked = false;
        const auto definition = LookupStoreEntry(
            NativeCatalogEntry::ScienceStoreFindScience, 0x5F7C162Fu, invoked);
        if (invoked && definition != 0)
        {
            GrantUpgradeToContext(player, definition, 1);
        }
    }
    results[0] = 3;
}

uint32_t ApplyMovementSpeedEntry(uint32_t entry, uint32_t mode, bool requireActive)
{
    if (entry == 0)
    {
        return 0;
    }
    uint32_t current = 0;
    uint32_t active = 0;
    if (!SafeReadU32(entry + 0x08u, current) ||
        (requireActive && (!SafeReadU32(entry + 0x18u, active) || active != 0x3F800000u)))
    {
        return 0;
    }
    if (mode == 4)
    {
        if (current != 0x43FA0000u && current != 0x41200000u && current != 0)
        {
            return 1;
        }
        uint32_t original = 0;
        return SafeReadU32(entry + 0x40u, original) && SafeWriteU32(entry + 0x08u, original) ? 1u : 0u;
    }
    if (current != 0x43FA0000u && current != 0x41200000u && current != 0)
    {
        SafeWriteU32(entry + 0x40u, current);
    }
    const auto target = mode == 1 ? 0x43FA0000u : mode == 2 ? 0x41200000u : 0u;
    return SafeWriteU32(entry + 0x08u, target) ? 1u : 0u;
}

uint32_t SetSelectedUnitSpeedVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    const auto movementOffset = ResolveNativeCatalogRva(NativeCatalogEntry::MovementModuleOffset);
    const auto containerOffset = ResolveNativeCatalogRva(NativeCatalogEntry::MovementContainerOffset);
    uint32_t movement = 0;
    uint32_t container = 0;
    uint32_t first = 0;
    uint32_t second = 0;
    if (movementOffset == 0 || containerOffset == 0 ||
        !SafeReadU32(weaponStoreUser + movementOffset, movement) || movement == 0 ||
        !SafeReadU32(movement + containerOffset, container) || container == 0)
    {
        return 0;
    }
    SafeReadU32(container, first);
    SafeReadU32(container + 0x04u, second);
    const auto changed = ApplyMovementSpeedEntry(first, arguments[0], false) +
        ApplyMovementSpeedEntry(second, arguments[0], true);
    return changed != 0 ? 1u : 0u;
}

void SetSelectedUnitSpeedOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[1] = VisitSelectedObjects(SetSelectedUnitSpeedVisitor, arguments, results[0]) ? 1u : 0u;
}

uint32_t CaptureSelectedUnitVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    return SafeWriteStructureU32(
        weaponStoreUser, NativeCatalogEntry::ObjectOwnerOffset, arguments[0]) ? 1u : 0u;
}

void CaptureSelectedUnitsOnGameThread(const uint32_t*, uint32_t* results)
{
    static constexpr uint32_t kExcludedUnitIds[] = {
        0x95D6E965u, 0xFD87E82Au, 0x856C9DD6u,
        0xD20552E1u, 0x1AFC9A6Eu, 0xD6D22475u
    };
    results[0] = 0;
    results[1] = 0;
    const auto selectedUnitCode = ResolveProfileGlobal(NativeCatalogEntry::SelectedUnitCode);
    for (const auto excluded : kExcludedUnitIds)
    {
        if (selectedUnitCode == excluded)
        {
            results[0] = 1;
            results[1] = 1;
            return;
        }
    }
    const auto player = GetMutationOwnerPlayer();
    if (player == 0)
    {
        return;
    }
    const uint32_t arguments[] = { player };
    results[1] = VisitSelectedObjects(CaptureSelectedUnitVisitor, arguments, results[0]) ? 1u : 0u;
}

uint32_t SetSelectedAmmoVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    uint32_t container = 0;
    uint32_t cursor = 0;
    uint32_t end = 0;
    if (!SafeReadStructureU32(weaponStoreUser, NativeCatalogEntry::WeaponContainerOffset, container) || container == 0 ||
        !SafeReadU32(container + 0x24u, cursor) ||
        !SafeReadU32(container + 0x28u, end) || cursor >= end)
    {
        return 0;
    }
    uint32_t changed = 0;
    for (; cursor < end; cursor += sizeof(uint32_t))
    {
        uint32_t slot = 0;
        uint32_t entries = 0;
        uint32_t active = 0;
        if (!SafeReadU32(cursor, slot) || slot == 0 ||
            !SafeReadU32(slot + 0xA4u, active) || (active & 0xFFu) == 0 ||
            !SafeReadU32(slot + 0x94u, entries) || entries == 0)
        {
            continue;
        }
        for (uint32_t index = 0; index < 6; ++index)
        {
            uint32_t entry = 0;
            if (SafeReadU32(entries + index * sizeof(uint32_t), entry) && entry != 0 &&
                SafeWriteU32(entry + 0x1Cu, arguments[0]))
            {
                ++changed;
            }
        }
    }
    return changed != 0 ? 1u : 0u;
}

void SetSelectedUnitAmmoOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[1] = VisitSelectedObjects(SetSelectedAmmoVisitor, arguments, results[0]) ? 1u : 0u;
}

// Smart-toggle one per-GameObject weapon flag. All off -> enable all; all on ->
// disable all; mixed -> enable all. Original flag words are retained so a rare
// write fault can roll back objects already updated in this pass.
void ToggleSelectedWeaponObjectFlagOnGameThread(uint16_t flagMask, uint32_t* results)
{
    results[0] = 0;
    results[1] = 0;
    if (!EnsureWeaponObjectFlagsInitialized())
    {
        return;
    }

    g_tempObjectCount = 0;
    uint32_t visitedCount = 0;
    if (!VisitSelectedObjects(CollectWeaponObjectsVisitor, nullptr, visitedCount) ||
        g_tempObjectCount == 0)
    {
        return;
    }

    bool hasDisabled = false;
    for (uint32_t i = 0; i < g_tempObjectCount; ++i)
    {
        if (!ReadWeaponObjectFlags(g_tempObjectBuffer[i], g_tempObjectFlags[i]))
        {
            return;
        }
        if ((g_tempObjectFlags[i] & flagMask) == 0)
        {
            hasDisabled = true;
        }
    }
    const bool enable = hasDisabled;

    uint32_t writtenCount = 0;
    for (; writtenCount < g_tempObjectCount; ++writtenCount)
    {
        const auto original = g_tempObjectFlags[writtenCount];
        const auto updated = enable
            ? static_cast<uint16_t>(original | flagMask)
            : static_cast<uint16_t>(original & ~flagMask);
        if (WriteWeaponObjectFlags(g_tempObjectBuffer[writtenCount], updated))
        {
            continue;
        }

        while (writtenCount != 0)
        {
            --writtenCount;
            WriteWeaponObjectFlags(g_tempObjectBuffer[writtenCount], g_tempObjectFlags[writtenCount]);
        }
        return;
    }

    results[0] = g_tempObjectCount;
    results[1] = 1;
}

void ToggleSelectedAttackSpeedOnGameThread(const uint32_t*, uint32_t* results)
{
    ToggleSelectedWeaponObjectFlagOnGameThread(kAttackSpeedObjectFlag, results);
}

void ToggleSelectedAttackRangeOnGameThread(const uint32_t*, uint32_t* results)
{
    ToggleSelectedWeaponObjectFlagOnGameThread(kAttackRangeObjectFlag, results);
}

uint32_t TeleportSelectedUnitVisitor(uint32_t /*gameObject*/, uint32_t weaponStoreUser, const uint32_t* arguments)
{
    uint32_t movable = 0;
    NativeVector3 position = {};
    NativeVector3 delta = {};
    std::memcpy(&delta.X, &arguments[0], sizeof(float));
    std::memcpy(&delta.Y, &arguments[1], sizeof(float));
    std::memcpy(&delta.Z, &arguments[2], sizeof(float));
    if (!SafeReadStructureU32(weaponStoreUser, NativeCatalogEntry::MovementModuleOffset, movable) || movable == 0 ||
        !SafeReadFloat(weaponStoreUser + 0x38u, position.X) ||
        !SafeReadFloat(weaponStoreUser + 0x3Cu, position.Y) ||
        !SafeReadFloat(weaponStoreUser + 0x40u, position.Z))
    {
        return 0;
    }

    position.X += delta.X;
    position.Y += delta.Y;
    position.Z += delta.Z;
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto functionRva = ResolveNativeCatalogRva(NativeCatalogEntry::ObjectSetPosition);
    if (moduleBase == 0 || functionRva == 0)
    {
        return 0;
    }
    using SetPositionFunction = void(__thiscall*)(void*, const NativeVector3*);
    const auto function = reinterpret_cast<SetPositionFunction>(moduleBase + functionRva);
    function(reinterpret_cast<void*>(static_cast<uintptr_t>(weaponStoreUser)), &position);
    return 1;
}

void TeleportSelectedUnitsToMouseOnGameThread(const uint32_t*, uint32_t* results)
{
    results[0] = 0;
    results[1] = 0;
    NativeVector3 mouse = {};
    const auto anchor = FindFirstMovableSelectedComponent();
    NativeVector3 anchorPosition = {};
    if (anchor == 0 || !ReadMouseMapPosition(mouse) ||
        !SafeReadFloat(anchor + 0x38u, anchorPosition.X) ||
        !SafeReadFloat(anchor + 0x3Cu, anchorPosition.Y) ||
        !SafeReadFloat(anchor + 0x40u, anchorPosition.Z))
    {
        return;
    }

    NativeVector3 delta = {
        mouse.X - anchorPosition.X,
        mouse.Y - anchorPosition.Y,
        mouse.Z - anchorPosition.Z
    };
    uint32_t arguments[3] = {};
    std::memcpy(&arguments[0], &delta.X, sizeof(float));
    std::memcpy(&arguments[1], &delta.Y, sizeof(float));
    std::memcpy(&arguments[2], &delta.Z, sizeof(float));
    results[1] = VisitSelectedObjects(TeleportSelectedUnitVisitor, arguments, results[0]) ? 1u : 0u;
}

uint32_t FindThingClassOnGameThread(uint32_t unitTypeId)
{
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto storeRva = ResolveNativeCatalogRva(NativeCatalogEntry::ThingTemplateStore);
    const auto functionRva = ResolveNativeCatalogRva(NativeCatalogEntry::GetThingClass);
    if (moduleBase == 0 || storeRva == 0 || functionRva == 0 || unitTypeId == 0)
    {
        return 0;
    }

    uint32_t store = 0;
    if (!SafeReadU32(static_cast<uint32_t>(moduleBase + storeRva), store) || store == 0)
    {
        return 0;
    }

    using FindThingClassFunction = uint32_t(__thiscall*)(void*, uint32_t);
    const auto function = reinterpret_cast<FindThingClassFunction>(moduleBase + functionRva);
    return function(reinterpret_cast<void*>(static_cast<uintptr_t>(store)), unitTypeId);
}

void GetThingClassOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[0] = FindThingClassOnGameThread(arguments[0]);
}

void ReadSelectedUnitCodeOnGameThread(const uint32_t*, uint32_t* results)
{
    results[0] = 0;
    results[1] = 0;
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    const auto selectedUnitCodeRva = ResolveNativeCatalogRva(NativeCatalogEntry::SelectedUnitCode);
    if (moduleBase == 0 || selectedUnitCodeRva == 0 ||
        !SafeReadU32(static_cast<uint32_t>(moduleBase + selectedUnitCodeRva), results[0]) ||
        results[0] == 0)
    {
        return;
    }

    results[1] = FindThingClassOnGameThread(results[0]);
}

bool TryReadGameMode(int32_t& gameMode)
{
    const auto moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandle(nullptr));
    if (moduleBase == 0)
    {
        return false;
    }

    uint32_t gameClientPtr = 0;
    const auto gameClientRva = ResolveNativeCatalogRva(NativeCatalogEntry::GameClientPointer);
    if (gameClientRva == 0)
    {
        return false;
    }
    if (!SafeReadU32(static_cast<uint32_t>(moduleBase + gameClientRva), gameClientPtr) || gameClientPtr == 0)
    {
        return false;
    }

    uint32_t rawValue = 0;
    if (!SafeReadU32(gameClientPtr + kGameModeFieldOffset, rawValue))
    {
        return false;
    }

    gameMode = static_cast<int32_t>(rawValue);
    return true;
}

bool IsInShellMode()
{
    int32_t gameMode = 0;
    return TryReadGameMode(gameMode) && gameMode == kGameModeShell;
}

uint32_t NextGameApiRequestId()
{
    auto requestId = static_cast<uint32_t>(InterlockedIncrement(&g_nextGameApiRequestId));
    return requestId == 0 ? static_cast<uint32_t>(InterlockedIncrement(&g_nextGameApiRequestId)) : requestId;
}

template <typename TPayload>
void SetGameApiDiagnostics(
    TPayload& result,
    uint32_t requestId,
    uint32_t gameThreadTickBefore,
    uint32_t gameThreadTickAfter)
{
    result.RequestId = requestId;
    result.GameThreadTickBefore = gameThreadTickBefore;
    result.GameThreadTickAfter = gameThreadTickAfter;
}

}

bool TryGetGameMode(int32_t& gameMode)
{
    return TryReadGameMode(gameMode);
}

uint32_t ResolveCurrentPlayerPointer()
{
    return GetCurrentPlayerPointer();
}

void InitializeNativeCatalog()
{
    for (uint32_t index = 0; index < kNativeCatalogEntryCount; ++index)
    {
        g_nativeCatalogRvas[index] = kDefaultNativeCatalogRvas[index];
    }
    g_nativeCatalogReady = false;
}

bool HasNativeCatalog()
{
    return g_nativeCatalogReady;
}

// Disables hook-side reads immediately. A later toggle reinitializes every live
// GameObject before re-enabling the fast path; the registration hook clears future objects.
void ResetNativeGameApiRuntimeState()
{
    InterlockedExchange(&g_weaponObjectFlagsInitialized, 0);
}

bool IsAttackSpeedObject(uint32_t gameObject)
{
    uint16_t flags = 0;
    return AreWeaponObjectFlagsInitialized() &&
        ReadWeaponObjectFlags(gameObject, flags) &&
        (flags & kAttackSpeedObjectFlag) != 0;
}

bool IsAttackRangeObject(uint32_t gameObject)
{
    uint16_t flags = 0;
    return AreWeaponObjectFlagsInitialized() &&
        ReadWeaponObjectFlags(gameObject, flags) &&
        (flags & kAttackRangeObjectFlag) != 0;
}

void ClearWeaponObjectFlagsForRegisteredObject(uint32_t gameObject)
{
    // Hook 49 runs at GameLogic_RegisterObject entry, before the object is appended
    // to GameLogic::firstObject. Clearing the full word prevents pool-reused objects
    // from inheriting either effect.
    WriteWeaponObjectFlags(gameObject, 0);
}

uint32_t ResolveNativeCatalogRva(NativeCatalogEntry entry)
{
    const auto index = static_cast<uint32_t>(entry);
    if (index < kNativeCatalogEntryCount)
    {
        return g_nativeCatalogRvas[index];
    }
    return 0;
}

AgentStatusCode SetNativeCatalogFromPayload(const unsigned char* payload, uint32_t length)
{
    if (payload == nullptr && length != 0)
    {
        return AgentStatusCode::InvalidCommand;
    }

    constexpr uint32_t kCountSize = sizeof(uint32_t);
    if (length < kCountSize)
    {
        return AgentStatusCode::InvalidCommand;
    }

    uint32_t count = 0;
    std::memcpy(&count, payload, kCountSize);
    if (count != kNativeCatalogEntryCount)
    {
        return AgentStatusCode::InvalidCommand;
    }

    const uint32_t expectedLength = kCountSize + count * sizeof(uint32_t);
    if (length != expectedLength)
    {
        return AgentStatusCode::InvalidCommand;
    }

    uint32_t rvas[kNativeCatalogEntryCount] = {};
    for (uint32_t index = 0; index < count; ++index)
    {
        std::memcpy(
            &rvas[index],
            payload + kCountSize + index * sizeof(uint32_t),
            sizeof(uint32_t));
        // Zero is an explicit "unavailable on this profile" marker. Keeping it in the
        // runtime table prevents fallback to the baked-in 1.12 address for partial profiles.
    }

    for (uint32_t index = 0; index < kNativeCatalogEntryCount; ++index)
    {
        g_nativeCatalogRvas[index] = rvas[index];
    }
    g_nativeCatalogReady = true;
    return AgentStatusCode::Ok;
}

const GameApiFunctionSpec* GetGameApiCatalog(uint32_t& count)
{
    count = static_cast<uint32_t>(sizeof(kGameApiCatalog) / sizeof(kGameApiCatalog[0]));
    return kGameApiCatalog;
}

const GameApiFunctionSpec* FindGameApiSpec(const char* name)
{
    if (name == nullptr || name[0] == '\0')
    {
        return nullptr;
    }

    uint32_t count = 0;
    const auto* catalog = GetGameApiCatalog(count);
    for (uint32_t index = 0; index < count; ++index)
    {
        if (std::strcmp(catalog[index].Name, name) == 0)
        {
            return &catalog[index];
        }
    }

    return nullptr;
}

bool IsGameApiInvocationAllowed(
    const GameApiFunctionSpec& spec,
    bool isGameThread,
    bool smokeVerified)
{
    if (!spec.EnabledByDefault)
    {
        return false;
    }

    if (spec.ThreadRequirement != GameApiThreadRequirement::GameThread)
    {
        return false;
    }

    return smokeVerified && isGameThread;
}

bool IsValidNativeRequest(uint32_t timeoutMilliseconds, uint32_t enabled)
{
    return timeoutMilliseconds > 0 &&
        timeoutMilliseconds <= kMaxGameApiSmokeTimeoutMilliseconds &&
        enabled != 0;
}

AgentGameThreadDispatchStatus RunNativeRequest(
    AgentGameThreadWork work,
    const uint32_t* arguments,
    uint32_t argumentCount,
    uint32_t timeoutMilliseconds,
    AgentGameThreadResult& nativeResult,
    uint32_t& requestId,
    uint32_t& tickBefore,
    uint32_t& tickAfter)
{
    requestId = NextGameApiRequestId();
    tickBefore = AgentGameThreadDispatcher::PumpTick();
    AgentGameThreadRequest nativeRequest = {};
    nativeRequest.Work = work;
    if (arguments != nullptr && argumentCount > 0)
    {
        const auto count = argumentCount > 8 ? 8u : argumentCount;
        std::memcpy(nativeRequest.Arguments, arguments, count * sizeof(uint32_t));
    }
    const auto status = AgentGameThreadDispatcher::Dispatch(
        nativeRequest,
        timeoutMilliseconds,
        nativeResult);
    tickAfter = AgentGameThreadDispatcher::PumpTick();
    return status;
}

AgentGameThreadDispatchStatus RunNativeRequest(
    AgentGameThreadWork work,
    uint32_t argument0,
    uint32_t timeoutMilliseconds,
    AgentGameThreadResult& nativeResult,
    uint32_t& requestId,
    uint32_t& tickBefore,
    uint32_t& tickAfter)
{
    return RunNativeRequest(
        work, &argument0, 1, timeoutMilliseconds, nativeResult,
        requestId, tickBefore, tickAfter);
}

template <typename TRequest, typename TPayload>
AgentStatusCode DispatchNativeAction(
    const TRequest& request,
    TPayload& result,
    AgentGameThreadWork work,
    const uint32_t* arguments,
    uint32_t argumentCount)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi))
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }
    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        work, arguments, argumentCount, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && nativeResult.Values[0] != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }
    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeGetThingClass(
    const AgentGameApiGetThingClassRequest& request,
    AgentGameApiGetThingClassPayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    result.UnitTypeId = request.UnitTypeId;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi) ||
        request.UnitTypeId == 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }

    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }

    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        GetThingClassOnGameThread,
        request.UnitTypeId,
        request.TimeoutMilliseconds,
        nativeResult,
        result.RequestId,
        result.GameThreadTickBefore,
        result.GameThreadTickAfter);
    result.ThingClassAddress = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && result.ThingClassAddress != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }

    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeReadSelectedUnitCode(
    const AgentGameApiReadSelectedUnitCodeRequest& request,
    AgentGameApiSelectedUnitSnapshotPayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi))
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }

    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }

    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        ReadSelectedUnitCodeOnGameThread,
        0,
        request.TimeoutMilliseconds,
        nativeResult,
        result.RequestId,
        result.GameThreadTickBefore,
        result.GameThreadTickAfter);
    result.UnitTypeId = nativeResult.Values[0];
    result.ThingClassAddress = nativeResult.Values[1];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed &&
        result.UnitTypeId != 0 && result.ThingClassAddress != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }

    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::NoSelectedUnit);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeGetCurrentPlayer(
    const AgentGameApiGetCurrentPlayerRequest& request,
    AgentGameApiGetCurrentPlayerPayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi))
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }

    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }

    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        GetCurrentPlayerOnGameThread,
        0,
        request.TimeoutMilliseconds,
        nativeResult,
        result.RequestId,
        result.GameThreadTickBefore,
        result.GameThreadTickAfter);
    result.PlayerPointer = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && result.PlayerPointer != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }

    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(
        timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(
        timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::NoSelectedUnit);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeLookupScienceByHash(
    const AgentGameApiLookupScienceByHashRequest& request,
    AgentGameApiLookupScienceByHashPayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi) || request.Hash == 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }

    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        LookupScienceByHashOnGameThread, request.Hash, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    result.SciencePointer = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && nativeResult.Values[1] != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }

    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeLookupTemplateByHash(
    const AgentGameApiLookupTemplateByHashRequest& request,
    AgentGameApiLookupTemplateByHashPayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi) || request.Hash == 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }

    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        LookupTemplateByHashOnGameThread, request.Hash, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    result.TemplatePointer = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && nativeResult.Values[1] != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }

    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeLookupUpgradeByHash(
    const AgentGameApiLookupUpgradeByHashRequest& request,
    AgentGameApiLookupUpgradeByHashPayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi) || request.Hash == 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }

    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        LookupUpgradeByHashOnGameThread, request.Hash, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    result.UpgradePointer = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && nativeResult.Values[1] != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }

    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeHasUpgrade(
    const AgentGameApiHasUpgradeRequest& request,
    AgentGameApiHasUpgradePayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi) || request.UpgradeHash == 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }

    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        HasUpgradeOnGameThread, request.UpgradeHash, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    result.HasUpgrade = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && nativeResult.Values[1] != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }

    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeSetSelectedStatusBit(
    const AgentGameApiSetSelectedStatusBitRequest& request,
    AgentGameApiSetSelectedStatusBitPayload& result)
{
    const auto validBit = request.Domain == 0 ? request.BitIndex < 0xE0u : request.BitIndex < 0x1C9u;
    if (request.Domain >= 2 || !validBit)
    {
        result = {};
        result.AgentVersion = kAgentProtocolVersion;
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    const uint32_t arguments[] = { request.Domain, request.BitIndex, request.Enabled };
    return DispatchNativeAction(request, result, SetSelectedStatusBitOnGameThread, arguments, 3);
}

AgentStatusCode DispatchNativeSetSelectedUnitHealth(
    const AgentGameApiSetSelectedUnitHealthRequest& request,
    AgentGameApiSetSelectedUnitHealthPayload& result)
{
    if (request.Mode == 0 || request.Mode >= 5)
    {
        result = {};
        result.AgentVersion = kAgentProtocolVersion;
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    uint32_t healthBits = 0;
    uint32_t maxHealthBits = 0;
    std::memcpy(&healthBits, &request.Health, sizeof(healthBits));
    std::memcpy(&maxHealthBits, &request.MaxHealth, sizeof(maxHealthBits));
    const uint32_t arguments[] = { request.Mode, healthBits, maxHealthBits };
    return DispatchNativeAction(request, result, SetSelectedUnitHealthOnGameThread, arguments, 3);
}

// Not consolidated to DispatchNativeAction: needs result.ExpandedStructureCount
// = nativeResult.Values[0] before the success check, which the template doesn't support.
AgentStatusCode DispatchNativeExpandProductionQueue(
    const AgentGameApiExpandProductionQueueRequest& request,
    AgentGameApiExpandProductionQueuePayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi) ||
        request.MaxQueueEntries == 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }

    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        ExpandProductionQueueOnGameThread, request.MaxQueueEntries, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    result.ExpandedStructureCount = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed &&
        nativeResult.Values[1] != 0 && result.ExpandedStructureCount != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }

    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::NoSelectedUnit);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeTeleportSelectedUnitsToMouse(
    const AgentGameApiTeleportSelectedUnitsToMouseRequest& request,
    AgentGameApiTeleportSelectedUnitsToMousePayload& result)
{
    return DispatchNativeAction(request, result, TeleportSelectedUnitsToMouseOnGameThread, nullptr, 0);
}

AgentStatusCode DispatchNativeLevelUpSelected(
    const AgentGameApiLevelUpSelectedRequest& request,
    AgentGameApiLevelUpSelectedPayload& result)
{
    if (request.Count == 0)
    {
        result = {};
        result.AgentVersion = kAgentProtocolVersion;
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    const uint32_t arguments[] = { request.Count, request.Rank, request.Flags };
    return DispatchNativeAction(request, result, LevelUpSelectedOnGameThread, arguments, 3);
}

AgentStatusCode DispatchNativeKillUnit(
    const AgentGameApiKillUnitRequest& request,
    AgentGameApiKillUnitPayload& result)
{
    return DispatchNativeAction(request, result, KillUnitOnGameThread, nullptr, 0);
}

AgentStatusCode DispatchNativeSetUnitState(
    const AgentGameApiSetUnitStateRequest& request,
    AgentGameApiSetUnitStatePayload& result)
{
    const uint32_t arguments[] = { request.StateFlags };
    return DispatchNativeAction(request, result, SetUnitStateOnGameThread, arguments, 1);
}

AgentStatusCode DispatchNativeCreateUnit(
    const AgentGameApiCreateUnitRequest& request,
    AgentGameApiCreateUnitPayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi) ||
        request.ThingClassAddress == 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }
    uint32_t arguments[4] = { request.ThingClassAddress, 0, 0, 0 };
    std::memcpy(&arguments[1], &request.PosX, sizeof(float));
    std::memcpy(&arguments[2], &request.PosY, sizeof(float));
    std::memcpy(&arguments[3], &request.PosZ, sizeof(float));
    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        CreateUnitOnGameThread, arguments, 4, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    result.CreatedUnitAddress = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && result.CreatedUnitAddress != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }
    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeCopyForMe(
    const AgentGameApiCopyForMeRequest& request,
    AgentGameApiCopyForMePayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi))
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }
    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        CopyForMeOnGameThread, 0, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    result.CreatedUnitAddress = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && result.CreatedUnitAddress != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }
    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeGetMeBase(
    const AgentGameApiGetMeBaseRequest& request,
    AgentGameApiGetMeBasePayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi))
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }
    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        GetMeBaseOnGameThread, 0, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && nativeResult.Values[0] != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }
    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeGrantPlayerTech(
    const AgentGameApiGrantPlayerTechRequest& request,
    AgentGameApiGrantPlayerTechPayload& result)
{
    const uint32_t arguments[] = { request.TechHash };
    return DispatchNativeAction(request, result, GrantPlayerTechOnGameThread, arguments, 1);
}

AgentStatusCode DispatchNativeGrantUpgradeToPlayer(
    const AgentGameApiGrantUpgradeToPlayerRequest& request,
    AgentGameApiGrantUpgradeToPlayerPayload& result)
{
    const uint32_t arguments[] = { request.UpgradeHash };
    return DispatchNativeAction(request, result, GrantUpgradeToPlayerOnGameThread, arguments, 1);
}

AgentStatusCode DispatchNativeGrantSecretProtocol(
    const AgentGameApiGrantSecretProtocolRequest& request,
    AgentGameApiGrantSecretProtocolPayload& result)
{
    const uint32_t arguments[] = { request.TechHash, request.UpgradeHash };
    return DispatchNativeAction(request, result, GrantSecretProtocolOnGameThread, arguments, 2);
}

AgentStatusCode DispatchNativeGrantSelectedUpgrade(
    const AgentGameApiGrantSelectedUpgradeRequest& request,
    AgentGameApiGrantSelectedUpgradePayload& result)
{
    const uint32_t arguments[] = { request.UpgradeHash };
    return DispatchNativeAction(request, result, GrantSelectedUpgradeOnGameThread, arguments, 1);
}

AgentStatusCode DispatchNativeClearPlayerTechLocks(
    const AgentGameApiClearPlayerTechLocksRequest& request,
    AgentGameApiClearPlayerTechLocksPayload& result)
{
    return DispatchNativeAction(request, result, ClearPlayerTechLocksOnGameThread, nullptr, 0);
}

AgentStatusCode DispatchNativeWeNeedBack(
    const AgentGameApiWeNeedBackRequest& request,
    AgentGameApiWeNeedBackPayload& result)
{
    if (request.UnitTypeId == 0 || request.Count == 0 || request.Count > 1000)
    {
        result = {};
        result.AgentVersion = kAgentProtocolVersion;
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    const uint32_t arguments[] = { request.UnitTypeId, request.Count, request.Rank };
    return DispatchNativeAction(request, result, WeNeedBackOnGameThread, arguments, 3);
}

AgentStatusCode DispatchNativeReplaceTemplateModel(
    const AgentGameApiReplaceTemplateModelRequest& request,
    AgentGameApiReplaceTemplateModelPayload& result)
{
    const uint32_t arguments[] = { request.TargetHash, request.DonorHash };
    return DispatchNativeAction(request, result, ReplaceTemplateModelOnGameThread, arguments, 2);
}

AgentStatusCode DispatchNativeReplaceTemplateWeapon(
    const AgentGameApiReplaceTemplateWeaponRequest& request,
    AgentGameApiReplaceTemplateWeaponPayload& result)
{
    const uint32_t arguments[] = { request.TargetHash, request.DonorHash };
    return DispatchNativeAction(request, result, ReplaceTemplateWeaponOnGameThread, arguments, 2);
}

AgentStatusCode DispatchNativeSecretProtocolBindingProbe(
    const AgentGameApiSecretProtocolBindingProbeRequest& request,
    AgentGameApiSecretProtocolBindingProbePayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi))
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }
    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        SecretProtocolBindingProbeOnGameThread, 0, request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    result.ProbeResult = nativeResult.Values[0];
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && result.ProbeResult != 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }
    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeSetSelectedUnitSpeed(
    const AgentGameApiSetSelectedUnitSpeedRequest& request,
    AgentGameApiSetSelectedUnitSpeedPayload& result)
{
    if (request.Mode == 0 || request.Mode > 4)
    {
        result = {};
        result.AgentVersion = kAgentProtocolVersion;
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    const uint32_t arguments[] = { request.Mode };
    return DispatchNativeAction(request, result, SetSelectedUnitSpeedOnGameThread, arguments, 1);
}

AgentStatusCode DispatchNativeCaptureSelectedUnits(
    const AgentGameApiCaptureSelectedUnitsRequest& request,
    AgentGameApiCaptureSelectedUnitsPayload& result)
{
    return DispatchNativeAction(request, result, CaptureSelectedUnitsOnGameThread, nullptr, 0);
}

AgentStatusCode DispatchNativeSetSelectedUnitAmmo(
    const AgentGameApiSetSelectedUnitAmmoRequest& request,
    AgentGameApiSetSelectedUnitAmmoPayload& result)
{
    if (request.Ammo == 0)
    {
        result = {};
        result.AgentVersion = kAgentProtocolVersion;
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    const uint32_t arguments[] = { request.Ammo };
    return DispatchNativeAction(request, result, SetSelectedUnitAmmoOnGameThread, arguments, 1);
}

AgentStatusCode DispatchNativeToggleSelectedAttackSpeed(
    const AgentGameApiToggleSelectedAttackSpeedRequest& request,
    AgentGameApiToggleSelectedAttackSpeedPayload& result)
{
    return DispatchNativeAction(request, result, ToggleSelectedAttackSpeedOnGameThread, nullptr, 0);
}

AgentStatusCode DispatchNativeToggleSelectedAttackRange(
    const AgentGameApiToggleSelectedAttackRangeRequest& request,
    AgentGameApiToggleSelectedAttackRangePayload& result)
{
    return DispatchNativeAction(request, result, ToggleSelectedAttackRangeOnGameThread, nullptr, 0);
}

void ClearSelectedAttackSpeedEffectsOnGameThread(const uint32_t*, uint32_t* results)
{
    results[1] = ClearWeaponObjectFlagFromAllObjects(
        kAttackSpeedObjectFlag,
        results[0]) ? 1u : 0u;
}

void ClearSelectedAttackRangeEffectsOnGameThread(const uint32_t*, uint32_t* results)
{
    results[1] = ClearWeaponObjectFlagFromAllObjects(
        kAttackRangeObjectFlag,
        results[0]) ? 1u : 0u;
}

AgentStatusCode DispatchNativeClearSelectedAttackSpeedEffects(
    const AgentGameApiClearSelectedAttackSpeedEffectsRequest& request,
    AgentGameApiClearSelectedAttackSpeedEffectsPayload& result)
{
    return DispatchNativeAction(request, result, ClearSelectedAttackSpeedEffectsOnGameThread, nullptr, 0);
}

AgentStatusCode DispatchNativeClearSelectedAttackRangeEffects(
    const AgentGameApiClearSelectedAttackRangeEffectsRequest& request,
    AgentGameApiClearSelectedAttackRangeEffectsPayload& result)
{
    return DispatchNativeAction(request, result, ClearSelectedAttackRangeEffectsOnGameThread, nullptr, 0);
}

AgentStatusCode DispatchNativeGrantObjectUpgradeOnSelectedSameType(
    const AgentGameApiGrantObjectUpgradeOnSelectedSameTypeRequest& request,
    AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi) ||
        request.UpgradeHash == 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }
    // Fail-closed before consuming a game-thread slot: the profile must have verified both
    // the OBJECT-type layout and the grant function. The handler re-checks OBJECT type.
    if (ResolveStructureOffset(NativeCatalogEntry::UpgradeTemplateTypeOffset) == 0 ||
        ResolveNativeCatalogRva(NativeCatalogEntry::GameObjectAddUpgrade) == 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::Ok;
    }
    const uint32_t arguments[] = { request.UpgradeHash };
    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        GrantObjectUpgradeOnSelectedSameTypeOnGameThread, arguments, 1,
        request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);
    // Success requires traversal established (Values[1]) AND at least one actual grant
    // (Values[0]). A traversal that matched zero same-type objects is not a success.
    if (nativeStatus == AgentGameThreadDispatchStatus::Completed &&
        nativeResult.Values[1] != 0 && nativeResult.Values[0] > 0)
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
        return AgentStatusCode::Ok;
    }
    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(
        timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(
        timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

AgentStatusCode DispatchNativeGetSelectedUnitUpgrades(
    const AgentGameApiGetSelectedUnitUpgradesRequest& request,
    AgentGameApiSelectedUnitUpgradesPayload& result)
{
    result = {};
    result.AgentVersion = kAgentProtocolVersion;
    if (!IsValidNativeRequest(request.TimeoutMilliseconds, request.EnableDirectGameApi))
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Disabled);
        return AgentStatusCode::InvalidCommand;
    }
    if (IsInShellMode())
    {
        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::NoGameTick);
        return AgentStatusCode::Ok;
    }

    // The handler writes directly into the dispatcher-owned Values[24] buffer; no
    // caller-stack pointer crosses the game-thread boundary.
    AgentGameThreadResult nativeResult = {};
    const auto nativeStatus = RunNativeRequest(
        GetSelectedUnitUpgradesOnGameThread, nullptr, 0,
        request.TimeoutMilliseconds, nativeResult,
        result.RequestId, result.GameThreadTickBefore, result.GameThreadTickAfter);

    if (nativeStatus == AgentGameThreadDispatchStatus::Completed && nativeResult.Values[0] != 0)
    {
        const uint32_t count = nativeResult.Values[3];
        if (count <= 20u)
        {
            result.StatusCode = static_cast<uint16_t>(AgentStatusCode::Ok);
            result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Completed);
            result.UnitTypeId = nativeResult.Values[1];
            result.ThingTemplateAddress = nativeResult.Values[2];
            result.Count = count;
            static_assert(sizeof(result.UpgradeHash0) == sizeof(uint32_t),
                          "UpgradeHash fields must be uint32-sized");
            std::memcpy(&result.UpgradeHash0, &nativeResult.Values[4], count * sizeof(uint32_t));
            return AgentStatusCode::Ok;
        }
        // count > 20: corrupted handler output. Fall through to Failed; do not copy.
    }
    const auto timedOut = nativeStatus == AgentGameThreadDispatchStatus::TimedOut;
    result.StatusCode = static_cast<uint16_t>(
        timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError);
    result.DispatchStatus = static_cast<uint32_t>(
        timedOut ? GameApiDispatchStatus::TimedOut : GameApiDispatchStatus::Failed);
    return timedOut ? AgentStatusCode::TimedOut : AgentStatusCode::InternalError;
}

#include "Generated/AgentGameApi.Dispatch.generated.inc"

}
