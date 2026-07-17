#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>
#include <cstring>
#include <iostream>
#include <vector>

#include <Zydis/Decoder.h>

// Stub implementations for symbols needed by AgentPatchManager.cpp compilation.
// These are NEVER called by PatchSet tests — they only exist to satisfy the
// linker. If any is reached, the test has a bug or a dependency error.
extern "C" void AgentNativeHookBridge()
{
    DebugBreak();
}

extern "C" ZyanStatus ZydisDecoderInit(
    ZydisDecoder* /*decoder*/,
    ZydisMachineMode /*machineMode*/,
    ZydisStackWidth /*stackWidth*/)
{
    DebugBreak();
    return ZYAN_STATUS_FALSE;
}

extern "C" ZyanStatus ZydisDecoderDecodeFull(
    const ZydisDecoder* /*decoder*/,
    const void* /*buffer*/,
    ZyanUSize /*length*/,
    ZydisDecodedInstruction* /*instruction*/,
    ZydisDecodedOperand* /*operands*/)
{
    DebugBreak();
    return ZYAN_STATUS_FALSE;
}

#include "../../src/RayaTrainer.Agent/AgentPatchManager.h"

// Linker stubs for agent-internal functions never called by PatchSet tests.
namespace RayaTrainer::agent
{
void RegisterNativeHookAddress(uint32_t, uint32_t, uint32_t)
{
    DebugBreak();
}

void ResetNativeHookRuntime()
{
}

bool TryReadVerifiedBuiltInHookBytes(
    uint32_t, uint32_t, std::vector<unsigned char>&)
{
    DebugBreak();
    return false;
}
}

namespace
{
int g_pst_failures = 0;

void Expect(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << "FAILED: " << message << '\n';
        ++g_pst_failures;
    }
}

// ── Payload builders ───────────────────────────────────────────────────────────

struct TestPatchSetEntry
{
    uint32_t Address;
    uint8_t Kind;
    std::vector<unsigned char> EnableBytes;
    std::vector<unsigned char> DisableBytes;
};

struct TestHook
{
    uint32_t Address;
    uint32_t NativeHookId;
    uint32_t PatchLength;
    std::vector<unsigned char> OriginalBytes;
};

// Build a v11 InstallPatches payload with one PatchSet (Id=1) and optional hooks.
unsigned char* BuildV11Payload(
    const TestPatchSetEntry* entries, uint32_t entryCount,
    const TestHook* hooks, uint32_t hookCount,
    uint32_t& outLength)
{
    // Layout:
    //   PatchSetCount(4)
    //   [ PatchSetId(4) + EntryCount(4) + entries... ]  (1 patch set, Id=1)
    //   HookCount(4)
    //   [ hooks... ]

    uint32_t length = 4 + 4 + 4; // PatchSetCount + PatchSetId(1) + EntryCount
    for (uint32_t i = 0; i < entryCount; ++i)
    {
        length += 4 + 1; // Address + Kind
        length += 4 + static_cast<uint32_t>(entries[i].EnableBytes.size());  // EnableByteCount + bytes
        length += 4 + static_cast<uint32_t>(entries[i].DisableBytes.size()); // DisableByteCount + bytes
    }
    length += 4; // HookCount
    for (uint32_t i = 0; i < hookCount; ++i)
    {
        length += 4 + 4 + 4 + 4 + static_cast<uint32_t>(hooks[i].OriginalBytes.size());
    }

    auto* buf = new unsigned char[length];
    uint32_t offset = 0;

    // PatchSetCount = 1
    const uint32_t one = 1;
    std::memcpy(buf + offset, &one, 4); offset += 4;

    // PatchSetId = 1 (FrameRateUnlock)
    const uint32_t psId = 1;
    std::memcpy(buf + offset, &psId, 4); offset += 4;

    // EntryCount
    std::memcpy(buf + offset, &entryCount, 4); offset += 4;

    for (uint32_t i = 0; i < entryCount; ++i)
    {
        std::memcpy(buf + offset, &entries[i].Address, 4); offset += 4;
        buf[offset] = entries[i].Kind; offset += 1;

        const uint32_t ebc = static_cast<uint32_t>(entries[i].EnableBytes.size());
        std::memcpy(buf + offset, &ebc, 4); offset += 4;
        if (ebc > 0) { std::memcpy(buf + offset, entries[i].EnableBytes.data(), ebc); offset += ebc; }

        const uint32_t dbc = static_cast<uint32_t>(entries[i].DisableBytes.size());
        std::memcpy(buf + offset, &dbc, 4); offset += 4;
        if (dbc > 0) { std::memcpy(buf + offset, entries[i].DisableBytes.data(), dbc); offset += dbc; }
    }

    std::memcpy(buf + offset, &hookCount, 4); offset += 4;

    for (uint32_t i = 0; i < hookCount; ++i)
    {
        std::memcpy(buf + offset, &hooks[i].Address, 4); offset += 4;
        std::memcpy(buf + offset, &hooks[i].NativeHookId, 4); offset += 4;
        std::memcpy(buf + offset, &hooks[i].PatchLength, 4); offset += 4;
        const uint32_t obc = static_cast<uint32_t>(hooks[i].OriginalBytes.size());
        std::memcpy(buf + offset, &obc, 4); offset += 4;
        std::memcpy(buf + offset, hooks[i].OriginalBytes.data(), obc); offset += obc;
    }

    outLength = length;
    return buf;
}

