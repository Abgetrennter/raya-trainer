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
}

int main()
{
    DispatchRunsOnlyWhenPumpExecutes();
    TimedOutPendingRequestCanBeReused();
    g_failures += RunNativePointerSetTests();
    if (g_failures != 0)
    {
        return 1;
    }

    std::cout << "All agent tests passed\n";
    return 0;
}
