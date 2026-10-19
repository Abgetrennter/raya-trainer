using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

// Private runtime-asset-loading surface for AgentCommand.LoadBundledRuntimeAssets (cmd 69). The
// command is hand-written (not generated from apis.json, which only supports fixed-width fields)
// because it carries a variable-length manifest path. Excluded from the public projection.
public sealed partial class AgentNamedPipeClient
{
    public Task<LoadBundledRuntimeAssetsPayload> LoadBundledRuntimeAssetsAsync(
        int processId,
        string absoluteManifestPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.LoadBundledRuntimeAssets,
            EncodeLoadBundledRuntimeAssetsRequest(absoluteManifestPath),
            timeout,
            LoadBundledRuntimeAssetsPayload.ReadFrom,
            cancellationToken);
    }

    // Request: u16 version (1) + u16 byteLength + UTF-8 path bytes. Matches the native handler in
    // AgentPipeServer.cpp HandleLoadBundledRuntimeAssets.
    private static byte[] EncodeLoadBundledRuntimeAssetsRequest(string absoluteManifestPath)
    {
        var pathBytes = System.Text.Encoding.UTF8.GetBytes(absoluteManifestPath);
        if (pathBytes.Length == 0 || pathBytes.Length > 1024)
        {
            throw new ArgumentException(
                "Manifest path must be 1..1024 UTF-8 bytes.", nameof(absoluteManifestPath));
        }

        var payload = new byte[4 + pathBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), (ushort)pathBytes.Length);
        pathBytes.CopyTo(payload, 4);
        return payload;
    }
}

// Response: u16 status + u8 state + u32 resolved + u32 expected. Mirrors the native reply.
public readonly record struct LoadBundledRuntimeAssetsPayload(
    AgentStatusCode StatusCode,
    byte State,
    uint ResolvedTemplateCount,
    uint ExpectedTemplateCount)
{
    public const int Size = 11;

    public static LoadBundledRuntimeAssetsPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != Size)
        {
            throw new InvalidDataException(
                $"LoadBundledRuntimeAssets payload must be {Size} bytes, actual {payload.Length}.");
        }

        var span = payload.Span;
        return new LoadBundledRuntimeAssetsPayload(
            (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span[..2]),
            span[2],
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(3, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(7, 4)));
    }
}