// Build a minimal old v10 payload (writes + hooks) to verify rejection as v11.
unsigned char* BuildV10Payload(uint32_t& outLength)
{
    // Format: writeCount(4) + [Address(4) + ByteCount(4) + bytes...] + hookCount(4)
    // Old write: Address=0x1000, ByteCount=4, bytes=[0x11,0x22,0x33,0x44]
    // hookCount=0
    const uint32_t writeCount = 1;
    const uint32_t addr = 0x1000;
    const uint32_t byteCount = 4;
    const unsigned char writeBytes[] = { 0x11, 0x22, 0x33, 0x44 };
    const uint32_t hookCount = 0;

    outLength = 4 + 4 + 4 + byteCount + 4;
    auto* buf = new unsigned char[outLength];
    uint32_t offset = 0;

    std::memcpy(buf + offset, &writeCount, 4); offset += 4;
    std::memcpy(buf + offset, &addr, 4); offset += 4;
    std::memcpy(buf + offset, &byteCount, 4); offset += 4;
    std::memcpy(buf + offset, writeBytes, byteCount); offset += byteCount;
    std::memcpy(buf + offset, &hookCount, 4); offset += 4;

    return buf;
}

// Allocate RWX memory for test patches and write DisableBytes into it.
// Returns the base address (0 on failure).
uint32_t AllocPatchBuffer(const unsigned char* bytes, size_t length)
{
    auto* mem = VirtualAlloc(nullptr, length + 16, MEM_COMMIT, PAGE_EXECUTE_READWRITE);
    if (mem == nullptr)
    {
        return 0;
    }
    std::memset(mem, 0xCC, length + 16); // INT3 fill
    std::memcpy(mem, bytes, length);
    FlushInstructionCache(GetCurrentProcess(), mem, length);
    return static_cast<uint32_t>(reinterpret_cast<uintptr_t>(mem));
}

void FreePatchBuffer(uint32_t address)
{
    if (address != 0)
    {
        VirtualFree(reinterpret_cast<void*>(static_cast<uintptr_t>(address)), 0, MEM_RELEASE);
    }
}

// ── Tests ───────────────────────────────────────────────────────────────────────

void TestSetRuntimePatchSet_EmptyPayload_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;

    const auto status = SetRuntimePatchSetFromPayload(nullptr, 0);
    Expect(status == AgentStatusCode::InvalidCommand,
        "null/0 payload must return InvalidCommand");

    // Truncated: only 4 bytes instead of 8
    unsigned char shortPayload[4] = { 1, 0, 0, 0 };
    const auto status2 = SetRuntimePatchSetFromPayload(shortPayload, 4);
    Expect(status2 == AgentStatusCode::InvalidCommand,
        "4-byte payload must return InvalidCommand");

    // Extra bytes: 12 instead of 8
    unsigned char longPayload[12] = {};
    const auto status3 = SetRuntimePatchSetFromPayload(longPayload, 12);
    Expect(status3 == AgentStatusCode::InvalidCommand,
        "12-byte payload must return InvalidCommand");
}

void TestSetRuntimePatchSet_UnknownPatchSetId_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;

    // No patch sets installed yet
    unsigned char payload[8] = {};
    // PatchSetId = 999 (unknown), EnableFlag = 0
    uint32_t id = 999;
    std::memcpy(payload, &id, 4);
    // EnableFlag stays 0

    const auto status = SetRuntimePatchSetFromPayload(payload, 8);
    Expect(status == AgentStatusCode::InvalidCommand,
        "Unknown PatchSetId (999) without install must return InvalidCommand");
}

void TestSetRuntimePatchSet_NotInstalled_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;

    // PatchSetId=1, EnableFlag=1 — but no InstallPatches has been called
    unsigned char payload[8] = {};
    uint32_t id = 1;
    uint32_t flag = 1;
    std::memcpy(payload, &id, 4);
    std::memcpy(payload + 4, &flag, 4);

    const auto status = SetRuntimePatchSetFromPayload(payload, 8);
    Expect(status == AgentStatusCode::InvalidCommand,
        "SetRuntimePatchSet before InstallPatches must return InvalidCommand");
}

void TestSetRuntimePatchSet_FrameRateUnlock_NoHook41_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;

    // Set up a patch set entry with real memory for the DisableBytes pre-verify.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };

    const uint32_t testAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (testAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        return;
    }

    TestPatchSetEntry entries[] = {
        { testAddr, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 1, nullptr, 0, payloadLen);

    // Install the patch set (no hooks). Should succeed.
    const auto installStatus = InstallPatchesFromPayload(payload, payloadLen);
    Expect(installStatus == AgentStatusCode::Ok,
        "InstallPatches from valid payload must return Ok");

    delete[] payload;

    // Now try to enable FrameRateUnlock without Hook 41.
    unsigned char cmdPayload[8] = {};
    uint32_t psId = 1;
    uint32_t enable = 1;
    std::memcpy(cmdPayload, &psId, 4);
    std::memcpy(cmdPayload + 4, &enable, 4);

    const auto status = SetRuntimePatchSetFromPayload(cmdPayload, 8);
    Expect(status == AgentStatusCode::InvalidCommand,
        "SetRuntimePatchSet(FrameRateUnlock, enable) without Hook 41 must return InvalidCommand");

    FreePatchBuffer(testAddr);
}

