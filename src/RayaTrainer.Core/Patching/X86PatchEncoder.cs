namespace RayaTrainer.Core.Patching;

public static class X86PatchEncoder
{
    public static byte[] EncodeNearJumpWithNops(nint source, nint target, int totalLength)
    {
        if (totalLength < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLength), "x86 near jumps require at least 5 bytes.");
        }

        var bytes = Enumerable.Repeat((byte)0x90, totalLength).ToArray();
        bytes[0] = 0xE9;
        var relative = checked((int)(target - (source + 5)));
        BitConverter.GetBytes(relative).CopyTo(bytes, 1);
        return bytes;
    }
}
