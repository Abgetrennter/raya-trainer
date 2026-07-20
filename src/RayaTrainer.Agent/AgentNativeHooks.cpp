#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <algorithm>
#include <cstdint>
#include <cstring>

#include "AgentFeatureState.h"
#include "AgentGameApi.h"
#include "AgentGameThreadDispatcher.h"
#include "AgentNativeHooks.h"

namespace RayaTrainer::agent
{
namespace
{
constexpr uint32_t kHookCapacity = 64;
constexpr uint32_t kUnlimitedAttackRangeBits = 0x461C4000u; // 10000.0f
uint32_t g_hookAddresses[kHookCapacity] = {};
uint32_t g_hookContinuations[kHookCapacity] = {};
volatile LONG g_playerObject = 0;
volatile LONG g_playerOwnerId = 0;
volatile LONG g_oneHitBody = 0;
bool g_logicFreezeActive = false;
bool g_slowMotionActive = false;
uint32_t g_slowMotionFrameCounter = 0;
volatile LONG g_oneHitOwner = 0;
volatile LONG g_oneHitCaller = 0;

template <typename T>
bool TryRead(uint32_t address, T& value)
{
    if (address == 0)
    {
        return false;
    }
    __try
    {
        value = *reinterpret_cast<const T*>(static_cast<uintptr_t>(address));
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

template <typename T>
bool TryWrite(uint32_t address, T value)
{
    if (address == 0)
    {
        return false;
    }
    __try
    {
        *reinterpret_cast<T*>(static_cast<uintptr_t>(address)) = value;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

uint32_t ReadPlayerOwnerId()
{
    return static_cast<uint32_t>(InterlockedCompareExchange(&g_playerOwnerId, 0, 0));
}

bool IsEnabled(NativeFeatureStateId id)
{
    return ReadNativeFeatureState(id) != 0;
}

uint32_t OwnerOffset()
{
    const auto value = ResolveNativeCatalogRva(NativeCatalogEntry::ObjectOwnerOffset);
    return value == 0 ? 0x418u : value;
}

uint32_t BodyOffset()
{
    const auto value = ResolveNativeCatalogRva(NativeCatalogEntry::BodyOffset);
    return value == 0 ? 0x33Cu : value;
}

uint32_t StructureUnpackCompletionTickOffset()
{
    if (!HasNativeCatalog())
    {
        return 0;
    }

    // The equivalent StructureUnpackUpdate field is +0x3C in RA3 and +0x34
    // in Uprising. ObjectOwnerOffset already distinguishes the two layouts
    // across all four supported profiles; reject unknown layouts instead of
    // writing an unrelated module field.
    switch (ResolveNativeCatalogRva(NativeCatalogEntry::ObjectOwnerOffset))
    {
    case 0x418u:
        return 0x3Cu;
    case 0x428u:
        return 0x34u;
    default:
        return 0;
    }
}

// RA3 KindOf BitWord lives at ThingTemplate+0xC0 (9 DWORDs, 288 bits).
// Verified via PartitionFilter_IsKindOf_6FE370:
//   bit set  ==  (1 << (index & 31)) & template->kindOf[index >> 5]
// GameObject+0x4 = ThingTemplate* (see RA3_Analysis object_system docs).
//
// Projectile kinds that must NOT receive GodMode protection. These transient
// objects die on impact/expiry through the health path (Body_ApplyHealthChange).
// Locking their health prevents cleanup, which makes weapon detonation VFX loop
// every frame.
//   PROJECTILE        = 26  -> word0 bit26 mask 0x04000000
//   SMALL_MISSILE     = 53  -> word1 bit21 mask 0x00200000
//   BALLISTIC_MISSILE = 75  -> word2 bit11 mask 0x00000800
bool IsProjectileObject(uint32_t object)
{
    uint32_t thingTemplate = 0;
    if (!TryRead(object + 0x4u, thingTemplate) || thingTemplate == 0)
    {
        return false;
    }
    uint32_t kindOf0 = 0;
    uint32_t kindOf1 = 0;
    uint32_t kindOf2 = 0;
    TryRead(thingTemplate + 0xC0u, kindOf0);
    TryRead(thingTemplate + 0xC4u, kindOf1);
    TryRead(thingTemplate + 0xC8u, kindOf2);
    return (kindOf0 & 0x04000000u) != 0 ||  // PROJECTILE
           (kindOf1 & 0x00200000u) != 0 ||  // SMALL_MISSILE
           (kindOf2 & 0x00000800u) != 0;    // BALLISTIC_MISSILE
}

uint32_t GameModuleAddress(NativeCatalogEntry entry)
{
    const auto moduleBase = static_cast<uint32_t>(
        reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)));
    const auto rva = ResolveNativeCatalogRva(entry);
    return moduleBase != 0 && rva != 0 ? moduleBase + rva : 0;
}

bool TryWriteLogicPauseGate(uint32_t value)
{
    uint32_t gameLogic = 0;
    const auto gameLogicPointer = GameModuleAddress(NativeCatalogEntry::GameClientPointer);
    return TryRead(gameLogicPointer, gameLogic) && gameLogic != 0 &&
        TryWrite(gameLogic + 0x15Cu, value);
}

bool IsLocalContext(uint32_t candidate)
{
    if (candidate == 0)
    {
        return false;
    }
    const auto player = static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0));
    const auto siblingOffset = ResolveNativeCatalogRva(NativeCatalogEntry::LocalContextSiblingOffset);
    if (siblingOffset == 0)
    {
        return candidate == ResolveCurrentPlayerPointer();
    }
    if (player == 0)
    {
        return false;
    }
    return candidate == player || candidate == player + siblingOffset;
}

bool IsLocalPlacementCandidate(uint32_t manager, uint32_t candidate)
{
    uint32_t current = 0;
    if (manager == 0 || candidate == 0 || !TryRead(manager + 0x28u, current) || current == 0)
    {
        return false;
    }

    uint8_t flag106 = 0;
    uint8_t flag123C = 0;
    if (TryRead(current + 0x106u, flag106) &&
        TryRead(current + 0x123Cu, flag123C) &&
        (flag106 != 0 || flag123C != 0))
    {
        uint32_t alternate = 0;
        if (TryRead(manager + 0x80u, alternate) && alternate != 0)
        {
            current = alternate;
        }
    }

    const auto siblingOffset = ResolveNativeCatalogRva(NativeCatalogEntry::LocalContextSiblingOffset);
    if (siblingOffset == 0)
    {
        return candidate == ResolveCurrentPlayerPointer();
    }
    return candidate == current ||
        candidate == current + siblingOffset;
}

bool IsLocalDependency(uint32_t dependency)
{
    uint32_t object = 0;
    uint32_t component = 0;
    uint32_t owner = 0;
    return TryRead(dependency, object) && object != 0 &&
        TryRead(dependency + 4, component) && component != 0 &&
        TryRead(component + OwnerOffset(), owner) &&
        (owner == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)) ||
         owner == ReadPlayerOwnerId());
}

