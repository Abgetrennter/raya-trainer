#pragma once

#include <cstdint>

namespace RayaTrainer::agent
{
#pragma pack(push, 1)
struct NativeHookContext
{
    uint32_t Edi;
    uint32_t Esi;
    uint32_t Ebp;
    uint32_t OriginalEsp;
    uint32_t Ebx;
    uint32_t Edx;
    uint32_t Ecx;
    uint32_t Eax;
    uint32_t EFlags;
};
#pragma pack(pop)

void RegisterNativeHookAddress(uint32_t hookId, uint32_t address, uint32_t continuation);
uint32_t ReadCapturedPlayerObject();
void ResetNativeHookRuntime();
}

extern "C" uint32_t __cdecl AgentNativeHookHandler(
    uint32_t hookId,
    RayaTrainer::agent::NativeHookContext* context);
extern "C" uint32_t __cdecl AgentNativeHookBridge(
    uint32_t hookId,
    RayaTrainer::agent::NativeHookContext* context);
