#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "AgentRuntime.h"
#include "AgentFeatureState.h"

namespace RayaTrainer::agent
{
bool InitializeRuntime()
{
    ResetNativeFeatureStates();
    return true;
}

void ShutdownRuntime()
{
    ResetNativeFeatureStates();
}
}
