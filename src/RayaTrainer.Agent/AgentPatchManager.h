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
    uint8_t OriginKind = 0;     // 0=Hook, 1=RuntimePatchSet
    uint32_t SubjectId = 0;     // Hook: native hook id; PatchSet: PatchSetId
    uint32_t Sequence = 0;      // Monotonic sequence counter for recency ordering
};

bool TryGetLastMismatch(PatchMismatchCapture& outCapture);

// PatchSet types for v11 SetRuntimePatchSet (cmd 6).
enum class NativeRuntimePatchSetId : uint32_t
{
    FrameRateUnlock = 1,
};

enum class PatchSetEntryKind : uint8_t
{
    Data = 0,
    CodeFlow = 1,
    DerivedStateReset = 2,
};

struct PatchSetEntry
{
    uint32_t Address = 0;
    std::vector<unsigned char> EnableBytes;
    std::vector<unsigned char> DisableBytes;
    PatchSetEntryKind Kind = PatchSetEntryKind::Data;
};

struct PatchSetDefinition
{
    uint32_t Id = 0;
    std::vector<PatchSetEntry> Entries;
};

struct InstalledPatchSet
{
    PatchSetDefinition Definition;
    bool IsEnabled = false;
};

AgentStatusCode SetRuntimePatchSetFromPayload(const unsigned char* data, uint32_t length);
bool IsPatchSetEnabled(uint32_t patchSetId);
bool IsHookInstalled(uint32_t nativeHookId);

// IP conflict diagnostic: captured when a CodeFlow entry cannot be patched because
// a suspended thread's instruction pointer is within the ±16 byte guard zone.
struct PatchSetIpConflictCapture
{
    uint32_t PatchSetId = 0;
    uint32_t ConflictingEntryAddress = 0;
    uint32_t ConflictingThreadId = 0;
    uint32_t ObservedIp = 0;
    bool IsRestore = false;
    uint32_t Sequence = 0;      // Monotonic sequence counter for recency ordering
};

bool TryGetLastIpConflict(PatchSetIpConflictCapture& outCapture);

// Test seam: override IP conflict check result. 0=normal (default), 1=force-pass,
// 2=force-conflict. Only useful for testing; has no effect when set to 0.
void SetIpConflictTestSeamMode(int mode);
}
