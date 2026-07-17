#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <limits>
#include <utility>
#include <vector>

#include <Zydis/Decoder.h>

#include "AgentPatchManager.h"
#include "AgentFeatureState.h"
#include "AgentGameApi.h"
#include "AgentGameThreadDispatcher.h"
#include "AgentNativeHooks.h"
#include "AgentSignatureScanner.h"

namespace RayaTrainer::agent
{
namespace
{
// Test seam for IP conflict injection. 0=normal, 1=force-pass, 2=force-conflict.
// Declared early because SuspendedThreadSet::IsIpOutsideRanges references it.
int g_ipConflictTestSeamMode = 0;

struct InstalledPatch
{
    uint32_t Address;
    std::vector<unsigned char> OriginalBytes;
    std::vector<unsigned char> PatchBytes;
    void* Trampoline;
    uint32_t NativeHookId = 0;
};

struct ParsedWrite
{
    uint32_t Address;
    std::vector<unsigned char> Bytes;
};

struct ParsedHook
{
    uint32_t Address;
    uint32_t NativeHookId;
    uint32_t PatchLength;
    std::vector<unsigned char> OriginalBytes;
};

std::vector<InstalledPatch> g_installedPatches;
std::vector<InstalledPatchSet> g_installedPatchSets;
constexpr size_t kTrampolineCapacity = 512;

struct CodeRange
{
    uint32_t Address;
    uint32_t Length;
};

class SuspendedThreadSet
{
public:
    SuspendedThreadSet()
    {
        threads_.reserve(128);
    }

    ~SuspendedThreadSet()
    {
        ResumeAll();
    }

    bool SuspendAllOtherThreads()
    {
        const DWORD processId = GetCurrentProcessId();
        const DWORD currentThreadId = GetCurrentThreadId();
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
        if (snapshot == INVALID_HANDLE_VALUE)
        {
            return false;
        }

        THREADENTRY32 entry = {};
        entry.dwSize = sizeof(entry);
        BOOL hasEntry = Thread32First(snapshot, &entry);
        while (hasEntry)
        {
            if (entry.th32OwnerProcessID == processId && entry.th32ThreadID != currentThreadId)
            {
                HANDLE thread = OpenThread(
                    THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT,
                    FALSE,
                    entry.th32ThreadID);
                if (thread == nullptr)
                {
                    // Thread snapshots are inherently racy. A short-lived worker may exit
                    // between Thread32Next and OpenThread; that thread no longer needs to be
                    // suspended and must not abort the whole patch transaction.
                    if (GetLastError() == ERROR_INVALID_PARAMETER)
                    {
                        hasEntry = Thread32Next(snapshot, &entry);
                        continue;
                    }
                    CloseHandle(snapshot);
                    return false;
                }

                if (SuspendThread(thread) == static_cast<DWORD>(-1))
                {
                    CloseHandle(thread);
                    CloseHandle(snapshot);
                    return false;
                }

                CONTEXT context = {};
                context.ContextFlags = CONTEXT_CONTROL;
                if (!GetThreadContext(thread, &context))
                {
                    const DWORD error = GetLastError();
                    ResumeThread(thread);
                    CloseHandle(thread);
                    if (error == ERROR_INVALID_HANDLE || error == ERROR_INVALID_PARAMETER)
                    {
                        hasEntry = Thread32Next(snapshot, &entry);
                        continue;
                    }
                    CloseHandle(snapshot);
                    return false;
                }

                threads_.push_back({ thread, context.Eip, entry.th32ThreadID });
            }

            hasEntry = Thread32Next(snapshot, &entry);
        }

        CloseHandle(snapshot);
        return true;
    }

    bool IsOutside(const std::vector<CodeRange>& ranges) const
    {
        for (const auto& thread : threads_)
        {
            for (const auto& range : ranges)
            {
                if (thread.InstructionPointer >= range.Address &&
                    thread.InstructionPointer < range.Address + range.Length)
                {
                    return false;
                }
            }
        }
        return true;
    }

    // Returns true if every suspended thread's IP is at least kIpGuardPadding bytes
    // away from all given ranges (range plus guard zone on each side).
    // Used for CodeFlow entries: the executing thread must not be within ±16 bytes of
    // the patched address to avoid tearing a live instruction.
    static constexpr uint32_t kIpGuardPadding = 16;

    bool IsIpOutsideRanges(const std::vector<CodeRange>& ranges) const
    {
        // Test seam override
        if (g_ipConflictTestSeamMode == 1) return true;
        if (g_ipConflictTestSeamMode == 2) return false;

        for (const auto& thread : threads_)
        {
            for (const auto& range : ranges)
            {
                const uint32_t guardStart = range.Address >= kIpGuardPadding
                    ? range.Address - kIpGuardPadding
                    : 0;
                if (thread.InstructionPointer >= guardStart &&
                    thread.InstructionPointer < range.Address + range.Length + kIpGuardPadding)
                {
                    return false;
                }
            }
        }
        return true;
    }

    // When IsIpOutsideRanges returns false, use this to retrieve the first conflicting
    // thread's details for diagnostic capture.
    bool GetFirstIpConflict(
        const std::vector<CodeRange>& ranges,
        uint32_t& outThreadId,
        uint32_t& outIp) const
    {
        for (const auto& thread : threads_)
        {
            for (const auto& range : ranges)
            {
                const uint32_t guardStart = range.Address >= kIpGuardPadding
                    ? range.Address - kIpGuardPadding
                    : 0;
                if (thread.InstructionPointer >= guardStart &&
                    thread.InstructionPointer < range.Address + range.Length + kIpGuardPadding)
                {
                    outThreadId = thread.ThreadId;
                    outIp = thread.InstructionPointer;
                    return true;
                }
            }
        }
        return false;
    }

private:
    struct SuspendedThread
    {
        HANDLE Handle;
        uint32_t InstructionPointer;
        DWORD ThreadId;
    };

    void ResumeAll()
    {
        for (auto index = threads_.rbegin(); index != threads_.rend(); ++index)
        {
            ResumeThread(index->Handle);
            CloseHandle(index->Handle);
        }
        threads_.clear();
    }

