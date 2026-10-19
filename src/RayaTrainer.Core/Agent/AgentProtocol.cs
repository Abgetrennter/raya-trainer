namespace RayaTrainer.Core.Agent;

public static class AgentProtocol
{
    public const uint Magic = 0x41594152;

    /// <summary>
    /// Protocol major version (wire compatibility). Bumped 13 -> 14 for the simplified
    /// migration: the Agent owns runtime resolution and identity is derived from generated
    /// deterministic sources (<see cref="AgentBuildIdentity.CatalogContractHash"/> /
    /// <see cref="AgentBuildIdentity.BuildId"/>) instead of a hand-rotated build fingerprint.
    /// A v13 Agent is wire-incompatible with v14 and is refused at connect time (restart the
    /// game). Within v14, additive commands, products and capabilities are negotiated through
    /// capability/schema, so a matching major with a different <see cref="AgentBuildIdentity.BuildId"/>
    /// may be taken over (see <see cref="AgentBuildIdentity.EvaluateTakeover"/>) without a global
    /// identity rotation.
    /// </summary>
    public const ushort Version = 14;
    public const int HeaderSize = 16;
    public const uint MaxPayloadLength = 64 * 1024;

    public static void Validate(AgentProtocolHeader header)
    {
        if (header.Magic != Magic)
        {
            throw new InvalidDataException($"Agent protocol magic mismatch. Expected 0x{Magic:X8}, actual 0x{header.Magic:X8}.");
        }

        if (header.Version != Version)
        {
            throw new InvalidDataException($"Agent protocol version mismatch. Expected {Version}, actual {header.Version}.");
        }

        if (header.PayloadLength > MaxPayloadLength)
        {
            throw new InvalidDataException($"Agent payload length {header.PayloadLength} exceeds limit {MaxPayloadLength}.");
        }
    }
}
