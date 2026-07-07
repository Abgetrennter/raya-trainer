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
#include "AgentGameThreadDispatcher.h"
#include "AgentNativeHooks.h"
#include "AgentSignatureScanner.h"

namespace RayaTrainer::agent
{
namespace
{
struct InstalledPatch
{
    uint32_t Address;
    std::vector<unsigned char> OriginalBytes;
    std::vector<unsigned char> PatchBytes;
    void* Trampoline;
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

                threads_.push_back({ thread, context.Eip });
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

private:
    struct SuspendedThread
    {
        HANDLE Handle;
        uint32_t InstructionPointer;
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
    code.insert(code.end(), { 0x89, 0x44, 0x24, 0x1C, 0x61, 0x9D, 0xFF, 0xE0 });

    const auto returnOffset = code.size();
    code.insert(code.end(), { 0x61, 0x9D, 0xC3 });

    const auto return4Offset = code.size();
    code.insert(code.end(), { 0x61, 0x9D, 0xC2, 0x04, 0x00 });

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
        trampoline });
    return true;
}
}

bool RestoreInstalledPatches()
{
    if (g_installedPatches.empty())
    {
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

    bool restored = false;
    for (uint32_t attempt = 0; attempt < 100 && !restored; ++attempt)
    {
        {
            SuspendedThreadSet threads;
            if (!threads.SuspendAllOtherThreads())
            {
                // Retry transient thread-snapshot races.
            }
            else if (threads.IsOutside(guardedRanges))
            {
                restored = true;
                for (auto index = g_installedPatches.rbegin();
                     index != g_installedPatches.rend();
                     ++index)
                {
                    if (!WriteProcessLocalMemory(
                            index->Address,
                            index->OriginalBytes.data(),
                            index->OriginalBytes.size()))
                    {
                        restored = false;
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

        if (!restored)
        {
            Sleep(1);
        }
    }

    if (!restored)
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

AgentStatusCode InstallPatchesFromPayload(const unsigned char* data, uint32_t length)
{
    if (data == nullptr && length != 0)
    {
        return AgentStatusCode::InvalidCommand;
    }

    PayloadReader reader(data, length);
    uint32_t writeCount = 0;
    if (!reader.ReadUInt32(writeCount) || writeCount > 1024)
    {
        return AgentStatusCode::InvalidCommand;
    }

    std::vector<ParsedWrite> writes;
    writes.reserve(writeCount);
    for (uint32_t index = 0; index < writeCount; index++)
    {
        ParsedWrite write = {};
        if (!reader.ReadUInt32(write.Address) ||
            !reader.ReadBytes(write.Bytes) ||
            write.Bytes.empty())
        {
            return AgentStatusCode::InvalidCommand;
        }
        writes.push_back(std::move(write));
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

    if (!RestoreInstalledPatches())
    {
        return AgentStatusCode::InternalError;
    }
    ResetLastMismatch();

    if (!writes.empty())
    {
        std::vector<CodeRange> writeRanges;
        writeRanges.reserve(writes.size());
        for (const auto& write : writes)
        {
            writeRanges.push_back(
                { write.Address, static_cast<uint32_t>(write.Bytes.size()) });
        }

        bool written = false;
        for (uint32_t attempt = 0; attempt < 100 && !written; ++attempt)
        {
            {
                SuspendedThreadSet threads;
                if (!threads.SuspendAllOtherThreads())
                {
                    // Retry transient thread-snapshot races.
                }
                else if (threads.IsOutside(writeRanges))
                {
                    written = true;
                    for (const auto& write : writes)
                    {
                        if (!WriteProcessLocalMemory(
                                write.Address,
                                write.Bytes.data(),
                                write.Bytes.size()))
                        {
                            written = false;
                            break;
                        }
                    }
                }
            }

            if (!written)
            {
                Sleep(1);
            }
        }

        if (!written)
        {
            return AgentStatusCode::InternalError;
        }
    }

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
}
