using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RayaTrainer.Core.Diagnostics;

public static class TrainerDiagnosticExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Export(
        string zipPath,
        TrainerDiagnosticSnapshot snapshot,
        IReadOnlyList<TrainerDiagnosticEvent> events,
        string? mismatchReportPath = null,
        string? userProfilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(events);

        var directory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        userProfilePath ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            using var stream = File.Create(zipPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
            WriteJson(archive, "diagnostics.json", new DiagnosticExportEnvelope(snapshot, events), userProfilePath);

            if (!string.IsNullOrWhiteSpace(mismatchReportPath) && File.Exists(mismatchReportPath))
            {
                var report = File.ReadAllText(mismatchReportPath);
                WriteText(
                    archive,
                    $"patch-mismatch/{Path.GetFileName(mismatchReportPath)}",
                    RedactUserProfile(report, userProfilePath));
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
                // Preserve the original export exception.
            }

            throw;
        }
    }

    private static void WriteJson(ZipArchive archive, string name, object value, string userProfilePath)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        WriteText(archive, name, RedactUserProfile(json, userProfilePath));
    }

    private static void WriteText(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string RedactUserProfile(string value, string userProfilePath)
    {
        if (string.IsNullOrWhiteSpace(userProfilePath))
        {
            return value;
        }

        var redacted = value.Replace(userProfilePath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var jsonEscapedPath = userProfilePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        redacted = redacted.Replace(jsonEscapedPath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var slashPath = userProfilePath.Replace('\\', '/');
        return redacted.Replace(slashPath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DiagnosticExportEnvelope(
        TrainerDiagnosticSnapshot Snapshot,
        IReadOnlyList<TrainerDiagnosticEvent> Events);
}
