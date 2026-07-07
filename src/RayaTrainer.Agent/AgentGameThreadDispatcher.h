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
    uint32_t Values[8] = {};
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