void TestSetRuntimePatchSet_FrameRateUnlock_WithHook41_Enables()
{
    using namespace RayaTrainer::agent;

    // Allocate memory for hook 41 site (needs ≥5 bytes of original code)
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    if (hookAddr == 0)
    {
        Expect(false, "VirtualAlloc for hook failed");
        return;
    }

    // Allocate memory for patch set entry
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (psAddr == 0)
    {
        Expect(false, "VirtualAlloc for patch set failed");
        FreePatchBuffer(hookAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } }
    };

    TestHook hooks[] = {
        { hookAddr, 41, 5,
          { hookOriginal, hookOriginal + sizeof(hookOriginal) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, payloadLen);

    const auto installStatus = InstallPatchesFromPayload(payload, payloadLen);
    Expect(installStatus == AgentStatusCode::Ok,
        "InstallPatches with hook 41 + patch set must return Ok");

    delete[] payload;

    // Now enable the patch set — should succeed because Hook 41 is installed.
    unsigned char cmdPayload[8] = {};
    uint32_t psId = 1;
    uint32_t enable = 1;
    std::memcpy(cmdPayload, &psId, 4);
    std::memcpy(cmdPayload + 4, &enable, 4);

    const auto status = SetRuntimePatchSetFromPayload(cmdPayload, 8);
    Expect(status == AgentStatusCode::Ok,
        "SetRuntimePatchSet(FrameRateUnlock, enable) with Hook 41 must return Ok");

    Expect(IsPatchSetEnabled(1),
        "IsPatchSetEnabled(1) must be true after successful enable");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestSetRuntimePatchSet_IdempotentEnable_ReturnsOk()
{
    using namespace RayaTrainer::agent;

    // Set up: install patch set, enable it, then enable again
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };

    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr);
        FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, { hookOriginal, hookOriginal + sizeof(hookOriginal) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, payloadLen);
    InstallPatchesFromPayload(payload, payloadLen);
    delete[] payload;

    // Enable once
    unsigned char cmdPayload[8] = {};
    uint32_t psId = 1;
    uint32_t enable = 1;
    std::memcpy(cmdPayload, &psId, 4);
    std::memcpy(cmdPayload + 4, &enable, 4);
    SetRuntimePatchSetFromPayload(cmdPayload, 8);

    // Enable again — idempotent
    const auto status = SetRuntimePatchSetFromPayload(cmdPayload, 8);
    Expect(status == AgentStatusCode::Ok,
        "Idempotent enable must return Ok");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestSetRuntimePatchSet_IdempotentDisable_ReturnsOk()
{
    using namespace RayaTrainer::agent;

    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };

    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr);
        FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, { hookOriginal, hookOriginal + sizeof(hookOriginal) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, payloadLen);
    InstallPatchesFromPayload(payload, payloadLen);
    delete[] payload;

    // Patch set starts disabled. Disable again is idempotent.
    unsigned char cmdPayload[8] = {};
    uint32_t psId = 1;
    uint32_t disable = 0;
    std::memcpy(cmdPayload, &psId, 4);
    std::memcpy(cmdPayload + 4, &disable, 4);

    const auto status = SetRuntimePatchSetFromPayload(cmdPayload, 8);
    Expect(status == AgentStatusCode::Ok,
        "Idempotent disable (already disabled) must return Ok");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestSetRuntimePatchSet_PreWriteMismatch_ReturnsPatchMismatch()
{
    using namespace RayaTrainer::agent;

    // Install patch set, then corrupt the memory, then try to enable.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };
    const unsigned char corruptBytes[] = { 0x00, 0x00 };

    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr);
        FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, { hookOriginal, hookOriginal + sizeof(hookOriginal) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, payloadLen);
    InstallPatchesFromPayload(payload, payloadLen);
    delete[] payload;

    // Corrupt the patch set's memory so it no longer matches DisableBytes
    auto* ptr = reinterpret_cast<unsigned char*>(static_cast<uintptr_t>(psAddr));
    std::memcpy(ptr, corruptBytes, sizeof(corruptBytes));

    // Try to enable — should fail with PatchMismatch
    unsigned char cmdPayload[8] = {};
    uint32_t psId = 1;
    uint32_t enable = 1;
    std::memcpy(cmdPayload, &psId, 4);
    std::memcpy(cmdPayload + 4, &enable, 4);

    const auto status = SetRuntimePatchSetFromPayload(cmdPayload, 8);
    Expect(status == AgentStatusCode::PatchMismatch,
        "SetRuntimePatchSet with corrupted memory must return PatchMismatch");

    // Verify state is unchanged (still disabled)
    Expect(!IsPatchSetEnabled(1),
        "PatchSet 1 must remain disabled after failed enable");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestSetRuntimePatchSet_RollbackOnWriteFailure_StateUnchanged()
{
    // TODO: requires InjectArbitraryFailure seam in WriteProcessLocalMemory.
    // Skipping this test because the current architecture has no mock seam in the
    // memory-write path. A future refactor can add a test hook.
    // For now, verify the skip is intentional:
    std::cout << "  [SKIP] RollbackOnWriteFailure: requires InjectArbitraryFailure seam\n";
}

