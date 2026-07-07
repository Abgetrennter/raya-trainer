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
uint32_t g_hookAddresses[kHookCapacity] = {};
uint32_t g_hookContinuations[kHookCapacity] = {};
volatile LONG g_playerObject = 0;
volatile LONG g_playerOwnerId = 0;
volatile LONG g_oneHitBody = 0;
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

uint32_t GameModuleAddress(NativeCatalogEntry entry)
{
    const auto moduleBase = static_cast<uint32_t>(
        reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)));
    const auto rva = ResolveNativeCatalogRva(entry);
    return moduleBase != 0 && rva != 0 ? moduleBase + rva : 0;
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
            uint32_t object = 0;
            uint32_t owner = 0;
            uint32_t value = 0;
            if (TryRead(c.Edi + 8u, object) && object != 0 &&
                TryRead(object + OwnerOffset(), owner) &&
                owner == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)) &&
                TryRead(c.Edi + 0x3C, value))
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
                owner == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)))
            {
                TryWrite(c.Esi + 4, ReadNativeFeatureState(NativeFeatureStateId::SelectedUnitMaxHealthBits));
            }
        }
        // Auto-repair pulse is processed in the same hook as GodMode (old MustCode+0x1220
        // subroutine was called from the GodMode trampoline). Consume the pulse and apply
        // 2% max-health restoration when both the feature and pulse are set.
        if (ConsumeNativeFeatureState(NativeFeatureStateId::AutoRepairPulse) != 0 &&
            IsEnabled(NativeFeatureStateId::AutoRepair))
        {
            uint32_t object = 0;
            uint32_t owner = 0;
            uint32_t body = 0;
            float currentHp = 0.0f;
            float maxHp = 0.0f;
            if (TryRead(c.Ebx + 0x138, object) && object != 0 &&
                TryRead(object + OwnerOffset(), owner) &&
                owner == static_cast<uint32_t>(InterlockedCompareExchange(&g_playerObject, 0, 0)) &&
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
        uint32_t state = 0;
        uint32_t component = 0;
        if (TryRead(c.Ecx + 0x18u, state) && state != 0 &&
            TryRead(state + 0x24u, component) && component != 0 &&
            IsAttackSpeedComponentRegistered(component))
        {
            TryWrite(c.OriginalEsp + 4u, 1u);
            TryWrite(c.OriginalEsp + 8u, 1u);
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
