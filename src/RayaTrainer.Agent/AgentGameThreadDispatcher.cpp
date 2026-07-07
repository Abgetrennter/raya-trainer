#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstring>

#include "AgentGameThreadDispatcher.h"

namespace RayaTrainer::agent
{
namespace
{
enum class RequestState : LONG
{
    Idle = 0,
    Pending = 1,
    Running = 2,
    Completed = 3,
    Abandoned = 4
};

volatile LONG g_requestState = static_cast<LONG>(RequestState::Idle);
AgentGameThreadRequest g_request = {};
AgentGameThreadResult g_result = {};
volatile LONG g_gameThreadId = 0;
volatile LONG g_pumpTick = 0;
}

AgentGameThreadDispatchStatus AgentGameThreadDispatcher::Dispatch(
    const AgentGameThreadRequest& request,
    uint32_t timeoutMilliseconds,
    AgentGameThreadResult& result)
{
    result = {};
    if (request.Work == nullptr || timeoutMilliseconds == 0)
    {
        return AgentGameThreadDispatchStatus::Failed;
    }

    if (InterlockedCompareExchange(
            &g_requestState,
            static_cast<LONG>(RequestState::Running),
            static_cast<LONG>(RequestState::Idle)) != static_cast<LONG>(RequestState::Idle))
    {
        return AgentGameThreadDispatchStatus::Busy;
    }

    g_request = request;
    g_result = {};
    MemoryBarrier();
    InterlockedExchange(&g_requestState, static_cast<LONG>(RequestState::Pending));

    const auto startedAt = GetTickCount();
    for (;;)
    {
        const auto state = static_cast<RequestState>(
            InterlockedCompareExchange(&g_requestState, 0, 0));
        if (state == RequestState::Completed)
        {
            MemoryBarrier();
            result = g_result;
            InterlockedExchange(&g_requestState, static_cast<LONG>(RequestState::Idle));
            return result.Status;
        }

        if (GetTickCount() - startedAt > timeoutMilliseconds)
        {
            if (InterlockedCompareExchange(
                    &g_requestState,
                    static_cast<LONG>(RequestState::Idle),
                    static_cast<LONG>(RequestState::Pending)) == static_cast<LONG>(RequestState::Pending))
            {
                result.Status = AgentGameThreadDispatchStatus::TimedOut;
                return result.Status;
            }

            // The worker may stop waiting after the game thread has started, but the
            // shared request storage cannot become reusable until that invocation returns.
            if (InterlockedCompareExchange(
                    &g_requestState,
                    static_cast<LONG>(RequestState::Abandoned),
                    static_cast<LONG>(RequestState::Running)) == static_cast<LONG>(RequestState::Running))
            {
                result.Status = AgentGameThreadDispatchStatus::TimedOut;
                return result.Status;
            }
        }

        Sleep(1);
    }
}

void AgentGameThreadDispatcher::Pump()
{
    InterlockedIncrement(&g_pumpTick);
    InterlockedCompareExchange(
        &g_gameThreadId,
        static_cast<LONG>(GetCurrentThreadId()),
        0);

    if (InterlockedCompareExchange(
            &g_requestState,
            static_cast<LONG>(RequestState::Running),
            static_cast<LONG>(RequestState::Pending)) != static_cast<LONG>(RequestState::Pending))
    {
        return;
    }

    AgentGameThreadResult result = {};
    result.GameThreadId = static_cast<uint32_t>(g_gameThreadId);
    __try
    {
        g_request.Work(g_request.Arguments, result.Values);
        result.Status = AgentGameThreadDispatchStatus::Completed;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        result.Status = AgentGameThreadDispatchStatus::Failed;
    }

    g_result = result;
    MemoryBarrier();
    const auto previous = static_cast<RequestState>(InterlockedCompareExchange(
        &g_requestState,
        static_cast<LONG>(RequestState::Completed),
        static_cast<LONG>(RequestState::Running)));
    if (previous == RequestState::Abandoned)
    {
        g_request = {};
        g_result = {};
        InterlockedExchange(&g_requestState, static_cast<LONG>(RequestState::Idle));
    }
}

void AgentGameThreadDispatcher::Reset()
{
    const auto state = static_cast<RequestState>(
        InterlockedCompareExchange(&g_requestState, 0, 0));
    if (state != RequestState::Running && state != RequestState::Abandoned)
    {
        g_request = {};
        g_result = {};
        InterlockedExchange(&g_requestState, static_cast<LONG>(RequestState::Idle));
    }
    InterlockedExchange(&g_gameThreadId, 0);
    InterlockedExchange(&g_pumpTick, 0);
}

uint32_t AgentGameThreadDispatcher::GameThreadId()
{
    return static_cast<uint32_t>(InterlockedCompareExchange(&g_gameThreadId, 0, 0));
}

uint32_t AgentGameThreadDispatcher::PumpTick()
{
    return static_cast<uint32_t>(InterlockedCompareExchange(&g_pumpTick, 0, 0));
}
}