    std::vector<SuspendedThread> threads_;
};

// Last hook mismatch captured during InstallPatches, so the host can query it via
// GetMismatchDiagnostics instead of only seeing a bare PatchMismatch status code. Mirrors
// the DumpBytesBefore/DumpBytesAfter window the external-memory backend uses, so the host
// can feed the managed diagnostics writer with the same structured evidence.
PatchMismatchCapture g_lastMismatch;
constexpr uint32_t kMismatchDumpBefore = 16;
constexpr uint32_t kMismatchDumpAfter = 96;

// IP conflict diagnostic: populated when a CodeFlow entry transaction is aborted because
// a suspended thread's instruction pointer falls within the ±16 byte guard zone of the
// target address. The host queries this via TryGetLastIpConflict to emit
// agent.patchset_codeflow_ip_conflict diagnostics.
PatchSetIpConflictCapture g_lastIpConflict;

// Monotonic sequence counter for diagnostic recency ordering. Incremented before every
// mismatch or IP conflict capture so WriteMismatchDiagnosticsResponse can select the
// most recent diagnostic regardless of source.
uint32_t g_nextDiagnosticSequence = 1;

void ResetLastIpConflict()
{
    g_lastIpConflict = PatchSetIpConflictCapture{};
}

void CapturePatchSetIpConflict(
    uint32_t patchSetId,
    uint32_t entryAddress,
    uint32_t conflictingThreadId,
    uint32_t observedIp,
    bool isRestore)
{
    g_lastIpConflict.PatchSetId = patchSetId;
    g_lastIpConflict.ConflictingEntryAddress = entryAddress;
    g_lastIpConflict.ConflictingThreadId = conflictingThreadId;
    g_lastIpConflict.ObservedIp = observedIp;
    g_lastIpConflict.IsRestore = isRestore;
    g_lastIpConflict.Sequence = g_nextDiagnosticSequence++;
}

// Forward declaration: CaptureMismatch needs to read a dump window around the hook site,
// but ReadProcessLocalMemory is defined further down in this anonymous namespace.
bool ReadProcessLocalMemory(uint32_t address, unsigned char* bytes, size_t length);

void ResetLastMismatch()
{
    g_lastMismatch = PatchMismatchCapture{};
}

void CaptureMismatch(const ParsedHook& hook, const std::vector<unsigned char>& actual)
{
    PatchMismatchCapture capture;
    capture.HookAddress = hook.Address;
    capture.Expected = hook.OriginalBytes;
    capture.Actual = actual;
    capture.OriginKind = 0;         // Hook origin
    capture.SubjectId = hook.NativeHookId;
    capture.Sequence = g_nextDiagnosticSequence++;

    // Read a dump window around the hook site. Start before the hook address when possible
    // (the leading bytes are context for the failing instruction), then extend past the
    // expected bytes so the host can disassemble forward like the external backend does.
    const uint32_t dumpStart =
        hook.Address >= kMismatchDumpBefore ? hook.Address - kMismatchDumpBefore : hook.Address;
    const uint32_t dumpBefore = hook.Address - dumpStart;
    const uint32_t dumpLength = dumpBefore +
        std::max<uint32_t>(static_cast<uint32_t>(hook.OriginalBytes.size()), kMismatchDumpAfter);

    capture.Dump.resize(dumpLength);
    if (ReadProcessLocalMemory(dumpStart, capture.Dump.data(), dumpLength))
    {
        g_lastMismatch = std::move(capture);
    }
    else
    {
        // Keep address + expected + actual even if the dump read failed; the host can still
        // report the mismatch without disassembly context.
        capture.Dump.clear();
        g_lastMismatch = std::move(capture);
    }
}

class PayloadReader
{
public:
    PayloadReader(const unsigned char* data, uint32_t length)
        : data_(data), length_(length)
    {
    }

    bool ReadUInt32(uint32_t& value)
    {
        if (remaining() < sizeof(uint32_t))
        {
            return false;
        }

        std::memcpy(&value, data_ + offset_, sizeof(uint32_t));
        offset_ += sizeof(uint32_t);
        return true;
    }

    bool ReadUInt8(uint8_t& value)
    {
        if (remaining() < sizeof(uint8_t))
        {
            return false;
        }

        value = data_[offset_];
        offset_ += sizeof(uint8_t);
        return true;
    }

    bool ReadBytes(std::vector<unsigned char>& bytes)
    {
        uint32_t length = 0;
        if (!ReadUInt32(length) || remaining() < length)
        {
            return false;
        }

        bytes.assign(data_ + offset_, data_ + offset_ + length);
        offset_ += length;
        return true;
    }

    bool AtEnd() const
    {
        return offset_ == length_;
    }

private:
    uint32_t remaining() const
    {
        return length_ - offset_;
    }