void TestRestore_RestoresEnabledPatchSets()
{
    using namespace RayaTrainer::agent;

    // Install patch set, enable it, then call RestoreInstalledPatches.
    // Verify IsPatchSetEnabled is false and bytes are restored.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };

    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr);
        FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, { hookOriginal, hookOriginal + sizeof(hookOriginal) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, payloadLen);
    InstallPatchesFromPayload(payload, payloadLen);
    delete[] payload;

    // Enable the patch set
    unsigned char cmdPayload[8] = {};
    uint32_t psId = 1;
    uint32_t enable = 1;
    std::memcpy(cmdPayload, &psId, 4);
    std::memcpy(cmdPayload + 4, &enable, 4);
    SetRuntimePatchSetFromPayload(cmdPayload, 8);

    Expect(IsPatchSetEnabled(1),
        "PatchSet 1 must be enabled before restore test");

    // Read memory at psAddr — should now be EnableBytes
    unsigned char current[2] = {};
    std::memcpy(current, reinterpret_cast<void*>(static_cast<uintptr_t>(psAddr)), 2);
    Expect(std::equal(current, current + 2, enableBytes),
        "Memory at patch address must contain EnableBytes after enable");

    // Restore everything
    const bool restored = RestoreInstalledPatches();
    Expect(restored,
        "RestoreInstalledPatches must return true");

    Expect(!IsPatchSetEnabled(1),
        "PatchSet 1 must be disabled after RestoreInstalledPatches");

    // Verify bytes restored to DisableBytes
    unsigned char afterRestore[2] = {};
    std::memcpy(afterRestore, reinterpret_cast<void*>(static_cast<uintptr_t>(psAddr)), 2);
    Expect(std::equal(afterRestore, afterRestore + 2, disableBytes),
        "Memory at patch address must contain DisableBytes after restore");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestInstallPatches_PatchSetWithMismatchedEnableDisableLengths_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;

    // EnableBytes size 2, DisableBytes size 3 — must be rejected.
    const unsigned char enableBytes[] = { 0xEB, 0xFE };
    const unsigned char disableBytes[] = { 0x90, 0x90, 0x90 };

    TestPatchSetEntry entries[] = {
        { 0x2000, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 1, nullptr, 0, payloadLen);

    const auto status = InstallPatchesFromPayload(payload, payloadLen);
    Expect(status == AgentStatusCode::InvalidCommand,
        "InstallPatches with mismatched Enable/Disable lengths must return InvalidCommand");

    delete[] payload;
}

void TestInstallPatches_PatchSetCurrentBytesNotDisabled_ReturnsPatchMismatch()
{
    using namespace RayaTrainer::agent;

    // Set up memory with bytes that do NOT match DisableBytes
    const unsigned char memBytes[] = { 0x00, 0x00 }; // not DisableBytes!
    const unsigned char enableBytes[] = { 0xEB, 0xFE };
    const unsigned char disableBytes[] = { 0x90, 0x90 };

    const uint32_t psAddr = AllocPatchBuffer(memBytes, sizeof(memBytes));
    if (psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 1, nullptr, 0, payloadLen);

    const auto status = InstallPatchesFromPayload(payload, payloadLen);
    Expect(status == AgentStatusCode::PatchMismatch,
        "InstallPatches with non-disabled current bytes must return PatchMismatch");

    delete[] payload;
    FreePatchBuffer(psAddr);
}

