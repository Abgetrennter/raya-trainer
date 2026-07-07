#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

#include "AgentGameApi.h"
#include "AgentGameThreadDispatcher.h"
#include "AgentFeatureState.h"
#include "AgentMemoryAccess.h"
#include "AgentPatchManager.h"
#include "AgentPipeServer.h"
#include "AgentProtocol.h"
#include "AgentRuntime.h"
#include "AgentScanner.h"
#include "AgentSignatureScanner.h"

namespace RayaTrainer::agent
{
namespace
{
volatile LONG g_stopRequested = 0;

std::wstring BuildPipeName()
{
    return L"\\\\.\\pipe\\RayaTrainer.Agent." + std::to_wstring(GetCurrentProcessId());
}

uint32_t CurrentModuleBase()
{
    return static_cast<uint32_t>(reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)));
}

bool ReadExact(HANDLE pipe, void* buffer, DWORD length)
{
    auto* cursor = static_cast<unsigned char*>(buffer);
    DWORD totalRead = 0;
    while (totalRead < length)
    {
        DWORD read = 0;
        if (!ReadFile(pipe, cursor + totalRead, length - totalRead, &read, nullptr) || read == 0)
        {
            return false;
        }

        totalRead += read;
    }

    return true;
}

bool WriteExact(HANDLE pipe, const void* buffer, DWORD length)
{
    const auto* cursor = static_cast<const unsigned char*>(buffer);
    DWORD totalWritten = 0;
    while (totalWritten < length)
    {
        DWORD written = 0;
        if (!WriteFile(pipe, cursor + totalWritten, length - totalWritten, &written, nullptr) || written == 0)
        {
            return false;
        }

        totalWritten += written;
    }

    return true;
}

bool IsValidHeader(const AgentProtocolHeader& header)
{
    return header.Magic == kAgentMagic &&
        header.Version == kAgentProtocolVersion &&
        header.PayloadLength <= kMaxPayloadLength;
}

AgentStatusCode StatusForCommand(const AgentProtocolHeader& header)
{
    if (!IsValidHeader(header))
    {
        return AgentStatusCode::InvalidCommand;
    }

    const auto command = static_cast<AgentCommand>(header.Command);
    if (command == AgentCommand::Ping || command == AgentCommand::GetStatus)
    {
        return AgentStatusCode::Ok;
    }

    return AgentStatusCode::InvalidCommand;
}

bool ReadPayload(HANDLE pipe, uint32_t payloadLength, std::vector<unsigned char>& payload)
{
    payload.clear();
    if (payloadLength == 0)
    {
        return true;
    }

    payload.resize(payloadLength);
    return ReadExact(pipe, payload.data(), payloadLength);
}

bool WritePingResponse(HANDLE pipe, const AgentProtocolHeader& request, AgentStatusCode statusCode)
{
    AgentPingPayload payload = {};
    payload.StatusCode = static_cast<uint16_t>(statusCode);
    payload.AgentVersion = kAgentProtocolVersion;
    payload.ProcessId = GetCurrentProcessId();
    payload.ModuleBase = CurrentModuleBase();
    payload.NativeRuntimeCapabilities = kNativeRuntimeCapabilities;
    payload.BuildFingerprint = kAgentBuildFingerprint;

    AgentProtocolHeader response = {};
    response.Magic = kAgentMagic;
    response.Version = kAgentProtocolVersion;
    response.Command = request.Command;
    response.SequenceId = request.SequenceId;
    response.PayloadLength = sizeof(payload);

    return WriteExact(pipe, &response, sizeof(response)) &&
        WriteExact(pipe, &payload, sizeof(payload)) &&
        FlushFileBuffers(pipe);
}