    const unsigned char* data_;
    uint32_t length_;
    uint32_t offset_ = 0;
};

bool WriteProcessLocalMemory(uint32_t address, const unsigned char* bytes, size_t length)
{
    if (address == 0 || bytes == nullptr || length == 0)
    {
        return false;
    }

    DWORD oldProtect = 0;
    auto* target = reinterpret_cast<void*>(static_cast<uintptr_t>(address));
    if (!VirtualProtect(target, length, PAGE_EXECUTE_READWRITE, &oldProtect))
    {
        return false;
    }

    __try
    {
        std::memcpy(target, bytes, length);
        FlushInstructionCache(GetCurrentProcess(), target, length);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        DWORD ignored = 0;
        VirtualProtect(target, length, oldProtect, &ignored);
        return false;
    }

    DWORD ignored = 0;
    VirtualProtect(target, length, oldProtect, &ignored);
    return true;
}

bool ReadProcessLocalMemory(uint32_t address, unsigned char* bytes, size_t length)
{
    if (address == 0 || bytes == nullptr || length == 0)
    {
        return false;
    }

    __try
    {
        std::memcpy(bytes, reinterpret_cast<const void*>(static_cast<uintptr_t>(address)), length);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }

    return true;
}

std::vector<unsigned char> EncodeNearJump(uint32_t source, uint32_t target, uint32_t patchLength)
{
    std::vector<unsigned char> bytes(patchLength, 0x90);
    bytes[0] = 0xE9;
    const auto relative = static_cast<int32_t>(target - (source + 5));
    std::memcpy(bytes.data() + 1, &relative, sizeof(relative));
    return bytes;
}

void AppendUInt32(std::vector<unsigned char>& bytes, uint32_t value)
{
    const auto offset = bytes.size();
    bytes.resize(offset + sizeof(value));
    std::memcpy(bytes.data() + offset, &value, sizeof(value));
}

void PatchRelative32(
    std::vector<unsigned char>& bytes,
    size_t operandOffset,
    uint32_t instructionEnd,
    uint32_t target)
{
    const auto relative = static_cast<int32_t>(target - instructionEnd);
    std::memcpy(bytes.data() + operandOffset, &relative, sizeof(relative));
}

bool AppendRelocatedOriginalBytes(
    const ParsedHook& hook,
    uint32_t destination,
    const std::vector<unsigned char>& original,
    std::vector<unsigned char>& relocated)
{
    ZydisDecoder decoder = {};
    if (ZYAN_FAILED(ZydisDecoderInit(
            &decoder,
            ZYDIS_MACHINE_MODE_LEGACY_32,
            ZYDIS_STACK_WIDTH_32)))
    {
        return false;
    }

    relocated.clear();
    for (size_t offset = 0; offset < original.size();)
    {
        ZydisDecodedInstruction instruction = {};
        ZydisDecodedOperand operands[ZYDIS_MAX_OPERAND_COUNT] = {};
        if (ZYAN_FAILED(ZydisDecoderDecodeFull(
                &decoder,
                original.data() + offset,
                original.size() - offset,
                &instruction,
                operands)) ||
            instruction.length == 0 ||
            offset + instruction.length > original.size())
        {
            return false;
        }

        const auto instructionAddress = hook.Address + static_cast<uint32_t>(offset);
        const auto relocatedAddress = destination + static_cast<uint32_t>(relocated.size());
        const auto& relative = instruction.raw.imm[0];
        if (!relative.is_relative)
        {
            relocated.insert(
                relocated.end(),
                original.begin() + offset,
                original.begin() + offset + instruction.length);
            offset += instruction.length;
            continue;
        }

        const auto target = static_cast<uint32_t>(
            instructionAddress + instruction.length + static_cast<int32_t>(relative.value.s));
        if (relative.size == 32)
        {
            const auto relocatedOffset = relocated.size();
            relocated.insert(
                relocated.end(),
                original.begin() + offset,
                original.begin() + offset + instruction.length);
            const auto relocatedEnd = relocatedAddress + instruction.length;
            const auto newRelative = static_cast<int32_t>(target - relocatedEnd);
            std::memcpy(
                relocated.data() + relocatedOffset + relative.offset,
                &newRelative,
                sizeof(newRelative));
        }
        else if (relative.size == 8 && original[offset] >= 0x70 && original[offset] <= 0x7F)
        {
            relocated.push_back(0x0F);
            relocated.push_back(static_cast<unsigned char>(0x80 + original[offset] - 0x70));
            const auto relocatedEnd = relocatedAddress + 6u;
            const auto newRelative = static_cast<int32_t>(target - relocatedEnd);
            AppendUInt32(relocated, static_cast<uint32_t>(newRelative));
        }
        else if (relative.size == 8 && original[offset] == 0xEB)
        {
            relocated.push_back(0xE9);
            const auto relocatedEnd = relocatedAddress + 5u;
            const auto newRelative = static_cast<int32_t>(target - relocatedEnd);
            AppendUInt32(relocated, static_cast<uint32_t>(newRelative));
        }
        else
        {
            return false;
        }

        offset += instruction.length;
    }

    return true;
}

void* BuildNativeTrampoline(const ParsedHook& hook, const std::vector<unsigned char>& original)
{
    auto* memory = static_cast<unsigned char*>(VirtualAlloc(
        nullptr,
        kTrampolineCapacity,
        MEM_COMMIT | MEM_RESERVE,
        PAGE_EXECUTE_READWRITE));
    if (memory == nullptr)
    {
        return nullptr;
    }

    const auto base = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(memory));
    const auto continuation = hook.Address + hook.PatchLength;
    std::vector<unsigned char> code;
    code.reserve(kTrampolineCapacity);
    // PUSHAD records ESP after PUSHFD. Normalize the saved OriginalEsp slot back to the
    // hook-entry stack pointer before exposing NativeHookContext to C++ handlers.
    code.insert(code.end(), {
        0x9C,                         // pushfd
        0x60,                         // pushad
        0x83, 0x44, 0x24, 0x0C, 0x04, // add dword ptr [esp+0C],4
        0x8B, 0xC4,                   // mov eax,esp
        0x50,                         // push eax
        0x68                          // push hook id
    });
    AppendUInt32(code, hook.NativeHookId);
    code.push_back(0xB8);
    AppendUInt32(code, static_cast<uint32_t>(reinterpret_cast<uintptr_t>(&AgentNativeHookBridge)));
    code.insert(code.end(), { 0xFF, 0xD0, 0x83, 0xC4, 0x08, 0x85, 0xC0, 0x0F, 0x84 });
    const auto executeOriginalOperand = code.size();
    AppendUInt32(code, 0);
    code.insert(code.end(), { 0x83, 0xF8, 0x01, 0x0F, 0x84 });
    const auto skipOriginalOperand = code.size();
    AppendUInt32(code, 0);
    code.insert(code.end(), { 0x83, 0xF8, 0x02, 0x0F, 0x84 });
    const auto returnOperand = code.size();
    AppendUInt32(code, 0);
    code.insert(code.end(), { 0x83, 0xF8, 0x03, 0x0F, 0x84 });
    const auto return4Operand = code.size();
    AppendUInt32(code, 0);
    code.insert(code.end(), { 0x83, 0xF8, 0x04, 0x0F, 0x84 });
    const auto skipAdjust4Operand = code.size();
    AppendUInt32(code, 0);
    // Mode 5: callee-cleans-2-args direct return (retn 8). Used by hooks that need to
    // short-circuit a __fastcall(this, a2, a3) function and supply their own EDX:EAX.
    code.insert(code.end(), { 0x83, 0xF8, 0x05, 0x0F, 0x84 });
    const auto return8Operand = code.size();
    AppendUInt32(code, 0);
    code.insert(code.end(), { 0x89, 0x44, 0x24, 0x1C, 0x61, 0x9D, 0xFF, 0xE0 });