void TestDerivedStateReset_AllowsArbitraryCurrentValueAndWritesOnBothTransitions()
{
    using namespace RayaTrainer::agent;

    const unsigned char currentBytes[] = { 0x78, 0x56, 0x34, 0x12 };
    const unsigned char resetBytes[] = { 0x00, 0x00, 0x00, 0x00 };
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    const uint32_t resetAddr = AllocPatchBuffer(currentBytes, sizeof(currentBytes));
    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    if (resetAddr == 0 || hookAddr == 0)
    {
        Expect(false, "VirtualAlloc for DerivedStateReset test failed");
        FreePatchBuffer(resetAddr);
        FreePatchBuffer(hookAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { resetAddr, 2 /*DerivedStateReset*/,
          { resetBytes, resetBytes + sizeof(resetBytes) },
          { resetBytes, resetBytes + sizeof(resetBytes) } }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, { hookOriginal, hookOriginal + sizeof(hookOriginal) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, payloadLen);
    Expect(InstallPatchesFromPayload(payload, payloadLen) == AgentStatusCode::Ok,
        "DerivedStateReset must allow an arbitrary current value during registration");
    delete[] payload;

    unsigned char command[8] = {};
    uint32_t patchSetId = 1;
    uint32_t enable = 1;
    std::memcpy(command, &patchSetId, sizeof(patchSetId));
    std::memcpy(command + 4, &enable, sizeof(enable));
    Expect(SetRuntimePatchSetFromPayload(command, sizeof(command)) == AgentStatusCode::Ok,
        "DerivedStateReset enable transition must succeed");
    Expect(std::memcmp(reinterpret_cast<void*>(static_cast<uintptr_t>(resetAddr)),
        resetBytes, sizeof(resetBytes)) == 0,
        "DerivedStateReset enable transition must write the reset value");

    const uint32_t nonzeroAgain = 0xAABBCCDDu;
    std::memcpy(reinterpret_cast<void*>(static_cast<uintptr_t>(resetAddr)),
        &nonzeroAgain, sizeof(nonzeroAgain));
    uint32_t disable = 0;
    std::memcpy(command + 4, &disable, sizeof(disable));
    Expect(SetRuntimePatchSetFromPayload(command, sizeof(command)) == AgentStatusCode::Ok,
        "DerivedStateReset disable transition must ignore the changed derived value");
    Expect(std::memcmp(reinterpret_cast<void*>(static_cast<uintptr_t>(resetAddr)),
        resetBytes, sizeof(resetBytes)) == 0,
        "DerivedStateReset disable transition must write the reset value");

    FreePatchBuffer(resetAddr);
    FreePatchBuffer(hookAddr);
}

void TestInstallPatches_DuplicatePatchSetEntryAddress_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;

    // Two entries with the same address in the same patch set.
    const unsigned char enableBytes[] = { 0xEB, 0xFE };
    const unsigned char disableBytes[] = { 0x90, 0x90 };

    TestPatchSetEntry entries[] = {
        { 0x3000, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } },
        { 0x3000, 0, { enableBytes, enableBytes + sizeof(enableBytes) },
                       { disableBytes, disableBytes + sizeof(disableBytes) } }  // duplicate
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(entries, 2, nullptr, 0, payloadLen);

    const auto status = InstallPatchesFromPayload(payload, payloadLen);
    Expect(status == AgentStatusCode::InvalidCommand,
        "InstallPatches with duplicate entry address must return InvalidCommand");

    delete[] payload;
}

void TestInstallPatches_OldV10WritesPayload_Rejected()
{
    using namespace RayaTrainer::agent;

    uint32_t v10len = 0;
    auto* v10payload = BuildV10Payload(v10len);

    const auto status = InstallPatchesFromPayload(v10payload, v10len);
    Expect(status == AgentStatusCode::InvalidCommand,
        "Old v10 writes-style payload must be rejected as InvalidCommand");

    delete[] v10payload;
}

// ── L3b: CodeFlow tests ──────────────────────────────────────────────────────

void TestCodeFlow_EntryWithKind1_ClassifiedAsCodeFlow()
{
    using namespace RayaTrainer::agent;

    // Install a patch set with a CodeFlow entry (Kind=1).
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };

    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr); FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 1 /*CodeFlow*/, {enableBytes, enableBytes + sizeof(enableBytes)},
                                   {disableBytes, disableBytes + sizeof(disableBytes)} }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, {hookOriginal, hookOriginal + sizeof(hookOriginal)} }
    };

    uint32_t len = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, len);
    Expect(InstallPatchesFromPayload(payload, len) == AgentStatusCode::Ok,
        "InstallPatches with CodeFlow entry must return Ok");
    delete[] payload;

    // Enable the patch set — should succeed.
    unsigned char cmd[8] = {};
    uint32_t psId = 1, enable = 1;
    std::memcpy(cmd, &psId, 4); std::memcpy(cmd + 4, &enable, 4);
    Expect(SetRuntimePatchSetFromPayload(cmd, 8) == AgentStatusCode::Ok,
        "SetRuntimePatchSet with CodeFlow entry must succeed");
    Expect(IsPatchSetEnabled(1), "PatchSet must be enabled");

    // Disable again
    uint32_t disable = 0;
    std::memcpy(cmd + 4, &disable, 4);
    Expect(SetRuntimePatchSetFromPayload(cmd, 8) == AgentStatusCode::Ok,
        "Disable CodeFlow must succeed");
    Expect(!IsPatchSetEnabled(1), "PatchSet must be disabled after disable");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestCodeFlow_TransactionCommits_WhenIpOutsideRange()
{
    using namespace RayaTrainer::agent;

    // Normal case: install with CodeFlow entry, enable succeeds.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };

    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr); FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 1 /*CodeFlow*/, {enableBytes, enableBytes + sizeof(enableBytes)},
                                   {disableBytes, disableBytes + sizeof(disableBytes)} }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, {hookOriginal, hookOriginal + sizeof(hookOriginal)} }
    };

    uint32_t len = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, len);
    InstallPatchesFromPayload(payload, len);
    delete[] payload;

    // Verify memory is still DisableBytes
    unsigned char current[2] = {};
    std::memcpy(current, reinterpret_cast<void*>(static_cast<uintptr_t>(psAddr)), 2);
    Expect(std::equal(current, current + 2, disableBytes),
        "Memory must still be DisableBytes before enable");

    // Enable with seam mode 0 (normal) — IP is outside any range
    unsigned char cmd[8] = {};
    uint32_t psId = 1, enable = 1;
    std::memcpy(cmd, &psId, 4); std::memcpy(cmd + 4, &enable, 4);
    const auto status = SetRuntimePatchSetFromPayload(cmd, 8);
    Expect(status == AgentStatusCode::Ok,
        "CodeFlow enable with IP outside range must return Ok");

    // Verify memory changed to EnableBytes
    std::memcpy(current, reinterpret_cast<void*>(static_cast<uintptr_t>(psAddr)), 2);
    Expect(std::equal(current, current + 2, enableBytes),
        "Memory must be EnableBytes after enable");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestCodeFlow_TransactionAborts_WhenIpInsideRange()
{
    using namespace RayaTrainer::agent;

    // Use test seam to force IP conflict.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };

    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr); FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 1 /*CodeFlow*/, {enableBytes, enableBytes + sizeof(enableBytes)},
                                   {disableBytes, disableBytes + sizeof(disableBytes)} }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, {hookOriginal, hookOriginal + sizeof(hookOriginal)} }
    };

    uint32_t len = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, len);
    InstallPatchesFromPayload(payload, len);
    delete[] payload;

    // Force IP conflict via test seam
    SetIpConflictTestSeamMode(2);  // force-conflict

    unsigned char cmd[8] = {};
    uint32_t psId = 1, enable = 1;
    std::memcpy(cmd, &psId, 4); std::memcpy(cmd + 4, &enable, 4);
    const auto status = SetRuntimePatchSetFromPayload(cmd, 8);
    Expect(status == AgentStatusCode::InternalError,
        "CodeFlow enable with IP conflict must return InternalError");

    // Verify IP conflict was captured
    PatchSetIpConflictCapture conflict;
    Expect(TryGetLastIpConflict(conflict),
        "TryGetLastIpConflict must return true after IP conflict");
    Expect(conflict.PatchSetId == 1,
        "Conflict capture must have PatchSetId=1");
    Expect(!conflict.IsRestore,
        "Conflict capture must have IsRestore=false");

    SetIpConflictTestSeamMode(0);  // restore

    // Verify state unchanged
    Expect(!IsPatchSetEnabled(1),
        "PatchSet must remain disabled after IP conflict abort");

    // Verify memory unchanged (still DisableBytes)
    unsigned char current[2] = {};
    std::memcpy(current, reinterpret_cast<void*>(static_cast<uintptr_t>(psAddr)), 2);
    Expect(std::equal(current, current + 2, disableBytes),
        "Memory must be DisableBytes after IP conflict abort");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestCodeFlow_TransactionAborts_ClearsStickyState()
{
    using namespace RayaTrainer::agent;

    // After IP conflict abort, IsEnabled unchanged.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };

    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr); FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 1 /*CodeFlow*/, {enableBytes, enableBytes + sizeof(enableBytes)},
                                   {disableBytes, disableBytes + sizeof(disableBytes)} }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, {hookOriginal, hookOriginal + sizeof(hookOriginal)} }
    };

    uint32_t len = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, len);
    InstallPatchesFromPayload(payload, len);
    delete[] payload;

    Expect(!IsPatchSetEnabled(1),
        "PatchSet must be disabled initially");

    // Force IP conflict
    SetIpConflictTestSeamMode(2);
    unsigned char cmd[8] = {};
    uint32_t psId = 1, enable = 1;
    std::memcpy(cmd, &psId, 4); std::memcpy(cmd + 4, &enable, 4);
    SetRuntimePatchSetFromPayload(cmd, 8);
    SetIpConflictTestSeamMode(0);

    // Must still be disabled
    Expect(!IsPatchSetEnabled(1),
        "PatchSet must remain disabled after IP conflict abort (sticky state)");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestCodeFlow_DataEntriesBatched_AllCommitted()
{
    using namespace RayaTrainer::agent;

    // Mixed patch set with both CodeFlow and Data entries.
    // Use two separate addresses.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[]  = { 0xEB, 0xFE };
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };

    const uint32_t cfAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    const uint32_t dataAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    if (cfAddr == 0 || dataAddr == 0 || hookAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(cfAddr); FreePatchBuffer(dataAddr); FreePatchBuffer(hookAddr);
        return;
    }

    // Build payload with both CodeFlow and Data entries
    // Need to adapt BuildV11Payload: it's hardcoded to PatchSetId=1
    // But entries are passed as array, so both entries are in the same PatchSet
    TestPatchSetEntry entries[] = {
        { cfAddr, 1 /*CodeFlow*/, {enableBytes, enableBytes + sizeof(enableBytes)},
                                   {disableBytes, disableBytes + sizeof(disableBytes)} },
        { dataAddr, 0 /*Data*/,   {enableBytes, enableBytes + sizeof(enableBytes)},
                                   {disableBytes, disableBytes + sizeof(disableBytes)} }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, {hookOriginal, hookOriginal + sizeof(hookOriginal)} }
    };

    uint32_t len = 0;
    auto* payload = BuildV11Payload(entries, 2, hooks, 1, len);
    Expect(InstallPatchesFromPayload(payload, len) == AgentStatusCode::Ok,
        "InstallPatches with mixed CodeFlow+Data must succeed");
    delete[] payload;

    // Enable — both entries must be written
    unsigned char cmd[8] = {};
    uint32_t psId = 1, enable = 1;
    std::memcpy(cmd, &psId, 4); std::memcpy(cmd + 4, &enable, 4);
    Expect(SetRuntimePatchSetFromPayload(cmd, 8) == AgentStatusCode::Ok,
        "Enable mixed patch set must succeed");
    Expect(IsPatchSetEnabled(1), "PatchSet must be enabled");

    // Verify both addresses contain EnableBytes
    unsigned char cfCurrent[2] = {}, dataCurrent[2] = {};
    std::memcpy(cfCurrent, reinterpret_cast<void*>(static_cast<uintptr_t>(cfAddr)), 2);
    std::memcpy(dataCurrent, reinterpret_cast<void*>(static_cast<uintptr_t>(dataAddr)), 2);
    Expect(std::equal(cfCurrent, cfCurrent + 2, enableBytes),
        "CodeFlow entry must contain EnableBytes after enable");
    Expect(std::equal(dataCurrent, dataCurrent + 2, enableBytes),
        "Data entry must contain EnableBytes after enable");

    // Disable — both must revert
    uint32_t disable = 0;
    std::memcpy(cmd + 4, &disable, 4);
    Expect(SetRuntimePatchSetFromPayload(cmd, 8) == AgentStatusCode::Ok,
        "Disable mixed patch set must succeed");
    Expect(!IsPatchSetEnabled(1), "PatchSet must be disabled");

    std::memcpy(cfCurrent, reinterpret_cast<void*>(static_cast<uintptr_t>(cfAddr)), 2);
    std::memcpy(dataCurrent, reinterpret_cast<void*>(static_cast<uintptr_t>(dataAddr)), 2);
    Expect(std::equal(cfCurrent, cfCurrent + 2, disableBytes),
        "CodeFlow entry must be DisableBytes after disable");
    Expect(std::equal(dataCurrent, dataCurrent + 2, disableBytes),
        "Data entry must be DisableBytes after disable");

    FreePatchBuffer(cfAddr);
    FreePatchBuffer(dataAddr);
    FreePatchBuffer(hookAddr);
}

