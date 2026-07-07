using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

public readonly record struct AgentProtocolHeader(
    uint Magic,
    ushort Version,
    AgentCommand Command,
    uint SequenceId,
    uint PayloadLength)
{
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < AgentProtocol.HeaderSize)
        {
            throw new ArgumentException($"Destination must be at least {AgentProtocol.HeaderSize} bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination[..4], Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, sizeof(ushort)), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, sizeof(ushort)), (ushort)Command);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, sizeof(uint)), SequenceId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, sizeof(uint)), PayloadLength);
    }

    public static AgentProtocolHeader ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < AgentProtocol.HeaderSize)
        {
            throw new InvalidDataException($"Agent protocol header must be at least {AgentProtocol.HeaderSize} bytes, actual {source.Length}.");
        }

        return new AgentProtocolHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(source[..4]),
            BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, sizeof(ushort))),
            (AgentCommand)BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, sizeof(ushort))),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8, sizeof(uint))),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(12, sizeof(uint))));
    }
}