    const auto returnOffset = code.size();
    code.insert(code.end(), { 0x61, 0x9D, 0xC3 });

    const auto return4Offset = code.size();
    code.insert(code.end(), { 0x61, 0x9D, 0xC2, 0x04, 0x00 });

    const auto return8Offset = code.size();
    code.insert(code.end(), { 0x61, 0x9D, 0xC2, 0x08, 0x00 });

    const auto skipOriginalOffset = code.size();
    code.insert(code.end(), { 0x61, 0x9D, 0xE9 });
    const auto skipJumpOperand = code.size();
    AppendUInt32(code, 0);

    const auto skipAdjust4Offset = code.size();
    code.insert(code.end(), { 0x61, 0x9D, 0x83, 0xC4, 0x04, 0xE9 });
    const auto skipAdjust4JumpOperand = code.size();
    AppendUInt32(code, 0);

    const auto executeOriginalOffset = code.size();
    code.insert(code.end(), { 0x61, 0x9D });
    std::vector<unsigned char> relocated;
    if (!AppendRelocatedOriginalBytes(
            hook,
            base + static_cast<uint32_t>(code.size()),
            original,
            relocated))
    {
        VirtualFree(memory, 0, MEM_RELEASE);
        return nullptr;
    }
    code.insert(code.end(), relocated.begin(), relocated.end());
    code.push_back(0xE9);
    const auto executeJumpOperand = code.size();
    AppendUInt32(code, 0);

    if (code.size() > kTrampolineCapacity)
    {
        VirtualFree(memory, 0, MEM_RELEASE);
        return nullptr;
    }

    PatchRelative32(code, executeOriginalOperand,
        base + static_cast<uint32_t>(executeOriginalOperand + 4),
        base + static_cast<uint32_t>(executeOriginalOffset));
    PatchRelative32(code, skipOriginalOperand,
        base + static_cast<uint32_t>(skipOriginalOperand + 4),
        base + static_cast<uint32_t>(skipOriginalOffset));
    PatchRelative32(code, returnOperand,
        base + static_cast<uint32_t>(returnOperand + 4),
        base + static_cast<uint32_t>(returnOffset));
    PatchRelative32(code, return4Operand,
        base + static_cast<uint32_t>(return4Operand + 4),
        base + static_cast<uint32_t>(return4Offset));
    PatchRelative32(code, skipAdjust4Operand,
        base + static_cast<uint32_t>(skipAdjust4Operand + 4),
        base + static_cast<uint32_t>(skipAdjust4Offset));
    PatchRelative32(code, return8Operand,
        base + static_cast<uint32_t>(return8Operand + 4),
        base + static_cast<uint32_t>(return8Offset));
    PatchRelative32(code, skipJumpOperand,
        base + static_cast<uint32_t>(skipJumpOperand + 4), continuation);
    PatchRelative32(code, executeJumpOperand,
        base + static_cast<uint32_t>(executeJumpOperand + 4), continuation);
    PatchRelative32(code, skipAdjust4JumpOperand,
        base + static_cast<uint32_t>(skipAdjust4JumpOperand + 4), continuation);

    std::memcpy(memory, code.data(), code.size());
    FlushInstructionCache(GetCurrentProcess(), memory, code.size());
    RegisterNativeHookAddress(hook.NativeHookId, hook.Address, continuation);
    return memory;
}

bool InstallHook(const ParsedHook& hook)
{
    if (hook.PatchLength < 5 ||
        hook.OriginalBytes.empty() ||
        hook.OriginalBytes.size() > hook.PatchLength)
    {
        return false;
    }

    std::vector<unsigned char> current(hook.PatchLength);
    if (!ReadProcessLocalMemory(hook.Address, current.data(), current.size()))
    {
        // Capture with an empty actual so the host still sees which hook failed and why
        // (the read itself failed, not a byte mismatch).
        CaptureMismatch(hook, {});
        return false;
    }

    const bool exactMatch = std::equal(
        hook.OriginalBytes.begin(),
        hook.OriginalBytes.end(),
        current.begin());
    if (!exactMatch)
    {
        std::vector<unsigned char> signatureVerifiedBytes;
        if (!TryReadVerifiedBuiltInHookBytes(
                hook.Address,
                hook.PatchLength,
                signatureVerifiedBytes) ||
            signatureVerifiedBytes != current)
        {
            const std::vector<unsigned char> actual(
                current.begin(),
                current.begin() + hook.OriginalBytes.size());
            CaptureMismatch(hook, actual);
            return false;
        }
    }

    auto* trampoline = BuildNativeTrampoline(hook, current);
    if (trampoline == nullptr)
    {
        return false;
    }
    const auto effectiveTarget = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(trampoline));

    const auto patchBytes = EncodeNearJump(hook.Address, effectiveTarget, hook.PatchLength);
    std::vector<unsigned char> verified(hook.PatchLength);
    const std::vector<CodeRange> guardedRanges =
    {
        { hook.Address, hook.PatchLength }
    };

    bool committed = false;
    bool changedBeforeCommit = false;
    for (uint32_t attempt = 0; attempt < 100 && !committed; ++attempt)
    {
        {
            SuspendedThreadSet threads;
            if (!threads.SuspendAllOtherThreads())
            {
                // A thread may be created or exit while the snapshot is being acquired.
                // Resume any threads already captured and retry the full transaction.
            }
            else if (!threads.IsOutside(guardedRanges))
            {
                // A game thread is currently executing the bytes that will be replaced.
                // Resume it and retry instead of resuming it in the middle of a new JMP.
            }
            else if (!ReadProcessLocalMemory(hook.Address, verified.data(), verified.size()) ||
                     verified != current)
            {
                changedBeforeCommit = true;
                break;
            }
            else if (WriteProcessLocalMemory(hook.Address, patchBytes.data(), patchBytes.size()))
            {
                committed = true;
            }
            else
            {
                break;
            }
        }

        if (!committed)
        {
            Sleep(1);
        }
    }

