using System.Buffers.Binary;
using System.Text;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Shared u16-length-prefixed UTF-8 string decoding used by the GetCapabilitySnapshot (cmd 67)
/// and GetRuntimeDiagnostics (cmd 68) payload readers. The retired GetRuntimeStatus payload was
/// the only other consumer; the decoder is kept here instead of duplicating it per payload.
/// </summary>
internal static class RuntimePayloadStrings
{
    public static string ReadString(ReadOnlySpan<byte> span, ref int offset)
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
