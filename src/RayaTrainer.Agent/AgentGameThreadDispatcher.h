#pragma once

#include <cstdint>

namespace RayaTrainer::agent
{
using AgentGameThreadWork = void(*)(const uint32_t* arguments, uint32_t* results);

enum class AgentGameThreadDispatchStatus : uint32_t
{
    Completed = 0,
    Busy = 1,
    TimedOut = 2,
    Failed = 3
};

struct AgentGameThreadRequest
{
    AgentGameThreadWork Work = nullptr;
    uint32_t Arguments[8] = {};
};

struct AgentGameThreadResult
{
    AgentGameThreadDispatchStatus Status = AgentGameThreadDispatchStatus::Failed;
    // 24 slots: [0]=success/aux, [1..3]=unit metadata, [3]=count, [4..23]=up to 20 upgrade
    // hashes. The game-thread handler writes only into this dispatcher-owned storage; the
    // pipe worker never passes caller-stack pointers through Arguments. Widened 8 -> 24 for
    // GetSelectedUnitUpgrades (protocol v10). Struct assignment copies all 24 verbatim.
    uint32_t Values[24] = {};
    uint32_t GameThreadId = 0;
};

class AgentGameThreadDispatcher final
{
public:
    static AgentGameThreadDispatchStatus Dispatch(
        const AgentGameThreadRequest& request,
        uint32_t timeoutMilliseconds,
        AgentGameThreadResult& result);

    static void Pump();
    static void Reset();
    static uint32_t GameThreadId();
    static uint32_t PumpTick();
};
}

extern "C" void __cdecl AgentGameThreadPumpHandler();
extern "C" void __cdecl AgentGameThreadHookStub();
extern "C" volatile uint32_t g_AgentGameThreadContinuation;
