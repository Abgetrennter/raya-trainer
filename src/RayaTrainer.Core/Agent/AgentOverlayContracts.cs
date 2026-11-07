using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

public enum AgentOverlayLifecycle : ushort
{
    Disabled = 0,
    Installing = 1,
    WaitingForFrame = 2,
    Ready = 3,
    Stopping = 4,
    Failed = 5
}

[Flags]
public enum AgentOverlayFlags : uint
{
    None = 0,
    Supported = 1u << 0,
    Enabled = 1u << 1,
    Visible = 1u << 2,
    EndSceneHooked = 1u << 3,
    ResetHooked = 1u << 4,
    WndProcHooked = 1u << 5,
    LogicFreezeAvailable = 1u << 6,
    InMatch = 1u << 7
}

public enum AgentOverlayError : uint
{
    None = 0,
    UnsupportedTarget = 1,
    NativeCatalogUnavailable = 2,
    LogicFreezeHookUnavailable = 3,
    D3D9Unavailable = 4,
    ProbeWindowFailed = 5,
    ProbeDeviceFailed = 6,
    InvalidVtableAddress = 7,
    HookInitializationFailed = 8,
    HookInstallationFailed = 9,
    WindowProcHookFailed = 10,
    ImGuiInitializationFailed = 11,
    RenderThreadCleanupTimedOut = 12
}

public readonly record struct AgentOverlayControlRequest(bool Enabled, bool Visible)
{
    public const int Size = 8;

    public byte[] Encode()
    {
        if (!Enabled && Visible)
        {
            throw new InvalidOperationException("Overlay cannot be visible while disabled.");
        }

        var payload = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), Enabled ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), Visible ? 1u : 0u);
        return payload;
    }
}

public readonly record struct AgentOverlayStatusPayload(
    AgentStatusCode StatusCode,
    AgentOverlayLifecycle Lifecycle,
    AgentOverlayFlags Flags,
    uint RenderFrameCount,
    uint ButtonClickCount,
    uint DeviceResetCount,
    AgentOverlayError LastError)
{
    public const int Size = 24;

    public bool IsReady => Lifecycle == AgentOverlayLifecycle.Ready &&
        Flags.HasFlag(AgentOverlayFlags.Enabled) &&
        Flags.HasFlag(AgentOverlayFlags.EndSceneHooked) &&
        Flags.HasFlag(AgentOverlayFlags.ResetHooked);

    public static AgentOverlayStatusPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != Size)
        {
            throw new InvalidDataException($"Agent overlay status payload must be {Size} bytes, actual {payload.Length}.");
        }

        var span = payload.Span;
        return new AgentOverlayStatusPayload(
            (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0, 2)),
            (AgentOverlayLifecycle)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2)),
            (AgentOverlayFlags)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4)),
            (AgentOverlayError)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20, 4)));
    }
}
