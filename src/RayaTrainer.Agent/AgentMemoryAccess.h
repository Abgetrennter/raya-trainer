#pragma once

#include <cstdint>
#include <vector>

#include "AgentProtocol.h"

namespace RayaTrainer::agent
{
AgentStatusCode ApplyMemoryWritesFromPayload(const unsigned char* payload, uint32_t length);

AgentStatusCode ReadMemoryFromPayload(
    const unsigned char* payload,
    uint32_t length,
    uint32_t& address,
    std::vector<unsigned char>& bytes);
}
