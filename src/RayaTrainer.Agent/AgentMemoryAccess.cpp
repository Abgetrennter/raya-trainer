#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>
#include <cstring>
#include <vector>

#include "AgentMemoryAccess.h"
#include "AgentProtocol.h"

namespace RayaTrainer::agent
{
namespace
{
bool TryReadUInt32(const unsigned char* payload, uint32_t length, uint32_t& offset, uint32_t& value)
{
    if (offset > length || length - offset < sizeof(uint32_t))
    {
        return false;
    }

    std::memcpy(&value, payload + offset, sizeof(uint32_t));
    offset += sizeof(uint32_t);
    return true;
}

bool SafeReadMemory(uint32_t address, void* buffer, uint32_t length)
{
    if (length == 0)
    {
        return false;
    }

    __try
    {
        std::memcpy(
            buffer,
            reinterpret_cast<const void*>(static_cast<uintptr_t>(address)),
            static_cast<size_t>(length));
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

bool SafeWriteMemory(uint32_t address, const unsigned char* bytes, uint32_t length)
{
    if (length == 0)
    {
        return false;
    }

    auto* target = reinterpret_cast<void*>(static_cast<uintptr_t>(address));
    DWORD oldProtect = 0;
    if (!VirtualProtect(target, static_cast<SIZE_T>(length), PAGE_EXECUTE_READWRITE, &oldProtect))
    {
        return false;
    }

    bool copied = false;
    __try
    {
        std::memcpy(target, bytes, static_cast<size_t>(length));
        copied = true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        copied = false;
    }

    DWORD ignored = 0;
    VirtualProtect(target, static_cast<SIZE_T>(length), oldProtect, &ignored);
    if (copied)
    {
        FlushInstructionCache(GetCurrentProcess(), target, static_cast<SIZE_T>(length));
    }

    return copied;
}

bool ResolveWriteAddress(uint32_t address, AgentMemoryAddressMode mode, uint32_t& resolvedAddress)
{
    if (mode == AgentMemoryAddressMode::Direct)
    {
        resolvedAddress = address;
        return true;
    }

    if (mode == AgentMemoryAddressMode::DereferenceUInt32)
    {
        return SafeReadMemory(address, &resolvedAddress, sizeof(resolvedAddress));
    }

    return false;
}
}

AgentStatusCode ApplyMemoryWritesFromPayload(const unsigned char* payload, uint32_t length)
{
    uint32_t offset = 0;
    uint32_t writeCount = 0;
    if (!TryReadUInt32(payload, length, offset, writeCount))
    {
        return AgentStatusCode::InvalidCommand;
    }

    for (uint32_t index = 0; index < writeCount; ++index)
    {
        uint32_t address = 0;
        uint32_t modeValue = 0;
        uint32_t byteCount = 0;
        if (!TryReadUInt32(payload, length, offset, address) ||
            !TryReadUInt32(payload, length, offset, modeValue) ||
            !TryReadUInt32(payload, length, offset, byteCount) ||
            byteCount == 0 ||
            byteCount > kMaxPayloadLength ||
            offset > length ||
            length - offset < byteCount)
        {
            return AgentStatusCode::InvalidCommand;
        }

        uint32_t resolvedAddress = 0;
        if (!ResolveWriteAddress(address, static_cast<AgentMemoryAddressMode>(modeValue), resolvedAddress))
        {
            return AgentStatusCode::InvalidCommand;
        }

        if (!SafeWriteMemory(resolvedAddress, payload + offset, byteCount))
        {
            return AgentStatusCode::InternalError;
        }

        offset += byteCount;
    }

    return offset == length ? AgentStatusCode::Ok : AgentStatusCode::InvalidCommand;
}

AgentStatusCode ReadMemoryFromPayload(
    const unsigned char* payload,
    uint32_t length,
    uint32_t& address,
    std::vector<unsigned char>& bytes)
{
    if (length != sizeof(AgentMemoryReadRequest))
    {
        return AgentStatusCode::InvalidCommand;
    }

    uint32_t offset = 0;
    uint32_t byteCount = 0;
    if (!TryReadUInt32(payload, length, offset, address) ||
        !TryReadUInt32(payload, length, offset, byteCount) ||
        byteCount == 0 ||
        byteCount > kMaxPayloadLength - sizeof(AgentMemoryReadPayloadHeader))
    {
        return AgentStatusCode::InvalidCommand;
    }

    bytes.assign(byteCount, 0);
    return SafeReadMemory(address, bytes.data(), byteCount)
        ? AgentStatusCode::Ok
        : AgentStatusCode::InternalError;
}
}