bool WriteStatusResponse(HANDLE pipe, const AgentProtocolHeader& request, AgentStatusCode statusCode)
{
    AgentStatusPayload payload = {};
    payload.StatusCode = static_cast<uint16_t>(statusCode);
    payload.AgentVersion = kAgentProtocolVersion;
    payload.ProcessId = GetCurrentProcessId();
    payload.ModuleBase = CurrentModuleBase();
    payload.InstalledHookCount = InstalledPatchCount();
    payload.NativeRuntimeCapabilities = kNativeRuntimeCapabilities;
    payload.GameThreadTick = AgentGameThreadDispatcher::PumpTick();
    payload.BuildFingerprint = kAgentBuildFingerprint;

    AgentProtocolHeader response = {};
    response.Magic = kAgentMagic;
    response.Version = kAgentProtocolVersion;
    response.Command = request.Command;
    response.SequenceId = request.SequenceId;
    response.PayloadLength = sizeof(payload);

    return WriteExact(pipe, &response, sizeof(response)) &&
        WriteExact(pipe, &payload, sizeof(payload)) &&
        FlushFileBuffers(pipe);
}

bool WriteCommandResultResponse(HANDLE pipe, const AgentProtocolHeader& request, AgentStatusCode statusCode)
{
    AgentCommandResultPayload payload = {};
    payload.StatusCode = static_cast<uint16_t>(statusCode);
    payload.AgentVersion = kAgentProtocolVersion;
    payload.InstalledHookCount = InstalledPatchCount();

    AgentProtocolHeader response = {};
    response.Magic = kAgentMagic;
    response.Version = kAgentProtocolVersion;
    response.Command = request.Command;
    response.SequenceId = request.SequenceId;
    response.PayloadLength = sizeof(payload);

    return WriteExact(pipe, &response, sizeof(response)) &&
        WriteExact(pipe, &payload, sizeof(payload)) &&
        FlushFileBuffers(pipe);
}

bool WriteMemoryReadResponse(
    HANDLE pipe,
    const AgentProtocolHeader& request,
    AgentStatusCode statusCode,
    uint32_t address,
    const std::vector<unsigned char>& bytes)
{
    AgentMemoryReadPayloadHeader payload = {};
    payload.StatusCode = static_cast<uint16_t>(statusCode);
    payload.AgentVersion = kAgentProtocolVersion;
    payload.Address = address;
    payload.ByteCount = statusCode == AgentStatusCode::Ok
        ? static_cast<uint32_t>(bytes.size())
        : 0;

    AgentProtocolHeader response = {};
    response.Magic = kAgentMagic;
    response.Version = kAgentProtocolVersion;
    response.Command = request.Command;
    response.SequenceId = request.SequenceId;
    response.PayloadLength = sizeof(payload) + payload.ByteCount;

    if (!WriteExact(pipe, &response, sizeof(response)) ||
        !WriteExact(pipe, &payload, sizeof(payload)))
    {
        return false;
    }

    if (payload.ByteCount > 0 &&
        !WriteExact(pipe, bytes.data(), payload.ByteCount))
    {
        return false;
    }

    return FlushFileBuffers(pipe);
}

bool WriteMismatchDiagnosticsResponse(HANDLE pipe, const AgentProtocolHeader& request)
{
    PatchMismatchCapture capture;
    const bool hasMismatch = TryGetLastMismatch(capture);

    AgentMismatchDiagnosticsPayloadHeader payload = {};
    payload.StatusCode = static_cast<uint16_t>(
        hasMismatch ? AgentStatusCode::Ok : AgentStatusCode::InvalidCommand);
    payload.AgentVersion = kAgentProtocolVersion;
    payload.HookAddress = capture.HookAddress;
    payload.ExpectedLength = static_cast<uint32_t>(capture.Expected.size());
    payload.ActualLength = static_cast<uint32_t>(capture.Actual.size());
    payload.DumpLength = static_cast<uint32_t>(capture.Dump.size());

    AgentProtocolHeader response = {};
    response.Magic = kAgentMagic;
    response.Version = kAgentProtocolVersion;
    response.Command = request.Command;
    response.SequenceId = request.SequenceId;
    response.PayloadLength = sizeof(payload) + payload.ExpectedLength + payload.ActualLength + payload.DumpLength;

    if (!WriteExact(pipe, &response, sizeof(response)) ||
        !WriteExact(pipe, &payload, sizeof(payload)))
    {
        return false;
    }

    if (payload.ExpectedLength > 0 &&
        !WriteExact(pipe, capture.Expected.data(), payload.ExpectedLength))
    {
        return false;
    }

    if (payload.ActualLength > 0 &&
        !WriteExact(pipe, capture.Actual.data(), payload.ActualLength))
    {
        return false;
    }

    if (payload.DumpLength > 0 &&
        !WriteExact(pipe, capture.Dump.data(), payload.DumpLength))
    {
        return false;
    }

    return FlushFileBuffers(pipe);
}

