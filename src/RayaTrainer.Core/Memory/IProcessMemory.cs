namespace RayaTrainer.Core.Memory;

public interface IProcessMemory
{
    byte[] ReadBytes(nint address, int count);

    void WriteBytes(nint address, ReadOnlySpan<byte> bytes);
}
