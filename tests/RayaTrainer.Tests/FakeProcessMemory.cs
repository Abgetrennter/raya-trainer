using RayaTrainer.Core.Memory;

namespace RayaTrainer.Tests;

internal sealed class FakeProcessMemory : IProcessMemory
{
    private readonly Dictionary<nint, byte> _bytes = new();

    public byte[] ReadBytes(nint address, int count)
    {
        var bytes = new byte[count];
        for (var index = 0; index < count; index++)
        {
            bytes[index] = _bytes.GetValueOrDefault(address + index);
        }
        return bytes;
    }

    public void WriteBytes(nint address, ReadOnlySpan<byte> bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            _bytes[address + index] = bytes[index];
        }
    }

    public byte ReadByte(nint address) => _bytes.GetValueOrDefault(address);
}