bool WriteSignatureScanResponse(HANDLE pipe, const AgentProtocolHeader& request)
{
    std::vector<ScanResult> results;
    const bool scanRan = RunBuiltInScan(results);

    size_t payloadLength = sizeof(AgentSignatureScanPayloadHeader);
    uint32_t matchedCount = 0;
    bool payloadValid = scanRan;
    if (scanRan)
    {
        for (const auto& result : results)
        {
            const size_t nameLength = result.SymbolicName == nullptr
                ? 0
                : std::strlen(result.SymbolicName);
            if (nameLength == 0 || nameLength > 0xFFFFu)
            {
                payloadValid = false;
                break;
            }

            payloadLength += sizeof(AgentSignatureScanEntryHeader) + nameLength;
            if (payloadLength > kMaxPayloadLength)
            {
                payloadValid = false;
                break;
            }

            if (result.Address != 0)
            {
                ++matchedCount;
            }
        }
    }

    if (!payloadValid)
    {
        results.clear();
        payloadLength = sizeof(AgentSignatureScanPayloadHeader);
        matchedCount = 0;
    }

    AgentSignatureScanPayloadHeader payload = {};
    payload.StatusCode = static_cast<uint16_t>(
        payloadValid ? AgentStatusCode::Ok : AgentStatusCode::InternalError);
    payload.AgentVersion = kAgentProtocolVersion;
    payload.EntryCount = static_cast<uint32_t>(results.size());
    payload.MatchedCount = matchedCount;

    AgentProtocolHeader response = {};
    response.Magic = kAgentMagic;
    response.Version = kAgentProtocolVersion;
    response.Command = request.Command;
    response.SequenceId = request.SequenceId;
    response.PayloadLength = static_cast<uint32_t>(payloadLength);

    if (!WriteExact(pipe, &response, sizeof(response)) ||
        !WriteExact(pipe, &payload, sizeof(payload)))
    {
        return false;
    }

    for (const auto& result : results)
    {
        const auto nameLength = static_cast<uint16_t>(std::strlen(result.SymbolicName));
        AgentSignatureScanEntryHeader entry = {};
        entry.Address = result.Address;
        entry.NameLength = nameLength;

        if (!WriteExact(pipe, &entry, sizeof(entry)) ||
            !WriteExact(pipe, result.SymbolicName, nameLength))
        {
            return false;
        }
    }

    return FlushFileBuffers(pipe);
}

bool WriteGameModeResponse(HANDLE pipe, const AgentProtocolHeader& request)
{
    int32_t gameMode = 0;
    const bool succeeded = TryGetGameMode(gameMode);

    AgentGameModePayload payload = {};
    payload.StatusCode = static_cast<uint16_t>(
        succeeded ? AgentStatusCode::Ok : AgentStatusCode::InternalError);
    payload.AgentVersion = kAgentProtocolVersion;
    payload.GameMode = gameMode;

    AgentProtocolHeader response = {};
    response.Magic = kAgentMagic;
    response.Version = kAgentProtocolVersion;
    response.Command = request.Command;
    response.SequenceId = request.SequenceId;
    response.PayloadLength = sizeof(payload);

    return WriteExact(pipe, &response, sizeof(response)) &&
        WriteExact(pipe, &payload, sizeof(payload)) &&
        FlushFileBuffers(pipe);
}

template <typename TPayload>
bool WriteGameApiResponse(
    HANDLE pipe,
    const AgentProtocolHeader& request,
    const TPayload& payload)
{
    AgentProtocolHeader response = {};
    response.Magic = kAgentMagic;
    response.Version = kAgentProtocolVersion;
    response.Command = request.Command;
    response.SequenceId = request.SequenceId;
    response.PayloadLength = sizeof(payload);

    return WriteExact(pipe, &response, sizeof(response)) &&
        WriteExact(pipe, &payload, sizeof(payload)) &&
        FlushFileBuffers(pipe);
}