void TestCodeFlow_MixedPatchSet_CodeFlowFirstThenData()
{
    using namespace RayaTrainer::agent;

    // This test verifies ordering by using a CodeFlow entry at a lower address
    // and a Data entry at a higher address. If CodeFlow is written first, the
    // data at cfAddr changes before dataAddr. We use the test seam to force an
    // IP conflict on the Data write (not possible without a seam), so instead
    // we verify that both are written correctly — the ordering guarantee is that
    // CodeFlow writes happen before Data writes within a single transaction.
    // A direct IP-level verification would require a debugger. See TODO(L8).
    // For now, the mixed enable/disable test above exercises both paths together.
    std::cout << "  [SKIP] CodeFlowMixed_Ordering: requires thread-IP tracing\n";
}

void TestRestore_WithCodeFlowEntries_AppliesSameIpGuard()
{
    using namespace RayaTrainer::agent;

    // Enable patch set with CodeFlow, then Restore with IP conflict seam.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[]  = { 0xEB, 0xFE };
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };

    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr); FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 1 /*CodeFlow*/, {enableBytes, enableBytes + sizeof(enableBytes)},
                                   {disableBytes, disableBytes + sizeof(disableBytes)} }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, {hookOriginal, hookOriginal + sizeof(hookOriginal)} }
    };

    uint32_t len = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, len);
    InstallPatchesFromPayload(payload, len);
    delete[] payload;

    // Enable
    unsigned char cmd[8] = {};
    uint32_t psId = 1, enable = 1;
    std::memcpy(cmd, &psId, 4); std::memcpy(cmd + 4, &enable, 4);
    SetRuntimePatchSetFromPayload(cmd, 8);
    Expect(IsPatchSetEnabled(1), "PatchSet must be enabled before restore test");

    // Force IP conflict during Restore
    SetIpConflictTestSeamMode(2);
    const bool restored = RestoreInstalledPatches();
    SetIpConflictTestSeamMode(0);

    Expect(!restored,
        "Restore with CodeFlow IP conflict must return false");

    // IP conflict should be captured with IsRestore=true
    PatchSetIpConflictCapture conflict;
    Expect(TryGetLastIpConflict(conflict),
        "TryGetLastIpConflict must return true after Restore IP conflict");
    Expect(conflict.IsRestore,
        "Conflict capture must have IsRestore=true after Restore");

    // PatchSet.IsEnabled should be false now (Restore marks it disabled regardless)
    Expect(!IsPatchSetEnabled(1),
        "PatchSet must be disabled after Restore (attempted)");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestRestore_WithCodeFlowEntries_CommitSuccess()
{
    using namespace RayaTrainer::agent;

    // Normal Restore with CodeFlow entry.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[]  = { 0xEB, 0xFE };
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };

    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr); FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 1 /*CodeFlow*/, {enableBytes, enableBytes + sizeof(enableBytes)},
                                   {disableBytes, disableBytes + sizeof(disableBytes)} }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, {hookOriginal, hookOriginal + sizeof(hookOriginal)} }
    };

    uint32_t len = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, len);
    InstallPatchesFromPayload(payload, len);
    delete[] payload;

    // Enable
    unsigned char cmd[8] = {};
    uint32_t psId = 1, enable = 1;
    std::memcpy(cmd, &psId, 4); std::memcpy(cmd + 4, &enable, 4);
    SetRuntimePatchSetFromPayload(cmd, 8);

    unsigned char enabledBytes[2] = {};
    std::memcpy(enabledBytes, reinterpret_cast<void*>(static_cast<uintptr_t>(psAddr)), 2);
    Expect(std::equal(enabledBytes, enabledBytes + 2, enableBytes),
        "Memory must be EnableBytes after enable");

    // Restore normally
    Expect(RestoreInstalledPatches(),
        "Restore with CodeFlow entry must return true");
    Expect(!IsPatchSetEnabled(1),
        "PatchSet must be disabled after Restore");

    unsigned char restored[2] = {};
    std::memcpy(restored, reinterpret_cast<void*>(static_cast<uintptr_t>(psAddr)), 2);
    Expect(std::equal(restored, restored + 2, disableBytes),
        "Memory must be DisableBytes after Restore");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestIpConflictCapture_ResetBetweenAttempts()
{
    using namespace RayaTrainer::agent;

    // First attempt with conflict seam, second without.
    const unsigned char disableBytes[] = { 0x90, 0x90 };
    const unsigned char enableBytes[] =  { 0xEB, 0xFE };
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };

    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    const uint32_t psAddr = AllocPatchBuffer(disableBytes, sizeof(disableBytes));
    if (hookAddr == 0 || psAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        FreePatchBuffer(hookAddr); FreePatchBuffer(psAddr);
        return;
    }

    TestPatchSetEntry entries[] = {
        { psAddr, 1 /*CodeFlow*/, {enableBytes, enableBytes + sizeof(enableBytes)},
                                   {disableBytes, disableBytes + sizeof(disableBytes)} }
    };
    TestHook hooks[] = {
        { hookAddr, 41, 5, {hookOriginal, hookOriginal + sizeof(hookOriginal)} }
    };

    uint32_t len = 0;
    auto* payload = BuildV11Payload(entries, 1, hooks, 1, len);
    InstallPatchesFromPayload(payload, len);
    delete[] payload;

    // Force IP conflict
    SetIpConflictTestSeamMode(2);
    unsigned char cmd[8] = {};
    uint32_t psId = 1, enable = 1;
    std::memcpy(cmd, &psId, 4); std::memcpy(cmd + 4, &enable, 4);
    SetRuntimePatchSetFromPayload(cmd, 8);

    // Conflict should be captured
    PatchSetIpConflictCapture conflict;
    Expect(TryGetLastIpConflict(conflict),
        "IP conflict must be captured after forced conflict");
    Expect(conflict.PatchSetId == 1, "Conflict PatchSetId must be 1");

    // Reset seam and try again — should succeed
    SetIpConflictTestSeamMode(0);
    const auto status = SetRuntimePatchSetFromPayload(cmd, 8);
    Expect(status == AgentStatusCode::Ok,
        "Second attempt after clearing seam must succeed");

    // Verify IP conflict capture is now from the NEW (successful) attempt.
    // Since the second attempt succeeded, no conflict was captured.
    // TryGetLastIpConflict should return false (no conflict).
    Expect(!TryGetLastIpConflict(conflict),
        "IP conflict capture must be cleared after successful retry");

    Expect(IsPatchSetEnabled(1),
        "PatchSet must be enabled after successful retry");

    FreePatchBuffer(psAddr);
    FreePatchBuffer(hookAddr);
}

