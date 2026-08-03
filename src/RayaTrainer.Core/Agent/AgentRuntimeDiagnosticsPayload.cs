using System.Buffers.Binary;
using System.Collections.Immutable;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Decoded <see cref="AgentCommand.GetRuntimeDiagnostics"/> response (plan §5). Human-readable
/// diagnostic lines the Agent records while running its self-resolution pipeline (identity, layout,
/// capability readiness, blocked reason, core-hook proof). Wire layout: u16 status, u32 count, then
/// one u16-length-prefixed UTF-8 line each. Mirrors the native <c>EncodeRuntimeDiagnostics</c>.
/// </summary>
public readonly record struct AgentRuntimeDiagnosticsPayload(
    AgentStatusCode StatusCode,
    ImmutableArray<string> Lines)
{
    public const int MinimumSize = 6;

    public static AgentRuntimeDiagnosticsPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        if (span.Length < MinimumSize)
        {
            throw new InvalidDataException(
                $"Agent runtime diagnostics payload must be at least {MinimumSize} bytes, actual {span.Length}.");
        }

        var statusCode = (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0, 2));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(2, 4));

        int offset = 6;
        var lines = ImmutableArray.CreateBuilder<string>((int)count);
        for (uint i = 0; i < count; i++)
        {
            lines.Add(AgentRuntimeStatusPayload.ReadString(span, ref offset));
        }

        return new AgentRuntimeDiagnosticsPayload(statusCode, lines.ToImmutable());
    }
}
