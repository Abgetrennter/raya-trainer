#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

namespace RayaTrainer::agent
{
DWORD WINAPI AgentWorkerThread(LPVOID parameter);
void RequestStop(bool stop);
}