void TestCodeFlow_RollbackOnDataWriteFailure_RestoresCodeFlowBytes()
{
    // TODO: requires InjectArbitraryFailure seam in WriteProcessLocalMemory.
    // Skipping this test because the current architecture has no mock seam in the
    // memory-write path. A future refactor can add a test hook.
    std::cout << "  [SKIP] CodeFlowRollback: requires InjectArbitraryFailure seam\n";
}

void TestIsHookInstalled_ReflectsInstallState()
{
    using namespace RayaTrainer::agent;

    // No hooks installed initially.
    Expect(!IsHookInstalled(41),
        "IsHookInstalled(41) must be false before any install");
    Expect(!IsHookInstalled(99),
        "IsHookInstalled(99) must be false before any install");

    // Install a hook with NativeHookId=99.
    const unsigned char hookOriginal[] = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    const uint32_t hookAddr = AllocPatchBuffer(hookOriginal, sizeof(hookOriginal));
    if (hookAddr == 0)
    {
        Expect(false, "VirtualAlloc failed");
        return;
    }

    TestHook hooks[] = {
        { hookAddr, 99, 5, { hookOriginal, hookOriginal + sizeof(hookOriginal) } }
    };

    uint32_t payloadLen = 0;
    auto* payload = BuildV11Payload(nullptr, 0, hooks, 1, payloadLen);

    // Need a valid empty payload with patchSetCount=0
    // Rebuild: we need a payload with 0 patchSets
    delete[] payload;
    // Build manually: PatchSetCount=0 + HookCount=1 + hook
    uint32_t manualLen = 4 + 4 + 4 + 4 + 4 + 5; // PatchSetCount + HookCount + Address + NativeHookId + PatchLength + ByteCount + bytes
    auto* manualPayload = new unsigned char[manualLen];
    uint32_t offset = 0;
    const uint32_t psCount = 0;
    std::memcpy(manualPayload + offset, &psCount, 4); offset += 4;
    const uint32_t hCount = 1;
    std::memcpy(manualPayload + offset, &hCount, 4); offset += 4;
    std::memcpy(manualPayload + offset, &hookAddr, 4); offset += 4;
    const uint32_t nhId = 99;
    std::memcpy(manualPayload + offset, &nhId, 4); offset += 4;
    const uint32_t pLen = 5;
    std::memcpy(manualPayload + offset, &pLen, 4); offset += 4;
    const uint32_t obLen = 5;
    std::memcpy(manualPayload + offset, &obLen, 4); offset += 4;
    std::memcpy(manualPayload + offset, hookOriginal, 5); offset += 5;

    const auto installStatus = InstallPatchesFromPayload(manualPayload, manualLen);
    Expect(installStatus == AgentStatusCode::Ok,
        "InstallPatches with hook 99 must succeed");

    delete[] manualPayload;

    Expect(IsHookInstalled(99),
        "IsHookInstalled(99) must be true after installing hook 99");
    Expect(!IsHookInstalled(41),
        "IsHookInstalled(41) must still be false (hook 41 not installed)");

    FreePatchBuffer(hookAddr);
}
}

