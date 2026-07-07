#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>
#include <cstring>

#include "AgentFeatureState.h"

namespace RayaTrainer::agent
{
namespace
{
constexpr uint32_t kStateCapacity = 128;
volatile LONG g_states[kStateCapacity] = {};

bool ReadUInt32(const unsigned char* data, uint32_t length, uint32_t& offset, uint32_t& value)
{
    if (data == nullptr || length - offset < sizeof(uint32_t))
    {
        return false;
    }
    std::memcpy(&value, data + offset, sizeof(value));
    offset += sizeof(value);
    return true;
}
}

AgentStatusCode ApplyNativeFeatureStatesFromPayload(
    const unsigned char* data,
    uint32_t length)
{
    uint32_t offset = 0;
    uint32_t count = 0;
    if (!ReadUInt32(data, length, offset, count) || count > kStateCapacity)
    {
        return AgentStatusCode::InvalidCommand;
    }

    for (uint32_t index = 0; index < count; ++index)
    {
        uint32_t stateId = 0;
        uint32_t addressMode = 0;
        uint32_t byteCount = 0;
        uint32_t value = 0;
        if (!ReadUInt32(data, length, offset, stateId) ||
            !ReadUInt32(data, length, offset, addressMode) ||
            !ReadUInt32(data, length, offset, byteCount) ||
            byteCount != sizeof(uint32_t) ||
            !ReadUInt32(data, length, offset, value) ||
            stateId == 0 || stateId >= kStateCapacity || addressMode != 0)
        {
            return AgentStatusCode::InvalidCommand;
        }

        InterlockedExchange(&g_states[stateId], static_cast<LONG>(value));
    }

    return offset == length ? AgentStatusCode::Ok : AgentStatusCode::InvalidCommand;
}

uint32_t ReadNativeFeatureState(NativeFeatureStateId id)
{
    const auto index = static_cast<uint32_t>(id);
    return index < kStateCapacity
        ? static_cast<uint32_t>(InterlockedCompareExchange(&g_states[index], 0, 0))
        : 0;
}

uint32_t ConsumeNativeFeatureState(NativeFeatureStateId id)
{
    const auto index = static_cast<uint32_t>(id);
    return index < kStateCapacity
        ? static_cast<uint32_t>(InterlockedExchange(&g_states[index], 0))
        : 0;
}

void ResetNativeFeatureStates()
{
    for (auto& state : g_states)
    {
        InterlockedExchange(&state, 0);
    }

    InterlockedExchange(
        &g_states[static_cast<uint32_t>(NativeFeatureStateId::MoneyAmount)],
        100000);
    InterlockedExchange(
        &g_states[static_cast<uint32_t>(NativeFeatureStateId::PowerValue)],
        100000);
    InterlockedExchange(
        &g_states[static_cast<uint32_t>(NativeFeatureStateId::SecretProtocolPointValue)],
        15);
    InterlockedExchange(
        &g_states[static_cast<uint32_t>(NativeFeatureStateId::SelectedUnitMaxHealthBits)],
        0x497423F0);
}
}
