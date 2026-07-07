namespace RayaTrainer.Core.Agent;

/// <summary>
/// Response payload for the <see cref="AgentCommand.GetMismatchDiagnostics"/> command.
/// Mirrors the native <c>AgentMismatchDiagnosticsPayloadHeader</c> layout followed by the
/// trailing [expected][actual][dump] byte regions, so the host can reconstruct the same
/// per-hook diagnostic the external-memory backend produces via
/// <c>PatchMismatchReportWriter</c>.
/// </summary>
public sealed record AgentMismatchDiagnosticsPayload(
    AgentStatusCode StatusCode,
    ushort AgentVersion,
    uint HookAddress,
    byte[] ExpectedBytes,
    byte[] ActualBytes,
    byte[] DumpBytes)
{
    private const int FixedSize = 20;

    /// <summary>
    /// Address of the first byte of the dump region. The dump is laid out as
    /// [kDumpBefore bytes][hook bytes][remaining bytes], matching the external backend's
    /// DumpBytesBefore=16 window, so this equals <see cref="HookAddress"/> - 16 when the
    /// hook address permits it.
    /// </summary>
    public uint DumpStartAddress
    {
        get
        {
            const uint dumpBefore = 16;
            return HookAddress >= dumpBefore ? HookAddress - dumpBefore : HookAddress;
        }
    }

    /// <summary>
    /// True when the DLL reported a captured mismatch (status Ok). False when no mismatch
    /// is pending (e.g. install has not run yet, or succeeded).
    /// </summary>
    public bool HasMismatch => StatusCode == AgentStatusCode.Ok;

    public static AgentMismatchDiagnosticsPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < FixedSize)
        {
            throw new InvalidDataException(
                $"Agent mismatch diagnostics payload must be at least {FixedSize} bytes, actual {payload.Length}.");
        }

        var span = payload.Span;
        var statusCode = (AgentStatusCode)BitConverter.ToUInt16(span[..2]);
        var agentVersion = BitConverter.ToUInt16(span.Slice(2, 2));
        var hookAddress = BitConverter.ToUInt32(span.Slice(4, 4));
        var expectedLength = BitConverter.ToUInt32(span.Slice(8, 4));
        var actualLength = BitConverter.ToUInt32(span.Slice(12, 4));
        var dumpLength = BitConverter.ToUInt32(span.Slice(16, 4));

        var remaining = payload.Length - FixedSize;
        if (expectedLength + actualLength + dumpLength > remaining)
        {
            throw new InvalidDataException(
                "Agent mismatch diagnostics payload length is inconsistent with the region sizes.");
        }

        var offset = FixedSize;
        var expected = span.Slice(offset, checked((int)expectedLength)).ToArray();
        offset += checked((int)expectedLength);
        var actual = span.Slice(offset, checked((int)actualLength)).ToArray();
        offset += checked((int)actualLength);
        var dump = span.Slice(offset, checked((int)dumpLength)).ToArray();

        return new AgentMismatchDiagnosticsPayload(
            statusCode,
            agentVersion,
            hookAddress,
            expected,
            actual,
            dump);
    }
}
