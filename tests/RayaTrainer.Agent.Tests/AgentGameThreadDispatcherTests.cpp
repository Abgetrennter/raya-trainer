#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>
#include <iostream>
#include <thread>

#include "../../src/RayaTrainer.Agent/AgentGameThreadDispatcher.h"

extern int RunNativePointerSetTests();

namespace
{
int g_failures = 0;

void Expect(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << "FAILED: " << message << '\n';
        ++g_failures;
    }
}

void AddOnGameThread(const uint32_t* arguments, uint32_t* results)
{
    results[0] = arguments[0] + arguments[1];
    results[1] = GetCurrentThreadId();
}

// Exercises the Values[24] widening: the handler fills every slot and the Completed copy
// path must transfer all 24 verbatim (regression for the old Values[8] truncation risk).
void WriteAllTwentyFourValues(const uint32_t* /*arguments*/, uint32_t* results)
{
    for (uint32_t i = 0; i < 24; ++i)
    {
        results[i] = 0xA000u + i;
    }
}

// Running-timeout lifecycle probe. The handler blocks until released, writing only into the
// dispatcher-owned results buffer (never caller stack). This models a game-thread handler
// that outlives the pipe worker's timeout window.
volatile LONG g_blockingHandlerEntered = 0;
volatile LONG g_blockingHandlerRelease = 0;

void BlockingWorkWritesDispatcherStorage(const uint32_t* /*arguments*/, uint32_t* results)
{
    InterlockedExchange(&g_blockingHandlerEntered, 1);
    while (InterlockedCompareExchange(&g_blockingHandlerRelease, 0, 0) == 0)
    {
        Sleep(1);
    }
    // Write only dispatcher-owned storage. Filling many slots proves the late completion
    // touches g_result, not the pipe worker's already-returned stack frame.
    for (uint32_t i = 0; i < 24; ++i)
    {
        results[i] = 0xB000u + i;
    }
}

void DispatchRunsOnlyWhenPumpExecutes()
{
    using namespace RayaTrainer::agent;
    AgentGameThreadDispatcher::Reset();
    const auto workerThreadId = GetCurrentThreadId();
    AgentGameThreadRequest request = {};
    request.Work = AddOnGameThread;
    request.Arguments[0] = 20;
    request.Arguments[1] = 22;

    std::thread gameThread([]
    {
        Sleep(10);
        AgentGameThreadDispatcher::Pump();
    });

    AgentGameThreadResult result = {};
    const auto status = AgentGameThreadDispatcher::Dispatch(request, 1000, result);
    gameThread.join();

    Expect(status == AgentGameThreadDispatchStatus::Completed, "dispatch should complete");
    Expect(result.Values[0] == 42, "game-thread callback result should be returned");
    Expect(result.Values[1] == result.GameThreadId, "callback must execute on recorded game thread");
    Expect(result.GameThreadId != workerThreadId, "pipe worker must not execute callback");
}

void TimedOutPendingRequestCanBeReused()
{
    using namespace RayaTrainer::agent;
    AgentGameThreadDispatcher::Reset();
    AgentGameThreadRequest request = {};
    request.Work = AddOnGameThread;
    AgentGameThreadResult result = {};

    const auto timedOut = AgentGameThreadDispatcher::Dispatch(request, 2, result);
    Expect(timedOut == AgentGameThreadDispatchStatus::TimedOut, "request without Pump should time out");

    std::thread gameThread([]
    {
        Sleep(5);
        AgentGameThreadDispatcher::Pump();
    });
    const auto completed = AgentGameThreadDispatcher::Dispatch(request, 1000, result);
    gameThread.join();
    Expect(completed == AgentGameThreadDispatchStatus::Completed, "dispatcher should recover after timeout");
}

void CompletedResultCopiesAllTwentyFourValueSlots()
{
    using namespace RayaTrainer::agent;
    AgentGameThreadDispatcher::Reset();
    AgentGameThreadRequest request = {};
    request.Work = WriteAllTwentyFourValues;

    std::thread gameThread([]
    {
        Sleep(5);
        AgentGameThreadDispatcher::Pump();
    });
    AgentGameThreadResult result = {};
    const auto status = AgentGameThreadDispatcher::Dispatch(request, 1000, result);
    gameThread.join();

    Expect(status == AgentGameThreadDispatchStatus::Completed, "24-slot write must complete");
    for (uint32_t i = 0; i < 24; ++i)
    {
        Expect(result.Values[i] == 0xA000u + i, "all 24 value slots must be copied verbatim");
    }
}

void RunningTimeoutRecoversAndTouchesOnlyDispatcherStorage()
{
    using namespace RayaTrainer::agent;
    AgentGameThreadDispatcher::Reset();
    InterlockedExchange(&g_blockingHandlerEntered, 0);
    InterlockedExchange(&g_blockingHandlerRelease, 0);

    AgentGameThreadRequest request = {};
    request.Work = BlockingWorkWritesDispatcherStorage;

    std::thread gameThread([]
    {
        // Pump transitions Pending -> Running, then the handler blocks until released.
        AgentGameThreadDispatcher::Pump();
    });

    // The pipe worker waits with a timeout. The handler is blocked, so after the window the
    // dispatcher moves Running -> Abandoned and the pipe worker returns TimedOut. It must NOT
    // copy results: its result buffer stays zeroed (only Status is set to TimedOut).
    AgentGameThreadResult result = {};
    const auto timedOut = AgentGameThreadDispatcher::Dispatch(request, 100, result);
    Expect(timedOut == AgentGameThreadDispatchStatus::TimedOut, "blocked Running request must time out");
    Expect(g_blockingHandlerEntered != 0, "timeout must have occurred in Running state, not Pending");
    Expect(result.Status == AgentGameThreadDispatchStatus::TimedOut, "TimedOut status preserved");
    for (uint32_t i = 0; i < 24; ++i)
    {
        Expect(result.Values[i] == 0u, "TimedOut must not expose handler-written dispatcher values");
    }

    // Release the blocked handler. It writes g_result, sets Completed, detects Abandoned,
    // clears g_result, and returns to Idle — without ever touching the pipe worker's stack.
    InterlockedExchange(&g_blockingHandlerRelease, 1);
    gameThread.join();

    // The dispatcher must be reusable immediately: a fresh request completes normally.
    AgentGameThreadRequest request2 = {};
    request2.Work = AddOnGameThread;
    request2.Arguments[0] = 5;
    request2.Arguments[1] = 7;
    std::thread gameThread2([]
    {
        Sleep(5);
        AgentGameThreadDispatcher::Pump();
    });
    AgentGameThreadResult result2 = {};
    const auto recovered = AgentGameThreadDispatcher::Dispatch(request2, 1000, result2);
    gameThread2.join();
    Expect(recovered == AgentGameThreadDispatchStatus::Completed, "dispatcher must recover after Running-timeout");
    Expect(result2.Values[0] == 12u, "recovered dispatch must return the correct result");
}
}

int main()
{
    DispatchRunsOnlyWhenPumpExecutes();
    TimedOutPendingRequestCanBeReused();
    CompletedResultCopiesAllTwentyFourValueSlots();
    RunningTimeoutRecoversAndTouchesOnlyDispatcherStorage();
    g_failures += RunNativePointerSetTests();
    if (g_failures != 0)
    {
        return 1;
    }

    std::cout << "All agent tests passed\n";
    return 0;
}
