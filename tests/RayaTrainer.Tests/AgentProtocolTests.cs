using System.Buffers.Binary;
using System.IO.Pipes;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentProtocolTests
{
    [Fact]
    public void HeaderRoundTripsWithLittleEndianFields()
    {
        var header = new AgentProtocolHeader(
            AgentProtocol.Magic,
            AgentProtocol.Version,
            AgentCommand.Ping,
            SequenceId: 42,
            PayloadLength: 16);

        Span<byte> buffer = stackalloc byte[AgentProtocol.HeaderSize];
        header.WriteTo(buffer);

        Assert.Equal(
            [
                0x52, 0x41, 0x59, 0x41,
                (byte)AgentProtocol.Version, 0x00,
                0x01, 0x00,
                0x2A, 0x00, 0x00, 0x00,
                0x10, 0x00, 0x00, 0x00
            ],
            buffer.ToArray());

        var parsed = AgentProtocolHeader.ReadFrom(buffer);

        Assert.Equal(header, parsed);
    }

    [Fact]
    public void ValidateRejectsBadMagic()
    {
        var header = new AgentProtocolHeader(
            Magic: 0x12345678,
            AgentProtocol.Version,
            AgentCommand.Ping,
            SequenceId: 1,
            PayloadLength: 0);

        var ex = Assert.Throws<InvalidDataException>(() => AgentProtocol.Validate(header));

        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRejectsVersionMismatch()
    {
        var header = new AgentProtocolHeader(
            AgentProtocol.Magic,
            Version: (ushort)(AgentProtocol.Version + 1),
            AgentCommand.Ping,
            SequenceId: 1,
            PayloadLength: 0);

        var ex = Assert.Throws<InvalidDataException>(() => AgentProtocol.Validate(header));

        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetNativeCatalogCommandRoundTripsThroughPipe()
    {
        // Mirrors InstallPatchesAsyncSendsPayloadAndReadsCommandResult: host sends a
        // SetNativeCatalog payload, a fake server reads it and replies with a command result.
        var rvas = Ra3VersionProfileRegistry.Ra3113.BuildNativeAgentCatalogRvas();
        var expectedPayload = NativeAgentCatalog.Encode(rvas);
        var processId = Environment.ProcessId;
        var pipeName = AgentPipeName.ForProcessId(processId);
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync().ConfigureAwait(false);

            var headerBuffer = new byte[AgentProtocol.HeaderSize];
            await server.ReadExactlyAsync(headerBuffer).ConfigureAwait(false);
            var header = AgentProtocolHeader.ReadFrom(headerBuffer);
            Assert.Equal(AgentCommand.SetNativeCatalog, header.Command);

            var received = new byte[header.PayloadLength];
            await server.ReadExactlyAsync(received).ConfigureAwait(false);
            Assert.Equal(expectedPayload, received);

            var resultBytes = AgentCommandResultPayload.Encode(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                installedHookCount: 0);
            var responseHeader = new AgentProtocolHeader(
                AgentProtocol.Magic,
                AgentProtocol.Version,
                AgentCommand.SetNativeCatalog,
                header.SequenceId,
                (uint)AgentCommandResultPayload.Size);
            var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            responseHeader.WriteTo(responseHeaderBuffer);
            await server.WriteAsync(responseHeaderBuffer).ConfigureAwait(false);
            await server.WriteAsync(resultBytes).ConfigureAwait(false);
            await server.FlushAsync().ConfigureAwait(false);
        });

        var client = new AgentNamedPipeClient();
        var response = await client.SetNativeCatalogAsync(processId, rvas, TimeSpan.FromSeconds(5));

        await serverTask;
        Assert.Equal(AgentStatusCode.Ok, response.StatusCode);
    }

    [Fact]
    public void SignatureScanPayloadRoundTripsResolvedAndUnresolvedEntries()
    {
        var encoded = AgentSignatureScanPayload.Encode(
            AgentStatusCode.Ok,
            AgentProtocol.Version,
            new Dictionary<string, uint>
            {
                ["_BackPlayerMoney"] = 0xA64E9E,
                ["Rva_8D8CE4"] = 0
            });

        var payload = AgentSignatureScanPayload.ReadFrom(encoded);

        Assert.Equal(2u, payload.EntryCount);
        Assert.Equal(1u, payload.MatchedCount);
        Assert.Equal(0xA64E9Eu, payload.Addresses["_BackPlayerMoney"]);
        Assert.Equal(0u, payload.Addresses["Rva_8D8CE4"]);
        Assert.False(payload.IsComplete);
    }

    [Fact]
    public async Task ScanSignaturesCommandRoundTripsThroughPipe()
    {
        var processId = Environment.ProcessId;
        var pipeName = AgentPipeName.ForProcessId(processId);
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();

            var requestHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            await server.ReadExactlyAsync(requestHeaderBuffer);
            var requestHeader = AgentProtocolHeader.ReadFrom(requestHeaderBuffer);
            Assert.Equal(AgentCommand.ScanSignatures, requestHeader.Command);
            Assert.Equal(0u, requestHeader.PayloadLength);

            var responsePayload = AgentSignatureScanPayload.Encode(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                new Dictionary<string, uint>
                {
                    ["_BackPlayerMoney"] = 0xA64E9E,
                    ["Rva_8D8CE4"] = 0xCDDE84
                });
            var responseHeader = new AgentProtocolHeader(
                AgentProtocol.Magic,
                AgentProtocol.Version,
                AgentCommand.ScanSignatures,
                requestHeader.SequenceId,
                (uint)responsePayload.Length);
            var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            responseHeader.WriteTo(responseHeaderBuffer);

            await server.WriteAsync(responseHeaderBuffer);
            await server.WriteAsync(responsePayload);
            await server.FlushAsync();
        });

        var client = new AgentNamedPipeClient();
        var payload = await client.ScanSignaturesAsync(processId, TimeSpan.FromSeconds(2));

        Assert.True(payload.IsComplete);
        Assert.Equal(0xA64E9Eu, payload.Addresses["_BackPlayerMoney"]);
        Assert.Equal(0xCDDE84u, payload.Addresses["Rva_8D8CE4"]);
        await serverTask;
    }

    [Fact]
    public async Task GetGameModeCommandRoundTripsThroughPipe()
    {
        var processId = Environment.ProcessId;
        var pipeName = AgentPipeName.ForProcessId(processId);
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();

            var requestHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            await server.ReadExactlyAsync(requestHeaderBuffer);
            var requestHeader = AgentProtocolHeader.ReadFrom(requestHeaderBuffer);
            Assert.Equal(AgentCommand.GetGameMode, requestHeader.Command);
            Assert.Equal(0u, requestHeader.PayloadLength);

            var responsePayload = AgentGameModePayload.Encode(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                gameMode: 8);
            var responseHeader = new AgentProtocolHeader(
                AgentProtocol.Magic,
                AgentProtocol.Version,
                AgentCommand.GetGameMode,
                requestHeader.SequenceId,
                (uint)responsePayload.Length);
            var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            responseHeader.WriteTo(responseHeaderBuffer);

            await server.WriteAsync(responseHeaderBuffer);
            await server.WriteAsync(responsePayload);
            await server.FlushAsync();
        });

        var client = new AgentNamedPipeClient();
        var payload = await client.GetGameModeAsync(processId, TimeSpan.FromSeconds(2));

        Assert.Equal(AgentStatusCode.Ok, payload.StatusCode);
        Assert.Equal(8, payload.GameMode);
        await serverTask;
    }

    [Fact]
    public async Task GetStatusAsyncReturnsRuntimeStatusFromAgentPipe()
    {
        var processId = Environment.ProcessId;
        var pipeName = AgentPipeName.ForProcessId(processId);
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();

            var requestHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            await server.ReadExactlyAsync(requestHeaderBuffer);
            var requestHeader = AgentProtocolHeader.ReadFrom(requestHeaderBuffer);

            Assert.Equal(AgentCommand.GetStatus, requestHeader.Command);
            Assert.Equal(0u, requestHeader.PayloadLength);

            var responsePayload = AgentStatusPayload.Encode(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                processId,
                moduleBase: 0x400000,
                installedHookCount: 0,
                nativeRuntimeCapabilities: 7,
                gameThreadTick: 12);
            var responseHeader = new AgentProtocolHeader(
                AgentProtocol.Magic,
                AgentProtocol.Version,
                AgentCommand.GetStatus,
                requestHeader.SequenceId,
                (uint)responsePayload.Length);
            var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            responseHeader.WriteTo(responseHeaderBuffer);

            await server.WriteAsync(responseHeaderBuffer);
            await server.WriteAsync(responsePayload);
            await server.FlushAsync();
        });

        var client = new AgentNamedPipeClient();
        var result = await client.GetStatusAsync(processId, TimeSpan.FromSeconds(2));

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.Equal(AgentProtocol.Version, result.AgentVersion);
        Assert.Equal(processId, result.ProcessId);
        Assert.Equal(0x400000u, result.ModuleBase);
        Assert.Equal(7u, result.NativeRuntimeCapabilities);
        Assert.Equal(12u, result.GameThreadTick);
        await serverTask;
    }

    [Fact]
    public async Task ClientSerializesConcurrentCommandsAcrossSharedPipe()
    {
        var processId = Environment.ProcessId;
        var pipeName = AgentPipeName.ForProcessId(processId);
        using var firstServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 2,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var firstAccepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstServerTask = Task.Run(async () =>
        {
            await firstServer.WaitForConnectionAsync();
            firstAccepted.SetResult(true);
            await WriteStatusResponseAsync(firstServer, processId, releaseFirst.Task);
        });

        var client = new AgentNamedPipeClient();
        var firstCall = client.GetStatusAsync(processId, TimeSpan.FromSeconds(3));
        await firstAccepted.Task;

        using var secondServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 2,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var secondServerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAccepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondServerTask = Task.Run(async () =>
        {
            secondServerReady.SetResult(true);
            await secondServer.WaitForConnectionAsync();
            secondAccepted.SetResult(true);
            await WriteStatusResponseAsync(secondServer, processId);
        });
        await secondServerReady.Task;

        var secondCall = client.GetStatusAsync(processId, TimeSpan.FromSeconds(3));
        await Task.WhenAny(secondAccepted.Task, Task.Delay(200));
        Assert.False(secondAccepted.Task.IsCompleted);

        releaseFirst.SetResult(true);
        await Task.WhenAll(firstCall, secondCall, firstServerTask, secondServerTask);
        Assert.True(secondAccepted.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task InstallPatchesAsyncSendsPayloadAndReadsCommandResult()
    {
        var processId = Environment.ProcessId;
        var pipeName = AgentPipeName.ForProcessId(processId);
        var request = new AgentInstallPatchesRequest(
            [new AgentMemoryWrite(0x700200, [0xCC])],
            [new AgentPatchHook(0x401000, 0x700200, 6, [0x8B, 0x50, 0x28, 0x8B, 0x42, 0x20])]);
        var expectedPayload = request.Encode();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();

            var requestHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            await server.ReadExactlyAsync(requestHeaderBuffer);
            var requestHeader = AgentProtocolHeader.ReadFrom(requestHeaderBuffer);

            Assert.Equal(AgentCommand.InstallPatches, requestHeader.Command);
            Assert.Equal((uint)expectedPayload.Length, requestHeader.PayloadLength);
            var payload = new byte[requestHeader.PayloadLength];
            await server.ReadExactlyAsync(payload);
            Assert.Equal(expectedPayload, payload);

            var responsePayload = AgentCommandResultPayload.Encode(AgentStatusCode.Ok, AgentProtocol.Version, installedHookCount: 1);
            var responseHeader = new AgentProtocolHeader(
                AgentProtocol.Magic,
                AgentProtocol.Version,
                AgentCommand.InstallPatches,
                requestHeader.SequenceId,
                (uint)responsePayload.Length);
            var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            responseHeader.WriteTo(responseHeaderBuffer);

            await server.WriteAsync(responseHeaderBuffer);
            await server.WriteAsync(responsePayload);
            await server.FlushAsync();
        });

        var client = new AgentNamedPipeClient();
        var result = await client.InstallPatchesAsync(processId, request, TimeSpan.FromSeconds(2));

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.Equal(1u, result.InstalledHookCount);
        await serverTask;
    }

    [Fact]
    public void MemoryWriteRequestEncodesAddressModesAndBytes()
    {
        var request = new AgentMemoryWriteRequest(
        [
            new AgentMemoryWriteOperation(0x70010F, AgentMemoryAddressMode.Direct, [0x01]),
            new AgentMemoryWriteOperation(0x701401, AgentMemoryAddressMode.DereferenceUInt32, [0xEB, 0x0C])
        ]);

        var payload = request.Encode();

        Assert.Equal(
            [
                0x02, 0x00, 0x00, 0x00,
                0x0F, 0x01, 0x70, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00,
                0x01,
                0x01, 0x14, 0x70, 0x00,
                0x01, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0xEB, 0x0C
            ],
            payload);
    }

    [Fact]
    public async Task ReadMemoryAsyncSendsRequestAndReadsDynamicPayload()
    {
        var processId = Environment.ProcessId;
        var pipeName = AgentPipeName.ForProcessId(processId);
        var request = new AgentMemoryReadRequest(0xCE9838, 4);
        var expectedPayload = request.Encode();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();

            var requestHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            await server.ReadExactlyAsync(requestHeaderBuffer);
            var requestHeader = AgentProtocolHeader.ReadFrom(requestHeaderBuffer);

            Assert.Equal(AgentCommand.ReadMemory, requestHeader.Command);
            Assert.Equal((uint)expectedPayload.Length, requestHeader.PayloadLength);
            var payload = new byte[requestHeader.PayloadLength];
            await server.ReadExactlyAsync(payload);
            Assert.Equal(expectedPayload, payload);

            var responsePayload = AgentMemoryReadPayload.Encode(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                0xCE9838,
                [0xA0, 0xA5, 0x86, 0x65]);
            var responseHeader = new AgentProtocolHeader(
                AgentProtocol.Magic,
                AgentProtocol.Version,
                AgentCommand.ReadMemory,
                requestHeader.SequenceId,
                (uint)responsePayload.Length);
            var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            responseHeader.WriteTo(responseHeaderBuffer);

            await server.WriteAsync(responseHeaderBuffer);
            await server.WriteAsync(responsePayload);
            await server.FlushAsync();
        });

        var client = new AgentNamedPipeClient();
        var result = await client.ReadMemoryAsync(processId, request, TimeSpan.FromSeconds(2));

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.Equal(0xCE9838u, result.Address);
        Assert.Equal([0xA0, 0xA5, 0x86, 0x65], result.Bytes);
        await serverTask;
    }

    [Fact]
    public async Task SmokeGetThingClassAsyncSendsRequestAndReadsGameApiPayload()
    {
        var processId = Environment.ProcessId;
        var pipeName = AgentPipeName.ForProcessId(processId);
        var request = new AgentGameApiGetThingClassRequest(
            UnitTypeId: 0x6586A5A0,
            TimeoutMilliseconds: 250,
            EnableDirectGameApi: true);
        var expectedPayload = request.Encode();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();

            var requestHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            await server.ReadExactlyAsync(requestHeaderBuffer);
            var requestHeader = AgentProtocolHeader.ReadFrom(requestHeaderBuffer);

            Assert.Equal(AgentCommand.SmokeGetThingClass, requestHeader.Command);
            Assert.Equal((uint)expectedPayload.Length, requestHeader.PayloadLength);
            var payload = new byte[requestHeader.PayloadLength];
            await server.ReadExactlyAsync(payload);
            Assert.Equal(
                [
                    0xA0, 0xA5, 0x86, 0x65,
                    0xFA, 0x00, 0x00, 0x00,
                    0x01, 0x00, 0x00, 0x00
                ],
                payload);

            var responsePayload = AgentGameApiGetThingClassPayload.Encode(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                unitTypeId: 0x6586A5A0,
                thingClassAddress: 0x12345000,
                GameApiDispatchStatus.Completed,
                requestId: 7,
                gameThreadTickBefore: 10,
                gameThreadTickAfter: 11);
            var responseHeader = new AgentProtocolHeader(
                AgentProtocol.Magic,
                AgentProtocol.Version,
                AgentCommand.SmokeGetThingClass,
                requestHeader.SequenceId,
                (uint)responsePayload.Length);
            var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            responseHeader.WriteTo(responseHeaderBuffer);

            await server.WriteAsync(responseHeaderBuffer);
            await server.WriteAsync(responsePayload);
            await server.FlushAsync();
        });

        var client = new AgentNamedPipeClient();
        var result = await client.SmokeGetThingClassAsync(processId, request, TimeSpan.FromSeconds(2));

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.Equal(0x6586A5A0u, result.UnitTypeId);
        Assert.Equal(0x12345000u, result.ThingClassAddress);
        Assert.Equal(GameApiDispatchStatus.Completed, result.DispatchStatus);
        Assert.Equal(7u, result.RequestId);
        Assert.Equal(10u, result.GameThreadTickBefore);
        Assert.Equal(11u, result.GameThreadTickAfter);
        await serverTask;
    }

    [Fact]
    public async Task ReadSelectedUnitSnapshotViaGameApiAsyncSendsRequestAndReadsSnapshotPayload()
    {
        var processId = Environment.ProcessId;
        var pipeName = AgentPipeName.ForProcessId(processId);
        var request = new AgentGameApiReadSelectedUnitCodeRequest(
            TimeoutMilliseconds: 250,
            EnableDirectGameApi: true);
        var expectedPayload = request.Encode();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();

            var requestHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            await server.ReadExactlyAsync(requestHeaderBuffer);
            var requestHeader = AgentProtocolHeader.ReadFrom(requestHeaderBuffer);

            Assert.Equal(AgentCommand.ReadSelectedUnitCode, requestHeader.Command);
            Assert.Equal((uint)expectedPayload.Length, requestHeader.PayloadLength);
            var payload = new byte[requestHeader.PayloadLength];
            await server.ReadExactlyAsync(payload);
            Assert.Equal(
                [
                    0xFA, 0x00, 0x00, 0x00,
                    0x01, 0x00, 0x00, 0x00
                ],
                payload);

            var responsePayload = AgentGameApiSelectedUnitSnapshotPayload.Encode(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                unitTypeId: 0x356FA3A9,
                thingClassAddress: 0x0D55B180,
                GameApiDispatchStatus.Completed,
                requestId: 9,
                gameThreadTickBefore: 20,
                gameThreadTickAfter: 21);
            var responseHeader = new AgentProtocolHeader(
                AgentProtocol.Magic,
                AgentProtocol.Version,
                AgentCommand.ReadSelectedUnitCode,
                requestHeader.SequenceId,
                (uint)responsePayload.Length);
            var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
            responseHeader.WriteTo(responseHeaderBuffer);

            await server.WriteAsync(responseHeaderBuffer);
            await server.WriteAsync(responsePayload);
            await server.FlushAsync();
        });

        var client = new AgentNamedPipeClient();
        var result = await client.ReadSelectedUnitSnapshotViaGameApiAsync(processId, request, TimeSpan.FromSeconds(2));

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.Equal(0x356FA3A9u, result.UnitTypeId);
        Assert.Equal(0x0D55B180u, result.ThingClassAddress);
        Assert.Equal(GameApiDispatchStatus.Completed, result.DispatchStatus);
        Assert.Equal(9u, result.RequestId);
        Assert.Equal(20u, result.GameThreadTickBefore);
        Assert.Equal(21u, result.GameThreadTickAfter);
        await serverTask;
    }

    [Fact]
    public void SetSelectedStatusBitPayloadAcceptsLegacyTwelveByteAgentResponse()
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, sizeof(ushort)), (ushort)AgentStatusCode.Ok);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, sizeof(ushort)), AgentProtocol.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, sizeof(uint)), (uint)GameApiDispatchStatus.Completed);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, sizeof(uint)), 42);

        var result = AgentGameApiSetSelectedStatusBitPayload.ReadFrom(payload);

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.Equal(AgentProtocol.Version, result.AgentVersion);
        Assert.Equal(GameApiDispatchStatus.Completed, result.DispatchStatus);
        Assert.Equal(42u, result.RequestId);
        Assert.Equal(0u, result.GameThreadTickBefore);
        Assert.Equal(0u, result.GameThreadTickAfter);
    }

    private static async Task WriteStatusResponseAsync(
        NamedPipeServerStream server,
        int processId,
        Task? responseGate = null)
    {
        var requestHeaderBuffer = new byte[AgentProtocol.HeaderSize];
        await server.ReadExactlyAsync(requestHeaderBuffer);
        var requestHeader = AgentProtocolHeader.ReadFrom(requestHeaderBuffer);
        Assert.Equal(AgentCommand.GetStatus, requestHeader.Command);

        if (responseGate is not null)
        {
            await responseGate;
        }

        var responsePayload = AgentStatusPayload.Encode(
            AgentStatusCode.Ok,
            AgentProtocol.Version,
            processId,
            moduleBase: 0x400000,
            installedHookCount: 0);
        var responseHeader = new AgentProtocolHeader(
            AgentProtocol.Magic,
            AgentProtocol.Version,
            AgentCommand.GetStatus,
            requestHeader.SequenceId,
            (uint)responsePayload.Length);
        var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
        responseHeader.WriteTo(responseHeaderBuffer);

        await server.WriteAsync(responseHeaderBuffer);
        await server.WriteAsync(responsePayload);
        await server.FlushAsync();
    }
}