    if (!committed)
    {
        if (changedBeforeCommit)
        {
            CaptureMismatch(hook, verified);
        }
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return false;
    }

    // Preserve the live bytes, including wildcarded SKU-specific operands, so restore writes
    // back exactly what this process contained before the hook was installed.
    g_installedPatches.push_back(InstalledPatch{
        hook.Address,
        std::move(current),
        patchBytes,
        trampoline,
        hook.NativeHookId });
    return true;
}
}

bool RestoreInstalledPatches()
{
    // 1. Restore enabled patch sets first (if any)
    // 1. Restore enabled patch sets first, applying CodeFlow rules
    bool allPatchSetsRestored = true;
    for (auto& ps : g_installedPatchSets)
    {
        if (!ps.IsEnabled)
        {
            continue;
        }

        // For restore, target = DisableBytes, expected pre = EnableBytes
        // Partition by Kind for CodeFlow-first ordering
        std::vector<const PatchSetEntry*> cfEntries;
        std::vector<const PatchSetEntry*> dataEntries;
        cfEntries.reserve(ps.Definition.Entries.size());
        dataEntries.reserve(ps.Definition.Entries.size());

        for (const auto& entry : ps.Definition.Entries)
        {
            if (entry.Kind == PatchSetEntryKind::CodeFlow)
            {
                cfEntries.push_back(&entry);
            }
            else
            {
                dataEntries.push_back(&entry);
            }
        }

        // Build full range list for initial coarse is-outside check
        std::vector<CodeRange> allPsRanges;
        allPsRanges.reserve(ps.Definition.Entries.size());
        for (const auto* e : cfEntries)
        {
            allPsRanges.push_back(
                { e->Address, static_cast<uint32_t>(e->DisableBytes.size()) });
        }
        for (const auto* e : dataEntries)
        {
            allPsRanges.push_back(
                { e->Address, static_cast<uint32_t>(e->DisableBytes.size()) });
        }

        bool psRestored = false;
        for (uint32_t attempt = 0; attempt < 100 && !psRestored; ++attempt)
        {
            {
                SuspendedThreadSet threads;
                if (!threads.SuspendAllOtherThreads())
                {
                    continue;
                }
                if (!threads.IsOutside(allPsRanges))
                {
                    continue;
                }

                // CodeFlow entries first, each with IP guard
                bool cfOk = true;
                for (const auto* entry : cfEntries)
                {
                    CodeRange range{ entry->Address,
                        static_cast<uint32_t>(entry->DisableBytes.size()) };
                    const std::vector<CodeRange> singleRange = { range };

                    if (!threads.IsIpOutsideRanges(singleRange))
                    {
                        uint32_t conflictThreadId = 0;
                        uint32_t conflictIp = 0;
                        threads.GetFirstIpConflict(singleRange,
                            conflictThreadId, conflictIp);
                        CapturePatchSetIpConflict(
                            ps.Definition.Id,
                            entry->Address,
                            conflictThreadId,
                            conflictIp,
                            /*isRestore=*/true);
                        cfOk = false;
                        break;
                    }
                }

                if (!cfOk)
                {
                    // IP conflict on a CodeFlow entry — mark partial failure,
                    // still try remaining entries.
                    continue;
                }

                psRestored = true;
                // Write CodeFlow entries first (IP already verified above)
                for (const auto* entry : cfEntries)
                {
                    if (!WriteProcessLocalMemory(
                            entry->Address,
                            entry->DisableBytes.data(),
                            entry->DisableBytes.size()))
                    {
                        psRestored = false;
                    }
                }
                // Then Data entries
                for (const auto* entry : dataEntries)
                {
                    if (!WriteProcessLocalMemory(
                            entry->Address,
                            entry->DisableBytes.data(),
                            entry->DisableBytes.size()))
                    {
                        psRestored = false;
                    }
                }
            }

            if (!psRestored)
            {
                Sleep(1);
            }
        }

        ps.IsEnabled = false;
        if (!psRestored)
        {
            allPatchSetsRestored = false;
        }
    }

    // 2. Restore hooks (existing logic)
    if (g_installedPatches.empty())
    {
        if (!allPatchSetsRestored)
        {
            return false;
        }
        g_installedPatchSets.clear();
        AgentGameThreadDispatcher::Reset();
        ResetNativeHookRuntime();
        return true;
    }

    std::vector<CodeRange> guardedRanges;
    guardedRanges.reserve(g_installedPatches.size() * 2);
    for (const auto& patch : g_installedPatches)
    {
        guardedRanges.push_back(
            { patch.Address, static_cast<uint32_t>(patch.OriginalBytes.size()) });
        guardedRanges.push_back(
            { static_cast<uint32_t>(reinterpret_cast<uintptr_t>(patch.Trampoline)),
              static_cast<uint32_t>(kTrampolineCapacity) });
    }

    bool hooksRestored = false;
    for (uint32_t attempt = 0; attempt < 100 && !hooksRestored; ++attempt)
    {
        {
            SuspendedThreadSet threads;
            if (!threads.SuspendAllOtherThreads())
            {
                // Retry transient thread-snapshot races.
            }
            else if (threads.IsOutside(guardedRanges))
            {
                hooksRestored = true;
                for (auto index = g_installedPatches.rbegin();
                     index != g_installedPatches.rend();
                     ++index)
                {
                    if (!WriteProcessLocalMemory(
                            index->Address,
                            index->OriginalBytes.data(),
                            index->OriginalBytes.size()))
                    {
                        hooksRestored = false;
                        for (const auto& patch : g_installedPatches)
                        {
                            WriteProcessLocalMemory(
                                patch.Address,
                                patch.PatchBytes.data(),
                                patch.PatchBytes.size());
                        }
                        break;
                    }
                }
            }
        }

        if (!hooksRestored)
        {
            Sleep(1);
        }
    }

    if (!hooksRestored)
    {
        return false;
    }

    for (const auto& patch : g_installedPatches)
    {
        if (patch.Trampoline != nullptr)
        {
            VirtualFree(patch.Trampoline, 0, MEM_RELEASE);
        }
    }
    g_installedPatches.clear();

    if (!allPatchSetsRestored)
    {
        return false;
    }

    g_installedPatchSets.clear();
    AgentGameThreadDispatcher::Reset();
    ResetNativeHookRuntime();
    return true;
}

