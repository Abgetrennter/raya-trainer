namespace RayaTrainer.Core.Agent;

public sealed record AgentMemoryWriteOperation(
    uint Address,
    AgentMemoryAddressMode AddressMode,
    byte[] Bytes);