void CapturePlayer(NativeHookContext& context)
{
    uint32_t player = 0;
    uint32_t owner = 0;
    if (TryRead(context.Eax + 0x28, player) && TryRead(player + 0x20, owner))
    {
        InterlockedExchange(&g_playerObject, static_cast<LONG>(player));
        InterlockedExchange(&g_playerOwnerId, static_cast<LONG>(owner));
    }
}

void ApplyPlayerMoney(NativeHookContext& context)
{
    AgentGameThreadDispatcher::Pump();
    if (ConsumeNativeFeatureState(NativeFeatureStateId::MoneyPulse) != 0)
    {
        NotifyPulseFired(NativeFeatureStateId::MoneyPulse);
        uint32_t money = 0;
        if (TryRead(context.Eax + 4, money))
        {
            TryWrite(context.Eax + 4, money + ReadNativeFeatureState(NativeFeatureStateId::MoneyAmount));
        }
    }
}

void ResetPowerChain(uint32_t node)
{
    for (uint32_t index = 0; index < 4 && node != 0; ++index)
    {
        TryWrite(node + 0x0Cu, 0u);
        uint32_t next = 0;
        if (!TryRead(node, next))
        {
            break;
        }
        node = next;
    }
}

// Lazy-allocated executable stub for WeaponTemplate_GetWeaponActualRange_713770.
// Layout: D9 05 <abs addr> C2 08 00 | <4 bytes float const 0x461C4000 = 10000.0f>
// Execution: fld dword ptr [const]; retn 8.
// Never freed; lives for the Agent DLL lifetime.
uint32_t GetAttackRangeReturnStub()
{
    static uint32_t stub = 0;
    if (stub != 0)
    {
        return stub;
    }
    auto* mem = static_cast<unsigned char*>(
        VirtualAlloc(nullptr, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (mem == nullptr)
    {
        return 0;
    }
    const uint32_t floatAddress = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(mem)) + 9u;
    mem[0] = 0xD9; mem[1] = 0x05;                         // fld dword ptr [imm32]
    std::memcpy(mem + 2, &floatAddress, sizeof(floatAddress));
    mem[6] = 0xC2; mem[7] = 0x08; mem[8] = 0x00;          // retn 8
    std::memcpy(mem + 9, &kUnlimitedAttackRangeBits, sizeof(kUnlimitedAttackRangeBits));
    FlushInstructionCache(GetCurrentProcess(), mem, 13);
    stub = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(mem));
    return stub;
}

