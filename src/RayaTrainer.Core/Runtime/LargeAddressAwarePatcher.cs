using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RayaTrainer.Core.Runtime;

public sealed record LaaPatchResult(
    bool Success,
    string? BackupPath,
    string? ErrorMessage);

public static class LargeAddressAwarePatcher
{
    private const ushort ImageFileLargeAddressAware = 0x0020;
    private const uint PeSignature = 0x00004550;
    private const int HeaderReadSize = 0x8000;

    public static bool? CheckFlag(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            stream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();

            stream.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != PeSignature)
                return null;

            stream.Seek(peOffset + 22, SeekOrigin.Begin);
            var characteristics = reader.ReadUInt16();

            return (characteristics & ImageFileLargeAddressAware) != 0;
        }
        catch
        {
            return null;
        }
    }

    public static bool HasBackup(string filePath) => File.Exists(filePath + ".Backup");

    public static LaaPatchResult ApplyFlag(string filePath)
    {
        try
        {
            var backupPath = filePath + ".Backup";
            if (!File.Exists(backupPath))
            {
                File.Copy(filePath, backupPath, overwrite: false);
            }

            var fileInfo = new FileInfo(filePath);
            var readSize = (int)Math.Min(HeaderReadSize, fileInfo.Length);
            var bytes = new byte[readSize];
            using (var readStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                readStream.Read(bytes, 0, bytes.Length);
            }

            var peOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C, 4));
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(peOffset, 4)) != PeSignature)
                return new LaaPatchResult(false, backupPath, "无效的 PE 文件。");

            var characteristicsOffset = peOffset + 22;
            var current = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(characteristicsOffset, 2));
            if ((current & ImageFileLargeAddressAware) != 0)
                return new LaaPatchResult(true, HasBackup(filePath) ? backupPath : null, null);

            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(characteristicsOffset, 2),
                (ushort)(current | ImageFileLargeAddressAware));

            WriteBytesWithRetry(filePath, bytes, bytes.Length);

            var mapResult = MapFileAndCheckSumW(filePath, out _, out var checkSum);
            if (mapResult == 0)
            {
                using (var reread = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    reread.Read(bytes, 0, bytes.Length);
                }

                var optionalHeaderOffset = peOffset + 24;
                var magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(optionalHeaderOffset, 2));
                var checksumOffset = magic switch
                {
                    0x10B => optionalHeaderOffset + 64,
                    0x20B => optionalHeaderOffset + 68,
                    _ => throw new InvalidOperationException("不支持的 PE 格式。")
                };

                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(checksumOffset, 4), checkSum);
                WriteBytesWithRetry(filePath, bytes, bytes.Length);
            }

            return new LaaPatchResult(true, HasBackup(filePath) ? backupPath : null, null);
        }
        catch (Exception ex)
        {
            return new LaaPatchResult(false, HasBackup(filePath) ? filePath + ".Backup" : null, ex.Message);
        }
    }

    private static void WriteBytesWithRetry(string filePath, byte[] bytes, int count)
    {
        using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Write, FileShare.Read);
        stream.Write(bytes, 0, count);
    }

    [DllImport("imagehlp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint MapFileAndCheckSumW(
        string filename,
        out uint headerSum,
        out uint checkSum);
}
