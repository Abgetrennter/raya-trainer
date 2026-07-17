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

constexpr uint32_t kPulseStateIds[] = {1, 13, 21, 23};
volatile LONG g_stickyPulseBits[4] = {};

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

bool IsPulseId(uint32_t id)
{
    for (const auto pid : kPulseStateIds)
    {
        if (pid == id) return true;
    }
    return false;
}

int PulseIndex(NativeFeatureStateId id)
{
    const auto raw = static_cast<uint32_t>(id);
    for (int i = 0; i < 4; ++i)
    {
        if (kPulseStateIds[i] == raw) return i;
    }
    return -1;
}

bool IsValidDeclaredStateId(uint32_t id)
{
    // Declared enum range: 1-26 and 100-103
    if (id >= 1 && id <= 26) return true;
    if (id >= 100 && id <= 103) return true;
    return false;
}
}

// DEPRECATED v11: replaced by SetFeatureStatesFromPayload.
// Kept for L3a compat during transition.
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

AgentStatusCode SetFeatureStatesFromPayload(
    const unsigned char* data,
    uint32_t length)
{
    // Rule 1: payload must be at least 4 bytes (need Count field)
    if (data == nullptr || length < sizeof(uint32_t))
    {
        return AgentStatusCode::InvalidCommand;
    }

    uint32_t offset = 0;
    uint32_t count = 0;
    if (!ReadUInt32(data, length, offset, count))
    {
        return AgentStatusCode::InvalidCommand;
    }

    // Rule 2: Count ≤ 32
    if (count > 32)
    {
        return AgentStatusCode::InvalidCommand;
    }

    // Rule 3: exact payload length check — (length - 4) == Count * 8
    if ((length - sizeof(uint32_t)) != count * 8)
    {
        return AgentStatusCode::InvalidCommand;
    }

    // First pass: validate all entries before applying any
    // Track seen StateIds to detect duplicates
    uint32_t seenIds[32] = {};
    uint32_t seenCount = 0;

    for (uint32_t i = 0; i < count; ++i)
    {
        uint32_t stateId = 0;
        uint32_t value = 0;

        if (!ReadUInt32(data, length, offset, stateId) ||
            !ReadUInt32(data, length, offset, value))
        {
            return AgentStatusCode::InvalidCommand;
        }

        // Rule 4: must be in declared enum range (1-26 or 100-103), must not be 0
        if (!IsValidDeclaredStateId(stateId))
        {
            return AgentStatusCode::InvalidCommand;
        }

        // Rule 5: no duplicate StateId within the payload
        for (uint32_t j = 0; j < seenCount; ++j)
        {
            if (seenIds[j] == stateId)
            {
                return AgentStatusCode::InvalidCommand;
            }
        }
        seenIds[seenCount++] = stateId;

        // Pulse IDs are valid write targets: C# acts as producer (writes trigger value),
        // native hook is consumer (ConsumeNativeFeatureState on next frame). Sticky-bit
        // readback is observer-side and orthogonal to this write path.
    }

    // All validation passed — apply
    // Reset offset to re-read entries
    offset = sizeof(uint32_t);
    for (uint32_t i = 0; i < count; ++i)
    {
        uint32_t stateId = 0;
        uint32_t value = 0;
        ReadUInt32(data, length, offset, stateId);
        ReadUInt32(data, length, offset, value);
        InterlockedExchange(&g_states[stateId], static_cast<LONG>(value));
    }

    return AgentStatusCode::Ok;
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

    for (auto& bit : g_stickyPulseBits)
    {
        InterlockedExchange(&bit, 0);
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

void NotifyPulseFired(NativeFeatureStateId id)
{
    const int idx = PulseIndex(id);
    if (idx >= 0)
    {
        InterlockedExchange(&g_stickyPulseBits[idx], 1);
    }
}

uint32_t ReadStickyPulseBit(NativeFeatureStateId id)
{
    const int idx = PulseIndex(id);
    if (idx < 0) return 0;
    return static_cast<uint32_t>(InterlockedCompareExchange(&g_stickyPulseBits[idx], 0, 0));
}

void ClearAllStickyPulseBits()
{
    for (auto& bit : g_stickyPulseBits)
    {
        InterlockedExchange(&bit, 0);
    }
}

AgentStatusCode ReadAllFeatureStates(FeatureStateReadback* out, uint32_t capacity, uint32_t& outCount)
{
    if (out == nullptr || capacity < 30)
    {
        outCount = 0;
        return AgentStatusCode::InvalidCommand;
    }

    // Build the 40-entry ordered list: 1-26 then 100-103
    uint32_t index = 0;

    // IDs 1-26
    for (uint32_t id = 1; id <= 26; ++id)
    {
        const auto nsId = static_cast<NativeFeatureStateId>(id);
        if (IsPulseId(id))
        {
            out[index].StateId = id;
            out[index].Value = ReadStickyPulseBit(nsId);
        }
        else
        {
            out[index].StateId = id;
            out[index].Value = ReadNativeFeatureState(nsId);
        }
        ++index;
    }

    // IDs 100-103
    for (uint32_t id = 100; id <= 103; ++id)
    {
        const auto nsId = static_cast<NativeFeatureStateId>(id);
        out[index].StateId = id;
        out[index].Value = ReadNativeFeatureState(nsId);
        ++index;
    }

    outCount = index;
    return AgentStatusCode::Ok;
}
}
