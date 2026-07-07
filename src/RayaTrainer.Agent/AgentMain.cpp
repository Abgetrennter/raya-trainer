#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "AgentPipeServer.h"
#include "AgentProtocol.h"

namespace
{
HANDLE g_workerThread = nullptr;
}

extern "C" __declspec(dllexport) unsigned short __stdcall RayaTrainerAgentProtocolVersion()
{
    return RayaTrainer::agent::kAgentProtocolVersion;
}

extern "C" __declspec(dllexport) unsigned long long __stdcall RayaTrainerAgentBuildFingerprint()
{
    return RayaTrainer::agent::kAgentBuildFingerprint;
}

BOOL APIENTRY DllMain(HMODULE moduleHandle, DWORD reason, LPVOID)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(moduleHandle);
        RayaTrainer::agent::RequestStop(false);
        g_workerThread = CreateThread(
            nullptr,
            0,
            RayaTrainer::agent::AgentWorkerThread,
            moduleHandle,
            0,
            nullptr);
        break;
    case DLL_PROCESS_DETACH:
        RayaTrainer::agent::RequestStop(true);
        if (g_workerThread != nullptr)
        {
            CloseHandle(g_workerThread);
            g_workerThread = nullptr;
        }
        break;
    default:
        break;
    }

    return TRUE;
}
