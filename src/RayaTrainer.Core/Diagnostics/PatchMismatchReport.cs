using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Diagnostics;

public sealed record PatchMismatchReportOptions(int DumpBytesBefore = 16, int DumpBytesAfter = 96);

public sealed record PatchMismatchReportResult(
    PatchInstallResult InstallResult,
    string? ReportPath)
{
    public IReadOnlyList<SkippedPatchHook> SkippedHooks => InstallResult.SkippedHooks;
}

public static class PatchMismatchReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Write(
        string diagnosticsDirectory,
        TrainerTarget target,
        PatchInstallResult installResult,
        PatchMismatchReportOptions options)
    {
        Directory.CreateDirectory(diagnosticsDirectory);
        var path = Path.Combine(
            diagnosticsDirectory,
            $"patch-mismatch-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
        var report = CreateReport(target, installResult, options);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, report, JsonOptions);
        return path;
    }

    private static object CreateReport(
        TrainerTarget target,
        PatchInstallResult installResult,
        PatchMismatchReportOptions options)
    {
        return new
        {
            target = new
            {
                processId = target.ProcessId,
                processName = target.ProcessName,
                modulePath = target.ModulePath,
                moduleBase = FormatAddress(target.ModuleBase),
                version = target.FileVersion
            },
            options = new
            {
                dumpBytesBefore = options.DumpBytesBefore,
                dumpBytesAfter = options.DumpBytesAfter
            },
            summary = new
            {
                hookCount = installResult.HookCount,
                installedHookCount = installResult.InstalledHookCount,
                skippedCount = installResult.SkippedHooks.Count
            },
            reason = "Patch 点原始字节不匹配；可能原因：该位置已经被 patch 过、游戏版本不一致，或者 MOD 加载时修改了代码段。",
            skippedHooks = installResult.SkippedHooks.Select(ToDto).ToArray()
        };
    }

    private static object ToDto(SkippedPatchHook skipped)
    {
        return new
        {
            hookIndex = skipped.HookIndex,
            hookCount = skipped.HookCount,
            addressExpression = skipped.Hook.Address,
            absoluteAddress = FormatAddress(skipped.AbsoluteAddress),
            sectionTitle = skipped.Hook.SectionTitle,
            enableFlags = skipped.Hook.EnableFlags,
            expectedBytes = FormatBytes(skipped.ExpectedBytes),
            actualBytes = FormatBytes(skipped.ActualBytes),
            reason = skipped.Reason,
            dumpStartAddress = FormatAddress(skipped.DumpStartAddress),
            dumpBytes = FormatBytes(skipped.DumpBytes),
            instructions = CompatibilitySampler
                .Disassemble(skipped.DumpBytes, unchecked((ulong)skipped.DumpStartAddress))
                .Select(instruction => new
                {
                    ip = FormatAddress(unchecked((nint)instruction.Ip)),
                    instruction.Length,
                    instruction.Bytes,
                    instruction.Text
                })
                .ToArray()
        };
    }

    private static string FormatAddress(nint address)
    {
        return $"0x{address:X}";
    }

    private static string FormatBytes(IEnumerable<byte> bytes)
    {
        return string.Join(" ", bytes.Select(value => value.ToString("X2")));
    }
}