bool ExtendSelectedUnitAutoAcquireLocals(const NativeHookContext& c)
{
    uint32_t owner = 0;
    if (!TryRead(c.Ebx + 0x80u, owner) || !IsAttackRangeObject(owner))
    {
        return false;
    }

    TryWrite(c.OriginalEsp + 0x0Cu, kUnlimitedAttackRangeBits);
    TryWrite(c.OriginalEsp + 0x1Cu, kUnlimitedAttackRangeBits);
    return true;
}

bool IsSelectedUnitTurretAI(uint32_t turretAi)
{
    uint32_t owner = 0;
    return TryRead(turretAi + 0x5Cu, owner) && IsAttackRangeObject(owner);
}

uint32_t HandleHook(uint32_t hookId, NativeHookContext& c)
{
    switch (hookId)
    {
    case 1:
        CapturePlayer(c);
        break;
    case 2:
        ApplyPlayerMoney(c);
        break;
    case 3:
        if (IsEnabled(NativeFeatureStateId::Power))
        {
            TryWrite(c.Eax + 4, ReadNativeFeatureState(NativeFeatureStateId::PowerValue));
            TryWrite(c.Eax + 8, 0u);
        }
        break;
    case 4:
        if (IsEnabled(NativeFeatureStateId::SecretProtocolPoints))
        {
            TryWrite(c.Eax + 0x34, ReadNativeFeatureState(NativeFeatureStateId::SecretProtocolPointValue));
        }
        break;
    case 5:
        if (IsEnabled(NativeFeatureStateId::AllSecretProtocols))
        {
            TryWrite(c.Edi + 0x2C, 399.0f);
        }
        break;
    case 9:
        if (IsEnabled(NativeFeatureStateId::FastBuild))
        {
            const auto completionTickOffset = StructureUnpackCompletionTickOffset();
            uint32_t object = 0;
            uint32_t owner = 0;
            uint32_t value = 0;
            if (completionTickOffset != 0 &&
                TryRead(c.Edi + 8u, object) && object != 0 &&
                TryRead(object + OwnerOffset(), owner) &&
                owner == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)) &&
                TryRead(c.Edi + completionTickOffset, value))
            {
                TryWrite(c.Edi + 0x14, value + 1);
            }
        }
        break;
    case 10:
    {
        uint32_t owner = 0;
        if (TryRead(c.Eax + OwnerOffset(), owner))
        {
            if (IsEnabled(NativeFeatureStateId::SuperPower) &&
                owner == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)))
            {
                TryWrite(c.Esi + 0x20, 1u);
            }
            if (IsEnabled(NativeFeatureStateId::DisableAllSuperPowers) &&
                owner != static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)))
            {
                uint32_t value = 0;
                if (TryRead(c.Esi + 0x20, value)) TryWrite(c.Esi + 0x20, value + 1);
            }
        }
        break;
    }
    case 11:
    {
        uint32_t node = 0;
        if (TryRead(c.Ecx, node))
        {
            const bool local = node == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0));
            if ((local && IsEnabled(NativeFeatureStateId::SuperPower)) ||
                (!local && IsEnabled(NativeFeatureStateId::DisableAllSuperPowers)))
            {
                ResetPowerChain(node);
            }
        }
        break;
    }
    case 12:
        if (IsEnabled(NativeFeatureStateId::DisableAllSuperPowers) &&
            c.Ecx != static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)))
        {
            c.Eax = 0;
            return 3;
        }
        break;
    case 13:
        if (IsEnabled(NativeFeatureStateId::DisableAllSuperPowers))
        {
            uint32_t object = 0;
            uint32_t owner = 0;
            if (TryRead(c.Ecx - 8u, object) && object != 0 &&
                TryRead(object + OwnerOffset(), owner) &&
                owner != static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)))
            {
                c.Eax = 0;
                return 2;
            }
        }
        break;
    case 14:
        // Original semantics:
        //   test bl,bl; jne allowed; mov eax,2 (blocked)
        // The trainer may turn the BL==0 blocked path into allowed only when the placement
        // candidate belongs to the local player. Returning mode 1 skips to hook+9 after
        // setting EAX=2; returning the absolute hook+0x10 target follows the allowed branch.
        if ((c.Ebx & 0xFFu) != 0)
        {
            return g_hookAddresses[hookId] + 0x10u;
        }

        if (IsEnabled(NativeFeatureStateId::FreeBuild))
        {
            uint32_t candidateIndex = 0;
            uint32_t manager = 0;
            uint32_t candidate = 0;
            const auto managerAddress = GameModuleAddress(NativeCatalogEntry::PlayerManager);
            if (TryRead(c.OriginalEsp + 0x14u, candidateIndex) && candidateIndex < 0x14u &&
                TryRead(managerAddress, manager) && manager != 0 &&
                TryRead(manager + candidateIndex * sizeof(uint32_t) + 0x30u, candidate) &&
                IsLocalPlacementCandidate(manager, candidate))
            {
                return g_hookAddresses[hookId] + 0x10u;
            }
        }

        c.Eax = 2;
        return 1;
    case 15:
        if (IsEnabled(NativeFeatureStateId::SecretProtocolDependencyBypass) &&
            c.Ebp == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)))
        {
            c.Eax = 0;
            return 1;
        }
        break;
    case 16:
        if (IsEnabled(NativeFeatureStateId::IgnorePrerequisites))
        {
            uint32_t stackCandidate = 0;
            TryRead(c.OriginalEsp + 0x1Cu, stackCandidate);
            if (IsLocalContext(c.Ecx) || IsLocalContext(c.Edi) ||
                IsLocalContext(c.Ebx) || IsLocalContext(stackCandidate))
            {
                c.Eax = 1;
                return 2;
            }
        }
        break;
    case 17:
    case 18:
    case 19:
    case 20:
    case 21:
        if (IsEnabled(NativeFeatureStateId::IgnorePrerequisites))
        {
            uint32_t dependency = 0;
            if (TryRead(c.OriginalEsp, dependency) && IsLocalDependency(dependency))
            {
                c.Eax = 0;
                return 4;
            }
        }
        break;
    case 22:
    case 23:
        if (IsEnabled(NativeFeatureStateId::IgnorePrerequisites) && IsLocalContext(c.Ecx))
        {
            uint32_t argument = 0;
            if (TryRead(c.OriginalEsp + 4u, argument) && argument != 0)
            {
                c.Eax = 1;
                return 3;
            }
        }
        break;
    case 24:
        if (IsEnabled(NativeFeatureStateId::IgnoreQuantityLimit))
        {
            uint32_t owner = 0;
            if (TryRead(c.OriginalEsp + 4u, owner) && IsLocalContext(owner))
            {
                c.Eax = 0;
                return 2;
            }
        }
        break;
    case 25:
        // The legacy hook approximated unclamped zoom with x87 arithmetic. The actual
        // clamp is now isolated in hook 42, so this original write remains unchanged.
        break;
    case 26:
        if (IsEnabled(NativeFeatureStateId::RevealMap))
        {
            TryWrite(c.Ebp + 0x260, 100000.0f);
            return 1;
        }
        break;
    case 28:
    {
        uint32_t danger = 0;
        if (TryRead(c.Ecx + 0x1278, danger))
        {
            const auto mode = ReadNativeFeatureState(NativeFeatureStateId::DangerLevelMode);
            if (mode == 1) TryWrite(danger + 8, 50000u);
            if (mode == 2) TryWrite(danger + 8, 0u);
        }
        break;
    }
    case 29:
        // Old ASM unconditionally refills the ore amount, and only clears the cooldown
        // when the pulse fires and no build is pending.
        if (ResolveNativeCatalogRva(NativeCatalogEntry::RestoreOreCapacityMode) == 2u)
        {
            TryWrite(c.Ecx + 0x28u, 100000u);
        }
        else
        {
            TryWrite(c.Eax + 8u, 100000u);
        }
        if (c.Ebx == 0 && ConsumeNativeFeatureState(NativeFeatureStateId::RestoreOrePulse) != 0)
        {
            NotifyPulseFired(NativeFeatureStateId::RestoreOrePulse);
            TryWrite(c.Ecx + 0x14, 0u);
        }
        break;
    case 30:
        if (IsEnabled(NativeFeatureStateId::FastBuild))
        {
            uint32_t ownerId = 0;
            uint32_t total = 0;
            uint32_t consumed = 0;
            if (TryRead(c.Eax + 0x0Cu, ownerId) && ownerId == ReadPlayerOwnerId() &&
                TryRead(c.Esi + 0x30u, total) && TryRead(c.Esi + 0x68u, consumed))
            {
                c.Edi = total - consumed;
                TryWrite(c.OriginalEsp + 0x18u, 100.0f);
            }
        }
        if (IsEnabled(NativeFeatureStateId::EnemyCannotBuild))
        {
            uint32_t owner = 0;
            if (TryRead(c.Eax + 0x0C, owner) && owner != ReadPlayerOwnerId())
            {
                TryWrite(c.Eax + 4, 1u);
            }
        }
        break;
    case 31:
    {
        if (IsEnabled(NativeFeatureStateId::GodMode))
        {
            uint32_t object = 0;
            uint32_t owner = 0;
            if (TryRead(c.Ebx + 0x138, object) &&
                TryRead(object + OwnerOffset(), owner) &&
                owner == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)) &&
                !IsProjectileObject(object))
            {
                const auto lockedHealth = ReadNativeFeatureState(NativeFeatureStateId::SelectedUnitMaxHealthBits);
                TryWrite(c.Esi + 4, lockedHealth);
                // Sync maxHealth so display ratio health/maxHealth stays 1.0.
                // Without this, health=9999999f with original maxHealth (e.g. 500)
                // produces ratio >> 1, which can show as empty health bar.
                TryWrite(c.Esi + 0xC, lockedHealth);
            }
        }
        // Auto-repair pulse is processed in the same hook as GodMode (old MustCode+0x1220
        // subroutine was called from the GodMode trampoline). Consume the pulse and apply
        // 2% max-health restoration when both the feature and pulse are set.
        if (ConsumeNativeFeatureState(NativeFeatureStateId::AutoRepairPulse) != 0 &&
            IsEnabled(NativeFeatureStateId::AutoRepair))
        {
            NotifyPulseFired(NativeFeatureStateId::AutoRepairPulse);
            uint32_t object = 0;
            uint32_t owner = 0;
            uint32_t body = 0;
            float currentHp = 0.0f;
            float maxHp = 0.0f;
            if (TryRead(c.Ebx + 0x138, object) && object != 0 &&
                TryRead(object + OwnerOffset(), owner) &&
                owner == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)) &&
                !IsProjectileObject(object) &&
                TryRead(object + BodyOffset(), body) && body != 0 &&
                TryRead(c.Esi + 4, currentHp) && currentHp > 0.0f &&
                TryRead(body + 0x20, maxHp))
            {
                const float repair = maxHp * 0.02f;
                TryWrite(c.Esi + 4, (std::min)(currentHp + repair, maxHp));
            }
        }
        break;
    }
    case 32:
    {
        if (IsEnabled(NativeFeatureStateId::OneHitKill))
        {
            uint32_t object = 0;
            uint32_t owner = 0;
            uint32_t ownerId = 0;
            uint32_t deltaBits = 0;
            const auto currentOwner = static_cast<uint32_t>(
                InterlockedCompareExchange(&g_oneHitOwner, 0, 0));
            const bool damageDeltaMode =
                ResolveNativeCatalogRva(NativeCatalogEntry::OneHitDamageDeltaMode) != 0;
            bool eligible = false;
            if (damageDeltaMode)
            {
                eligible = TryRead(c.OriginalEsp + 0x0Cu, deltaBits) &&
                    (deltaBits & 0x80000000u) != 0;
            }
            else
            {
                const auto currentCaller = static_cast<uint32_t>(
                    InterlockedCompareExchange(&g_oneHitCaller, 0, 0));
                eligible = c.Esi == currentOwner ||
                    (currentCaller != 0 &&
                     (currentCaller == GameModuleAddress(NativeCatalogEntry::OneHitCaller1) ||
                      currentCaller == GameModuleAddress(NativeCatalogEntry::OneHitCaller2) ||
                      currentCaller == GameModuleAddress(NativeCatalogEntry::OneHitCaller3)));
            }
            if (eligible && TryRead(c.Esi - 8u, object) && object != 0 &&
                TryRead(object + OwnerOffset(), owner) && owner != 0 &&
                TryRead(owner + 0x20u, ownerId) && ownerId != ReadPlayerOwnerId())
            {
                TryWrite(c.Esi + 4u, 0u);
                return 1;
            }
        }
        // GodMode health lock at damage-time: intercepts the health write
        // [Body+4] before damage is applied, regardless of screen visibility.
        // Reads [esi-8] for GameObject (same pattern as OneHitKill above).
        if (IsEnabled(NativeFeatureStateId::GodMode))
        {
            uint32_t object = 0;
            uint32_t owner = 0;
            if (TryRead(c.Esi - 8u, object) && object != 0 &&
                TryRead(object + OwnerOffset(), owner) && owner != 0 &&
                owner == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)) &&
                !IsProjectileObject(object))
            {
                const auto lockedHealth = ReadNativeFeatureState(NativeFeatureStateId::SelectedUnitMaxHealthBits);
                TryWrite(c.Esi + 4u, lockedHealth);
                // Sync maxHealth to prevent display-ratio overflow
                // when health >> maxHealth (visible as empty health bar).
                TryWrite(c.Esi + 0xCu, lockedHealth);
                return 1;
            }
        }
        break;
    }
    case 33:
    {
        uint32_t owner = 0;
        if (TryRead(c.Esi + BodyOffset(), owner))
        {
            InterlockedExchange(&g_oneHitBody, static_cast<LONG>(c.Esi));
            InterlockedExchange(&g_oneHitOwner, static_cast<LONG>(owner));
        }
        break;
    }
    case 34:
    {
        uint32_t owner = 0;
        if (TryRead(c.Ecx + BodyOffset(), owner))
        {
            InterlockedExchange(&g_oneHitBody, static_cast<LONG>(c.Ecx));
            InterlockedExchange(&g_oneHitOwner, static_cast<LONG>(owner));
        }
        break;
    }
    case 35:
    {
        uint32_t caller = 0;
        if (TryRead(c.OriginalEsp + 4u, caller))
        {
            InterlockedExchange(&g_oneHitCaller, static_cast<LONG>(caller));
        }
        break;
    }
    case 36:
    {
        uint32_t caller = 0;
        if (TryRead(c.OriginalEsp, caller))
        {
            InterlockedExchange(&g_oneHitCaller, static_cast<LONG>(caller));
        }
        break;
    }
    case 37:
        if (IsEnabled(NativeFeatureStateId::ChallengeTime))
        {
            TryWrite(c.Eax + 0x38, 599999.0f);
        }
        break;
    case 38:
        if (ConsumeNativeFeatureState(NativeFeatureStateId::ChallengeMoneyPulse) != 0)
        {
            NotifyPulseFired(NativeFeatureStateId::ChallengeMoneyPulse);
            uint32_t value = 0;
            if (TryRead(c.Eax + 8, value)) TryWrite(c.Eax + 8, value + 50000u);
        }
        break;
    case 39:
        if (IsEnabled(NativeFeatureStateId::RunInBackground))
        {
            return 1;
        }
        break;
    case 40:
    {
        // WeaponStateMachine_ScaleDuration(this, RangeDuration* a2, RangeDuration* a3)
        // is __fastcall returning __int64 in EDX:EAX and callee-cleans 2 stack args (retn 8).
        // The owner chain [this+0x18]=StateMachine -> [+0x24]=GameObject is consistent
        // across all four profiles. Earlier notes misidentified these two objects as a
        // WeaponSlot and weapon component; the four current IDBs correct that model.
        //
        // Returning mode 5 (retn 8) short-circuits the whole function. EDX:EAX is set to
        // {0, 1} = 1 tick. This avoids the RATE_OF_FIRE modifier division inside the
        // function body: clamping only the input durations (min=max=1) still produces
        // (int)(1 / rateOfFireMultiplier), which drops to 0 when the unit has veteran
        // fire-rate bonuses (multiplier > 1.0). Callers treat result==0 as a
        // state-transition sentinel and leave the unit permanently stuck.
        //
        // The hot path tests bit 0 in the owner GameObject's verified padding word.
        uint32_t stateMachine = 0;
        uint32_t gameObject = 0;
        if (TryRead(c.Ecx + 0x18u, stateMachine) && stateMachine != 0 &&
            TryRead(stateMachine + 0x24u, gameObject) && gameObject != 0 &&
            IsAttackSpeedObject(gameObject))
        {
            c.Eax = 1u;
            c.Edx = 0u;
            return 5;
        }
        break;
    }
    case 42:
        // This clamp is shared. Only relax the call reached 0x39 bytes after the
        // profile-specific player-zoom hook; unrelated clamp callers stay untouched.
        if (IsEnabled(NativeFeatureStateId::Zoom))
        {
            uint32_t caller = 0;
            const auto zoomHook = g_hookAddresses[25];
            if (zoomHook != 0 && TryRead(c.OriginalEsp, caller) && caller == zoomHook + 0x39u)
            {
                TryWrite(c.OriginalEsp + 8u, 0xFF7FFFFFu);
                TryWrite(c.OriginalEsp + 0xCu, 0x7F7FFFFFu);
            }
        }
        break;
    case 43:
    {
        // WeaponTemplate_GetWeaponActualRange_713770 is the common numeric range
        // source used by the in-range predicate and range-planning helpers. It is
        // __thiscall with two callee-cleaned stack args (retn 8):
        //   ECX = WeaponTemplate*, [entry ESP+4] = owner GameObject*.
        // Returning 10000.0f in ST0 extends maximum range while preserving the
        // caller's minimum-range, contact geometry, status, LOS and target gates.
        const auto stub = GetAttackRangeReturnStub();
        uint32_t owner = 0;
        if (stub != 0 && TryRead(c.OriginalEsp + 4u, owner) && IsAttackRangeObject(owner))
        {
            return stub;
        }
        break;
    }
    case 44:
        // Logic Time Freeze + Slow Motion (pseudo-turn-based / micro-control).
        // Both reuse the same seam: [GameLogic+0x15C] (PauseManager pause-type).
        // Nonzero freezes sim; ~15 frame-advance/anim/main-loop sites gate on it.
        // We bypass DoPause to skip the UI/input ceremony — player keeps full
        // real-time command authority. Returns mode 0 (side-effect write only).
        //
        // Freeze: write 1 every frame → sim fully frozen, input responsive.
        // Slow-motion: alternate 0/1 every frame → sim advances 1 of every 2
        //   frames → 50% real speed. Input always responsive.
        // Priority: freeze > slow-motion. Disable transition clears our write
        //   once; when both inactive we touch nothing (respect game's own pause).
        if (IsEnabled(NativeFeatureStateId::LogicTimeFreeze))
        {
            TryWriteLogicPauseGate(1u);
            g_logicFreezeActive = true;
            g_slowMotionActive = false;
        }
        else if (IsEnabled(NativeFeatureStateId::SlowMotionMode))
        {
            g_slowMotionFrameCounter++;
            uint32_t gateValue = (g_slowMotionFrameCounter & 1u) ? 1u : 0u;
            TryWriteLogicPauseGate(gateValue);
            g_slowMotionActive = true;
            g_logicFreezeActive = false;
        }
        else
        {
            if (g_logicFreezeActive || g_slowMotionActive)
            {
                TryWriteLogicPauseGate(0u);
                g_logicFreezeActive = false;
                g_slowMotionActive = false;
                g_slowMotionFrameCounter = 0;
            }
        }
        break;
    case 45:
    {
        // BaseAITargetChooser_FindEnemyTargetInternal_836A80 computes two
        // independent vision-limited values before querying the partition:
        //   [entry ESP+0x0C] = partition search radius
        //   [entry ESP+0x1C] = UnitAITargetChooser distance recheck
        // EBX remains the chooser, whose owner GameObject* is at +0x80.
        // Extend both values only for owners already enabled by the selected-unit
        // attack-range registry. The existing shroud, stealth, relation, target,
        // weapon and turret-arc filters continue to run unchanged.
        ExtendSelectedUnitAutoAcquireLocals(c);
        break;
    }
    case 46:
    {
        // This seam is the mode-2 branch taken when LargestWeaponRange exceeds
        // the original vision-derived radius. For enabled owners, extend the
        // locals and jump to the same distance-filter setup used by mode 1.
        // Hooking after the x87 comparison avoids carrying ST0 across C++ code.
        if (ExtendSelectedUnitAutoAcquireLocals(c) && g_hookAddresses[46] != 0)
        {
            return g_hookAddresses[46] + 0x1Au; // 0x836E1B -> 0x836E35
        }
        break;
    }
    case 47:
    {
        // TurretAI_IsInTurretAngle is shared by candidate selection, ordering and
        // the final aim-state validation. Treat the selected owner's turret as
        // unrestricted, then return through the function's original true epilogue.
        // The entry prologue has already saved ECX/ESI, so the epilogue remains ABI-safe.
        if (IsSelectedUnitTurretAI(c.Esi) && g_hookAddresses[47] != 0)
        {
            return g_hookAddresses[47] + 0xAAu; // 0x7F2944 -> 0x7F29EE
        }
        break;
    }
    case 48:
    {
        // TurretAI's turn routine has already normalized the requested target angle
        // and stored it in its local before this native max-deflection branch. Reuse
        // the game's full-circle path for selected owners so the turret still turns
        // at its normal rate and must actually reach the target before firing.
        if (IsSelectedUnitTurretAI(c.Esi) && g_hookAddresses[48] != 0)
        {
            return g_hookAddresses[48] + 0xE1u; // 0x80DF79 -> 0x80E05A
        }
        break;
    }
    case 49:
    {
        // GameLogic_RegisterObject(this, GameObject*) entry. Object-pool allocation
        // does not zero memory, so clear both trainer bits before list insertion.
        uint32_t gameObject = 0;
        if (TryRead(c.OriginalEsp + 4u, gameObject) && gameObject != 0)
        {
            ClearWeaponObjectFlagsForRegisteredObject(gameObject);
        }
        break;
    }
    default:
        break;
    }
    return 0;
}
}

void RegisterNativeHookAddress(uint32_t hookId, uint32_t address, uint32_t continuation)
{
    if (hookId < kHookCapacity)
    {
        g_hookAddresses[hookId] = address;
        g_hookContinuations[hookId] = continuation;
    }
}

uint32_t ReadCapturedPlayerObject()
{
    return static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0));
}

void ResetNativeHookRuntime()
{
    ResetNativeFeatureStates();
    ResetNativeGameApiRuntimeState();
    std::memset(g_hookAddresses, 0, sizeof(g_hookAddresses));
    std::memset(g_hookContinuations, 0, sizeof(g_hookContinuations));
    InterlockedExchange(&g_playerObject, 0);
    InterlockedExchange(&g_playerOwnerId, 0);
    InterlockedExchange(&g_oneHitBody, 0);
    InterlockedExchange(&g_oneHitOwner, 0);
    InterlockedExchange(&g_oneHitCaller, 0);
}
}

extern "C" uint32_t __cdecl AgentNativeHookHandler(
    uint32_t hookId,
    RayaTrainer::agent::NativeHookContext* context)
{
    if (context == nullptr)
    {
        return 0;
    }
    __try
    {
        return RayaTrainer::agent::HandleHook(hookId, *context);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return 0;
    }
}
