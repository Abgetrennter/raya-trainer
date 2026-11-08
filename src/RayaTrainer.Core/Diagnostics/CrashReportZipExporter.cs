using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RayaTrainer.Core.Diagnostics;

/// <summary>
/// 闪退报告外发 ZIP 导出器（设计 §6）：报告元数据 + 冻结诊断（文本，做用户目录替换）
/// + 游戏 Dump（二进制原样——设计明确不做不可靠的字符串覆盖）+ 外发清单（文件/大小/SHA256）。
/// 与普通诊断导出（TrainerDiagnosticExporter）完全分离：普通诊断包绝不夹带 Dump。
/// 异步执行、可取消；取消/失败只删除本次未完成 ZIP，不触碰原始报告目录。
/// </summary>
public static class CrashReportZipExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task ExportAsync(
        string zipPath,
        GameCrashReportBundle bundle,
        string reportDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportDirectory);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath)) is { Length: > 0 } dir
            ? dir
            : ".");

        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var manifest = new StringBuilder()
            .AppendLine("RayaTrainer 游戏闪退报告外发清单")
            .AppendLine($"报告 ID: {bundle.Report.ReportId}")
            .AppendLine($"状态: {bundle.Report.Status}")
            .AppendLine();

        try
        {
            await using (var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                manifest.Append(await WriteJsonAsync(archive, "report.json", bundle.Report, userProfilePath, cancellationToken))
                    .Append(await WriteJsonAsync(
                        archive,
                        "snapshot.json",
                        new DiagnosticExportEnvelope(bundle.FrozenSnapshot, bundle.FrozenEvents),
                        userProfilePath,
                        cancellationToken));

                if (!string.IsNullOrWhiteSpace(bundle.MismatchText))
                {
                    manifest.Append(await WriteTextAsync(
                        archive, "patch-mismatch/report.txt",
                        TrainerDiagnosticExporter.RedactUserProfile(bundle.MismatchText, userProfilePath),
                        cancellationToken));
                }

                // Dump 打包（压缩，字节语义不变）：内存现场无法可靠匿名化，导出前的隐私
                // 提醒由 UI 层承担。deflate 实测 843MB→201MB（23.8%，7s）——传输体积关键。
                if (!string.IsNullOrWhiteSpace(bundle.Report.DumpFileName))
                {
                    var dumpPath = Path.Combine(reportDirectory, bundle.Report.DumpFileName);
                    if (File.Exists(dumpPath))
                    {
                        manifest.Append(await WriteFileAsync(archive, "dump/" + bundle.Report.DumpFileName, dumpPath, cancellationToken));
                    }
                }

                await WriteTextAsync(archive, "MANIFEST.txt", manifest.ToString() + "说明: Dump 为游戏内存现场，可能包含无法完全去除的个人信息。\n", cancellationToken);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch
            {
                // 保留原始导出异常。
            }

            throw;
        }
    }

    private sealed record DiagnosticExportEnvelope(
        TrainerDiagnosticSnapshot? Snapshot,
        IReadOnlyList<TrainerDiagnosticEvent> Events);

    private static async Task<string> WriteJsonAsync(
        ZipArchive archive, string name, object value, string userProfilePath, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return await WriteTextAsync(
            archive, name, TrainerDiagnosticExporter.RedactUserProfile(json, userProfilePath), ct);
    }

    private static async Task<string> WriteTextAsync(
        ZipArchive archive, string name, string content, CancellationToken ct)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), ct);
        return ManifestLine(name, Encoding.UTF8.GetByteCount(content));
    }

    private static async Task<string> WriteFileAsync(
        ZipArchive archive, string name, string filePath, CancellationToken ct)
    {
        // Dump 以 deflate 压缩打包（内存现场高度可压缩，实测 23.8%）；MANIFEST 记录
        // 原始字节数与原始字节的 SHA-256（哈希按源文件算，与压缩无关）。
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }

        return ManifestLine(name, total) + $"       sha256: {Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}\n";
    }

    private static string ManifestLine(string name, long bytes)
        => $"- {name}  ({bytes} bytes)\n";
}