void HandleClient(HANDLE pipe)
{
    AgentProtocolHeader header = {};
    if (!ReadExact(pipe, &header, sizeof(header)))
    {
        return;
    }

    if (!IsValidHeader(header))
    {
        WritePingResponse(pipe, header, AgentStatusCode::InvalidCommand);
        return;
    }

    std::vector<unsigned char> payload;
    if (!ReadPayload(pipe, header.PayloadLength, payload))
    {
        return;
    }

    const auto command = static_cast<AgentCommand>(header.Command);
    if (command == AgentCommand::GetStatus)
    {
        WriteStatusResponse(pipe, header, StatusForCommand(header));
        return;
    }

    if (command == AgentCommand::InstallPatches)
    {
        const auto status = InstallPatchesFromPayload(payload.data(), header.PayloadLength);
        WriteCommandResultResponse(pipe, header, status);
        return;
    }

    if (command == AgentCommand::RestorePatches)
    {
        const auto status = RestoreInstalledPatches()
            ? AgentStatusCode::Ok
            : AgentStatusCode::InternalError;
        WriteCommandResultResponse(pipe, header, status);
        return;
    }

    if (command == AgentCommand::SetToggle ||
        command == AgentCommand::WriteResourceValues)
    {
        const auto status = ApplyNativeFeatureStatesFromPayload(payload.data(), header.PayloadLength);
        WriteCommandResultResponse(pipe, header, status);
        return;
    }

    if (command == AgentCommand::TriggerAction)
    {
        const auto status = ApplyMemoryWritesFromPayload(payload.data(), header.PayloadLength);
        WriteCommandResultResponse(pipe, header, status);
        return;
    }

    if (command == AgentCommand::ReadMemory)
    {
        uint32_t address = 0;
        std::vector<unsigned char> bytes;
        const auto status = ReadMemoryFromPayload(payload.data(), header.PayloadLength, address, bytes);
        if (status != AgentStatusCode::Ok)
        {
            bytes.clear();
        }

        WriteMemoryReadResponse(pipe, header, status, address, bytes);
        return;
    }

    if (command == AgentCommand::SetNativeCatalog)
    {
        const auto status = SetNativeCatalogFromPayload(payload.data(), header.PayloadLength);
        WriteCommandResultResponse(pipe, header, status);
        return;
    }

    if (command == AgentCommand::GetMismatchDiagnostics)
    {
        WriteMismatchDiagnosticsResponse(pipe, header);
        return;
    }

    if (command == AgentCommand::ScanSignatures)
    {
        WriteSignatureScanResponse(pipe, header);
        return;
    }

    if (command == AgentCommand::GetGameMode)
    {
        WriteGameModeResponse(pipe, header);
        return;
    }

#include "Generated/AgentPipeServer.Dispatch.generated.inc"

    WritePingResponse(pipe, header, StatusForCommand(header));
}
}

void RequestStop(bool stop)
{
    InterlockedExchange(&g_stopRequested, stop ? 1 : 0);
}

DWORD WINAPI AgentWorkerThread(LPVOID)
{
    if (!InitializeRuntime())
    {
        return 1;
    }

    InitializeNativeCatalog();

    // Run the Zydis decoder self-test once at init so scanner readiness is cached before the
    // first signature request.
    IsScannerReady();

    const auto pipeName = BuildPipeName();
    while (InterlockedCompareExchange(&g_stopRequested, 0, 0) == 0)
    {
        HANDLE pipe = CreateNamedPipeW(
            pipeName.c_str(),
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,
            4096,
            4096,
            250,
            nullptr);
        if (pipe == INVALID_HANDLE_VALUE)
        {
            Sleep(250);
            continue;
        }

        const BOOL connected = ConnectNamedPipe(pipe, nullptr) ?
            TRUE :
            (GetLastError() == ERROR_PIPE_CONNECTED);
        if (connected)
        {
            HandleClient(pipe);
            DisconnectNamedPipe(pipe);
        }

        CloseHandle(pipe);
    }

    ShutdownRuntime();
    return 0;
}
}
