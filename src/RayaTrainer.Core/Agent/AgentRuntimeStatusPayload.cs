using System.Buffers.Binary;
using System.Text;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// The Agent's frozen runtime state (plan §5 / §6.4), returned by
/// <see cref="AgentCommand.GetRuntimeStatus"/>. Mirrors the native
/// <c>RayaTrainer::agent::runtime::RuntimeState</c>. An unconfirmed module leaves the runtime
/// <see cref="Failed"/> with a zero granted mask (fail-closed).
/// </summary>
public enum AgentRuntimeState : ushort
{
    Uninitialized = 0,
    Failed = 1,
    Blocked = 2,
    Degraded = 3,
    Ready = 4,
}

/// <summary>
/// Decoded <see cref="AgentCommand.GetRuntimeStatus"/> response. The Agent owns runtime resolution;
/// this payload is a read-only projection the host consumes on attach/reconnect (P6) — it never
/// carries addresses. Wire layout: a fixed little-endian prefix followed by three u16-length-prefixed
/// UTF-8 strings (game family id, layout family id, blocked reason). Mirrors the native
/// <c>EncodeRuntimeStatus</c> codec.
/// </summary>
public readonly record struct AgentRuntimeStatusPayload(
    AgentStatusCode StatusCode,
    AgentRuntimeState State,
    ushort ProtocolMajor,
    ushort ProtocolMinor,
    uint ResolutionGeneration,
    uint SupportedCapabilityCount,
    uint ReadyCapabilityCount,
    uint UnavailableCapabilityCount,
    uint ResolvedCoreHookCount,
    uint PlannedCoreHookCount,
    uint GrantedCapabilityMask,
    uint GameThreadTick,
    ushort OverlayLifecycle,
    bool RuntimeBlocked,
    ulong BuildId,
    string GameFamilyId,
    string LayoutFamilyId,
    string BlockedReason)
{
    /// <summary>Minimum size: the 51-byte fixed prefix plus three empty (2-byte) string headers.</summary>
    public const int MinimumSize = 57;

    public static AgentRuntimeStatusPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        if (span.Length < MinimumSize)
        {
            throw new InvalidDataException(
                $"Agent runtime status payload must be at least {MinimumSize} bytes, actual {span.Length}.");
        }

        var statusCode = (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0, 2));
        var state = (AgentRuntimeState)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2));
        ushort protocolMajor = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4, 2));
        ushort protocolMinor = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6, 2));
        uint resolutionGeneration = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4));
        uint supported = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4));
        uint ready = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4));
        uint unavailable = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20, 4));
        uint resolvedHooks = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(24, 4));
        uint plannedHooks = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(28, 4));
        uint grantedMask = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(32, 4));
        uint gameThreadTick = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(36, 4));
        ushort overlayLifecycle = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(40, 2));
        bool runtimeBlocked = span[42] != 0;
        ulong buildId = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(43, 8));

        int offset = 51;
        string gameFamilyId = ReadString(span, ref offset);
        string layoutFamilyId = ReadString(span, ref offset);
        string blockedReason = ReadString(span, ref offset);

        return new AgentRuntimeStatusPayload(
            statusCode,
            state,
            protocolMajor,
            protocolMinor,
            resolutionGeneration,
            supported,
            ready,
            unavailable,
            resolvedHooks,
            plannedHooks,
            grantedMask,
            gameThreadTick,
            overlayLifecycle,
            runtimeBlocked,
            buildId,
            gameFamilyId,
            layoutFamilyId,
            blockedReason);
    }

    internal static string ReadString(ReadOnlySpan<byte> span, ref int offset)
    {
        if (offset + 2 > span.Length)
        {
            throw new InvalidDataException("Agent runtime payload truncated reading a string length.");
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
        offset += 2;
        if (offset + length > span.Length)
        {
            throw new InvalidDataException("Agent runtime payload truncated reading string bytes.");
        }

        string value = length == 0 ? string.Empty : Encoding.UTF8.GetString(span.Slice(offset, length));
        offset += length;
        return value;
    }
}
