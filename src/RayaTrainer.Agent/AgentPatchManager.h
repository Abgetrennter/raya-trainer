#pragma once

#include <cstdint>
#include <vector>

#include "AgentProtocol.h"

namespace RayaTrainer::agent
{
AgentStatusCode InstallPatchesFromPayload(const unsigned char* data, uint32_t length);
bool RestoreInstalledPatches();
uint32_t InstalledPatchCount();

// Reports the last hook mismatch captured during a failed InstallPatches, if any.
// When a mismatch was captured, returns Ok and fills outAddress/expected/actual/dump.
// The dump covers a window of bytes around the hook site so the host can disassemble it
// the same way PatchMismatchReportWriter does for the external-memory backend. Returns
// false when no mismatch is pending (e.g. install has not run yet, or succeeded).
struct PatchMismatchCapture
{
    uint32_t HookAddress = 0;
    std::vector<unsigned char> Expected;
    std::vector<unsigned char> Actual;
    std::vector<unsigned char> Dump;
};

bool TryGetLastMismatch(PatchMismatchCapture& outCapture);
}