int RunAgentPatchSetTests()
{
    // L3a tests
    TestSetRuntimePatchSet_EmptyPayload_ReturnsInvalid();
    TestSetRuntimePatchSet_UnknownPatchSetId_ReturnsInvalid();
    TestSetRuntimePatchSet_NotInstalled_ReturnsInvalid();
    TestSetRuntimePatchSet_FrameRateUnlock_NoHook41_ReturnsInvalid();
    TestSetRuntimePatchSet_FrameRateUnlock_WithHook41_Enables();
    TestSetRuntimePatchSet_IdempotentEnable_ReturnsOk();
    TestSetRuntimePatchSet_IdempotentDisable_ReturnsOk();
    TestSetRuntimePatchSet_PreWriteMismatch_ReturnsPatchMismatch();
    TestSetRuntimePatchSet_RollbackOnWriteFailure_StateUnchanged();
    TestRestore_RestoresEnabledPatchSets();
    TestInstallPatches_PatchSetWithMismatchedEnableDisableLengths_ReturnsInvalid();
    TestInstallPatches_PatchSetCurrentBytesNotDisabled_ReturnsPatchMismatch();
    TestDerivedStateReset_AllowsArbitraryCurrentValueAndWritesOnBothTransitions();
    TestInstallPatches_DuplicatePatchSetEntryAddress_ReturnsInvalid();
    TestInstallPatches_OldV10WritesPayload_Rejected();
    TestIsHookInstalled_ReflectsInstallState();

    // L3b CodeFlow tests
    TestCodeFlow_EntryWithKind1_ClassifiedAsCodeFlow();
    TestCodeFlow_TransactionCommits_WhenIpOutsideRange();
    TestCodeFlow_TransactionAborts_WhenIpInsideRange();
    TestCodeFlow_TransactionAborts_ClearsStickyState();
    TestCodeFlow_DataEntriesBatched_AllCommitted();
    TestCodeFlow_MixedPatchSet_CodeFlowFirstThenData();
    TestRestore_WithCodeFlowEntries_AppliesSameIpGuard();
    TestRestore_WithCodeFlowEntries_CommitSuccess();
    TestIpConflictCapture_ResetBetweenAttempts();
    TestCodeFlow_RollbackOnDataWriteFailure_RestoresCodeFlowBytes();

    if (g_pst_failures != 0)
    {
        std::cerr << g_pst_failures << " AgentPatchSet test(s) FAILED\n";
    }
    return g_pst_failures;
}