uint32_t InstalledPatchCount()
{
    return static_cast<uint32_t>(g_installedPatches.size());
}

bool TryGetLastMismatch(PatchMismatchCapture& outCapture)
{
    // A pending mismatch is signalled by a non-zero hook address; CaptureMismatch always
    // records the failing hook address, and ResetLastMismatch zeroes the struct.
    if (g_lastMismatch.HookAddress == 0)
    {
        return false;
    }

    outCapture = g_lastMismatch;
    return true;
}

bool TryGetLastIpConflict(PatchSetIpConflictCapture& outCapture)
{
    // A pending IP conflict is signalled by a non-zero PatchSetId; ResetLastIpConflict and
    // fresh PatchSetIpConflictCapture{} both zero the struct.
    if (g_lastIpConflict.PatchSetId == 0)
    {
        return false;
    }

    outCapture = g_lastIpConflict;
    return true;
}

void SetIpConflictTestSeamMode(int mode)
{
    g_ipConflictTestSeamMode = mode;
}

bool CheckDuplicatePatchSetAddress(
    const std::vector<PatchSetEntry>& entries, uint32_t address)
{
    for (const auto& e : entries)
    {
        if (e.Address == address)
        {
            return true;
        }
    }
    return false;
}

AgentStatusCode InstallPatchesFromPayload(const unsigned char* data, uint32_t length)
{
    // v11: refuse if native catalog not yet delivered. InstallPatches must never run
    // with a zeroed catalog — hook handler code would read zero RVAs and silently no-op.
    if (!HasNativeCatalog())
    {
        return AgentStatusCode::InvalidCommand;
    }

    if (data == nullptr && length != 0)
    {
        return AgentStatusCode::InvalidCommand;
    }

    PayloadReader reader(data, length);

    // PatchSet section (v11) — replaces the old v10 writes section.
    uint32_t patchSetCount = 0;
    if (!reader.ReadUInt32(patchSetCount) || patchSetCount > 32)
    {
        return AgentStatusCode::InvalidCommand;
    }

    std::vector<InstalledPatchSet> newPatchSets;
    newPatchSets.reserve(patchSetCount);
    for (uint32_t psIndex = 0; psIndex < patchSetCount; ++psIndex)
    {
        InstalledPatchSet ps = {};
        if (!reader.ReadUInt32(ps.Definition.Id))
        {
            return AgentStatusCode::InvalidCommand;
        }

        uint32_t entryCount = 0;
        if (!reader.ReadUInt32(entryCount) || entryCount > 64)
        {
            return AgentStatusCode::InvalidCommand;
        }

        ps.Definition.Entries.reserve(entryCount);
        for (uint32_t eIndex = 0; eIndex < entryCount; ++eIndex)
        {
            PatchSetEntry entry = {};

            if (!reader.ReadUInt32(entry.Address) || entry.Address == 0)
            {
                return AgentStatusCode::InvalidCommand;
            }

            // Check for duplicate address within this patch set
            if (CheckDuplicatePatchSetAddress(ps.Definition.Entries, entry.Address))
            {
                return AgentStatusCode::InvalidCommand;
            }

            uint8_t kindVal = 0;
            if (!reader.ReadUInt8(kindVal))
            {
                return AgentStatusCode::InvalidCommand;
            }
            entry.Kind = static_cast<PatchSetEntryKind>(kindVal);
            if (entry.Kind != PatchSetEntryKind::Data &&
                entry.Kind != PatchSetEntryKind::CodeFlow &&
                entry.Kind != PatchSetEntryKind::DerivedStateReset)
            {
                return AgentStatusCode::InvalidCommand;
            }

            if (!reader.ReadBytes(entry.EnableBytes) ||
                entry.EnableBytes.empty() || entry.EnableBytes.size() > 16)
            {
                return AgentStatusCode::InvalidCommand;
            }

            if (!reader.ReadBytes(entry.DisableBytes) ||
                entry.DisableBytes.empty() || entry.DisableBytes.size() > 16)
            {
                return AgentStatusCode::InvalidCommand;
            }

            // EnableBytes and DisableBytes must be same length for each entry
            if (entry.EnableBytes.size() != entry.DisableBytes.size())
            {
                return AgentStatusCode::InvalidCommand;
            }

            ps.Definition.Entries.push_back(std::move(entry));
        }

        newPatchSets.push_back(std::move(ps));
    }

    uint32_t hookCount = 0;
    if (!reader.ReadUInt32(hookCount) || hookCount > 1024)
    {
        return AgentStatusCode::InvalidCommand;
    }

    std::vector<ParsedHook> hooks;
    hooks.reserve(hookCount);
    for (uint32_t index = 0; index < hookCount; index++)
    {
        ParsedHook hook = {};
        if (!reader.ReadUInt32(hook.Address) ||
            !reader.ReadUInt32(hook.NativeHookId) ||
            !reader.ReadUInt32(hook.PatchLength) ||
            !reader.ReadBytes(hook.OriginalBytes))
        {
            return AgentStatusCode::InvalidCommand;
        }
        hooks.push_back(std::move(hook));
    }

    if (!reader.AtEnd())
    {
        return AgentStatusCode::InvalidCommand;
    }

    // Pre-verify all patch set entries: current bytes must equal DisableBytes
    ResetLastMismatch();
    for (const auto& ps : newPatchSets)
    {
        for (const auto& entry : ps.Definition.Entries)
        {
            if (entry.Kind == PatchSetEntryKind::DerivedStateReset)
            {
                continue;
            }

            std::vector<unsigned char> current(entry.DisableBytes.size());
            if (!ReadProcessLocalMemory(entry.Address, current.data(), current.size()) ||
                current != entry.DisableBytes)
            {
                // Capture mismatch
                PatchMismatchCapture capture;
                capture.HookAddress = entry.Address;
                capture.Expected = entry.DisableBytes;
                capture.Actual = current;
                capture.OriginKind = 1;         // RuntimePatchSet origin
                capture.SubjectId = ps.Definition.Id;
                capture.Sequence = g_nextDiagnosticSequence++;
                const uint32_t dumpStart =
                    entry.Address >= 16 ? entry.Address - 16 : entry.Address;
                const uint32_t dumpBefore = entry.Address - dumpStart;
                const uint32_t dumpLength = dumpBefore + std::max<uint32_t>(
                    static_cast<uint32_t>(entry.DisableBytes.size()), 96u);
                capture.Dump.resize(dumpLength);
                if (!ReadProcessLocalMemory(dumpStart, capture.Dump.data(), dumpLength))
                {
                    capture.Dump.clear();
                }
                g_lastMismatch = std::move(capture);
                return AgentStatusCode::PatchMismatch;
            }
        }
    }

    if (!RestoreInstalledPatches())
    {
        return AgentStatusCode::InternalError;
    }
    ResetLastMismatch();

    // Register patch sets (not enabled — IsEnabled = false)
    g_installedPatchSets = std::move(newPatchSets);

    g_installedPatches.reserve(hooks.size());
    for (const auto& hook : hooks)
    {
        if (!InstallHook(hook))
        {
            const bool hasMismatch = g_lastMismatch.HookAddress != 0;
            if (!RestoreInstalledPatches())
            {
                return AgentStatusCode::InternalError;
            }
            return hasMismatch
                ? AgentStatusCode::PatchMismatch
                : AgentStatusCode::InternalError;
        }
    }

    return AgentStatusCode::Ok;
}

// ── PatchSet helpers ────────────────────────────────────────────────────────────

InstalledPatchSet* FindInstalledPatchSet(uint32_t id)
{
    for (auto& ps : g_installedPatchSets)
    {
        if (ps.Definition.Id == id)
        {
            return &ps;
        }
    }
    return nullptr;
}

const PatchSetEntry* FindPatchSetEntry(uint32_t patchSetId, uint32_t address)
{
    const auto* ps = FindInstalledPatchSet(patchSetId);
    if (ps == nullptr)
    {
        return nullptr;
    }

    for (const auto& entry : ps->Definition.Entries)
    {
        if (entry.Address == address)
        {
            return &entry;
        }
    }
    return nullptr;
}

bool IsHookInstalled(uint32_t nativeHookId)
{
    for (const auto& patch : g_installedPatches)
    {
        if (patch.NativeHookId == nativeHookId)
        {
            return true;
        }
    }
    return false;
}

bool IsPatchSetEnabled(uint32_t patchSetId)
{
    const auto* ps = FindInstalledPatchSet(patchSetId);
    return ps != nullptr && ps->IsEnabled;
}

AgentStatusCode SetRuntimePatchSetFromPayload(const unsigned char* data, uint32_t length)
{
    if (data == nullptr || length != 8)
    {
        return AgentStatusCode::InvalidCommand;
    }

    uint32_t patchSetId = 0;
    uint32_t enableFlag = 0;
    std::memcpy(&patchSetId, data, sizeof(patchSetId));
    std::memcpy(&enableFlag, data + sizeof(patchSetId), sizeof(enableFlag));

    auto* installed = FindInstalledPatchSet(patchSetId);
    if (installed == nullptr)
    {
        return AgentStatusCode::InvalidCommand;
    }

    const bool targetEnabled = (enableFlag != 0);

    // B1 Hook 41 dependency gate: PatchSet 1 (FrameRateUnlock) requires Hook 41
    // Also prevents accidentally enabling PatchSet that isn't gated.
    if (patchSetId == static_cast<uint32_t>(NativeRuntimePatchSetId::FrameRateUnlock) &&
        targetEnabled)
    {
        if (!IsHookInstalled(41))
        {
            return AgentStatusCode::InvalidCommand;
        }
    }

    // Idempotency check
    if (installed->IsEnabled == targetEnabled)
    {
        return AgentStatusCode::Ok;
    }

    // ── Phase A: Partition entries by Kind ──────────────────────────────────────
    struct WritePlan
    {
        uint32_t Address;
        const std::vector<unsigned char>* TargetBytes;
        const std::vector<unsigned char>* ExpectedPreBytes;
        bool SkipExpectedPreVerification;
    };

    auto makePlan = [&](const PatchSetEntry& entry) -> WritePlan {
        WritePlan p;
        p.Address = entry.Address;
        p.SkipExpectedPreVerification =
            entry.Kind == PatchSetEntryKind::DerivedStateReset;
        if (targetEnabled)
        {
            p.TargetBytes = &entry.EnableBytes;
            p.ExpectedPreBytes = &entry.DisableBytes;
        }
        else
        {
            p.TargetBytes = &entry.DisableBytes;
            p.ExpectedPreBytes = &entry.EnableBytes;
        }
        return p;
    };

    std::vector<WritePlan> cfWrites;
    std::vector<WritePlan> dataWrites;
    for (const auto& entry : installed->Definition.Entries)
    {
        auto plan = makePlan(entry);
        if (entry.Kind == PatchSetEntryKind::CodeFlow)
        {
            cfWrites.push_back(std::move(plan));
        }
        else
        {
            dataWrites.push_back(std::move(plan));
        }
    }

    // Build combined ranges for initial coarse thread-safety check
    std::vector<CodeRange> allRanges;
    allRanges.reserve(cfWrites.size() + dataWrites.size());
    for (const auto& p : cfWrites)
    {
        allRanges.push_back({ p.Address, static_cast<uint32_t>(p.TargetBytes->size()) });
    }
    for (const auto& p : dataWrites)
    {
        allRanges.push_back({ p.Address, static_cast<uint32_t>(p.TargetBytes->size()) });
    }

    // Transaction: suspend → pre-verify → CodeFlow-first (IP-guarded) → Data
    bool committed = false;
    for (uint32_t attempt = 0; attempt < 100 && !committed; ++attempt)
    {
        {
            SuspendedThreadSet threads;
            if (!threads.SuspendAllOtherThreads())
            {
                continue;
            }

            // Initial coarse-range check: no thread IP inside any target range
            if (!threads.IsOutside(allRanges))
            {
                continue;
            }

            // ── Phase C: Pre-write verification (all entries, both kinds) ──────
            ResetLastIpConflict();
            bool verifyOk = true;

            auto verifyPlan = [&](const WritePlan& p) {
                std::vector<unsigned char> current(p.ExpectedPreBytes->size());
                const bool readOk = ReadProcessLocalMemory(
                    p.Address, current.data(), current.size());
                if (!readOk ||
                    (!p.SkipExpectedPreVerification && current != *p.ExpectedPreBytes))
                {
                    PatchMismatchCapture capture;
                    capture.HookAddress = p.Address;
                    capture.Expected = *p.ExpectedPreBytes;
                    capture.Actual = current;
                    capture.OriginKind = 1;         // RuntimePatchSet origin
                    capture.SubjectId = installed->Definition.Id;
                    capture.Sequence = g_nextDiagnosticSequence++;
                    const uint32_t dumpStart =
                        p.Address >= 16 ? p.Address - 16 : p.Address;
                    const uint32_t dumpBefore = p.Address - dumpStart;
                    const uint32_t dumpLength = dumpBefore + std::max<uint32_t>(
                        static_cast<uint32_t>(p.ExpectedPreBytes->size()), 96u);
                    capture.Dump.resize(dumpLength);
                    if (!ReadProcessLocalMemory(dumpStart, capture.Dump.data(), dumpLength))
                    {
                        capture.Dump.clear();
                    }
                    g_lastMismatch = std::move(capture);
                    verifyOk = false;
                }
            };

            for (const auto& p : cfWrites)
            {
                verifyPlan(p);
                if (!verifyOk) break;
            }
            if (verifyOk)
            {
                for (const auto& p : dataWrites)
                {
                    verifyPlan(p);
                    if (!verifyOk) break;
                }
            }

            if (!verifyOk)
            {
                // g_lastMismatch already populated; PatchMismatch is terminal
                return AgentStatusCode::PatchMismatch;
            }

            // ── Phase D: CodeFlow entries first, each atomic + IP-guarded ──────
            std::vector<const WritePlan*> writtenCF;
            writtenCF.reserve(cfWrites.size());

            bool cfOk = true;
            for (size_t i = 0; i < cfWrites.size(); ++i)
            {
                const auto& p = cfWrites[i];
                const CodeRange range{ p.Address,
                    static_cast<uint32_t>(p.TargetBytes->size()) };
                const std::vector<CodeRange> singleRange = { range };

                // IP guard: no thread within ±16 bytes of this CodeFlow entry
                if (!threads.IsIpOutsideRanges(singleRange))
                {
                    uint32_t conflictThreadId = 0;
                    uint32_t conflictIp = 0;
                    threads.GetFirstIpConflict(singleRange, conflictThreadId, conflictIp);
                    CapturePatchSetIpConflict(
                        installed->Definition.Id,
                        p.Address,
                        conflictThreadId,
                        conflictIp,
                        /*isRestore=*/false);

                    // Rollback any CodeFlow entries already written before this one
                    for (const auto* w : writtenCF)
                    {
                        WriteProcessLocalMemory(
                            w->Address,
                            w->ExpectedPreBytes->data(),
                            w->ExpectedPreBytes->size());
                    }
                    cfOk = false;
                    break;
                }

                if (WriteProcessLocalMemory(
                        p.Address,
                        p.TargetBytes->data(),
                        p.TargetBytes->size()))
                {
                    writtenCF.push_back(&p);
                }
                else
                {
                    // Rollback already-written CodeFlow entries
                    for (const auto* w : writtenCF)
                    {
                        WriteProcessLocalMemory(
                            w->Address,
                            w->ExpectedPreBytes->data(),
                            w->ExpectedPreBytes->size());
                    }
                    cfOk = false;
                    break;
                }
            }

            if (!cfOk)
            {
                // IP conflict already captured via g_lastIpConflict.
                // Write failure did not modify state (rolled back above).
                // Using InternalError because a new status code would be wire-breaking;
                // the host queries TryGetLastIpConflict for the specific diagnostic.
                break;
            }

            // ── Phase E: Data entries — batched write ──────────────────────────
            // On failure, rollback ALL writes (CodeFlow first, then Data).
            std::vector<const WritePlan*> writtenData;
            writtenData.reserve(dataWrites.size());

            bool dataOk = true;
            for (size_t i = 0; i < dataWrites.size(); ++i)
            {
                const auto& p = dataWrites[i];
                if (WriteProcessLocalMemory(
                        p.Address,
                        p.TargetBytes->data(),
                        p.TargetBytes->size()))
                {
                    writtenData.push_back(&p);
                }
                else
                {
                    // Rollback all written entries
                    for (const auto* w : writtenCF)
                    {
                        WriteProcessLocalMemory(
                            w->Address,
                            w->ExpectedPreBytes->data(),
                            w->ExpectedPreBytes->size());
                    }
                    for (const auto* w : writtenData)
                    {
                        WriteProcessLocalMemory(
                            w->Address,
                            w->ExpectedPreBytes->data(),
                            w->ExpectedPreBytes->size());
                    }
                    dataOk = false;
                    break;
                }
            }

            if (dataOk)
            {
                committed = true;
                installed->IsEnabled = targetEnabled;
            }
        }

        if (!committed)
        {
            Sleep(1);
        }
    }

    return committed ? AgentStatusCode::Ok : AgentStatusCode::InternalError;
}
}
