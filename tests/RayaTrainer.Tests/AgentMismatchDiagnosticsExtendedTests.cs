using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using System.Text.Json;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentMismatchDiagnosticsExtendedTests
{
    /// <summary>
    /// Builds a v11 extended wire payload for GetMismatchDiagnostics (cmd 34).
    /// Layout: StatusCode(2) + AgentVersion(2) + HookAddress(4) + ExpectedLength(4) +
    ///         ActualLength(4) + DumpLength(4) + MismatchKind(1) + Reserved(3) + SubjectId(4)
    /// Followed by expectedBytes + actualBytes + dumpBytes.
    /// </summary>
    private static byte[] BuildPayload(
        ushort statusCode,
        ushort agentVersion,
        uint hookAddress,
        byte[] expectedBytes,
        byte[] actualBytes,
        byte[] dumpBytes,
        MismatchKind kind,
        uint subjectId)
    {
        var headerSize = 28;
        var payload = new byte[headerSize + expectedBytes.Length + actualBytes.Length + dumpBytes.Length];
        var span = payload.AsSpan();

        BitConverter.TryWriteBytes(span[..2], statusCode);
        BitConverter.TryWriteBytes(span.Slice(2, 2), agentVersion);
        BitConverter.TryWriteBytes(span.Slice(4, 4), hookAddress);
        BitConverter.TryWriteBytes(span.Slice(8, 4), (uint)expectedBytes.Length);
        BitConverter.TryWriteBytes(span.Slice(12, 4), (uint)actualBytes.Length);
        BitConverter.TryWriteBytes(span.Slice(16, 4), (uint)dumpBytes.Length);
        span[20] = (byte)kind;
        // Reserved[3] at 21-23 stays zero
        BitConverter.TryWriteBytes(span.Slice(24, 4), subjectId);

        var offset = headerSize;
        if (expectedBytes.Length > 0)
        {
            expectedBytes.CopyTo(payload, offset);
            offset += expectedBytes.Length;
        }
        if (actualBytes.Length > 0)
        {
            actualBytes.CopyTo(payload, offset);
            offset += actualBytes.Length;
        }
        if (dumpBytes.Length > 0)
        {
            dumpBytes.CopyTo(payload, offset);
        }

        return payload;
    }

    [Fact]
    public void ReadFrom_ParsesHookKind()
    {
        var expected = new byte[] { 0x8B, 0x51, 0x50, 0x3B, 0x50, 0x0C };
        var actual = new byte[] { 0x8B, 0x51, 0x50, 0xC7, 0x40, 0x0C };
        var dump = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x8B, 0x51, 0x50 };

        var payload = BuildPayload(
            statusCode: 0,          // Ok
            agentVersion: 11,
            hookAddress: 0x83ECA6,
            expectedBytes: expected,
            actualBytes: actual,
            dumpBytes: dump,
            kind: MismatchKind.Hook,
            subjectId: 42);

        var result = AgentMismatchDiagnosticsPayload.ReadFrom(payload);

        Assert.Equal(AgentStatusCode.Ok, result.StatusCode);
        Assert.Equal((ushort)11, result.AgentVersion);
        Assert.Equal(0x83ECA6u, result.HookAddress);
        Assert.Equal(MismatchKind.Hook, result.Kind);
        Assert.Equal(42u, result.SubjectId);
        Assert.True(result.HasMismatch);
        Assert.Equal(expected, result.ExpectedBytes);
        Assert.Equal(actual, result.ActualBytes);
        Assert.Equal(dump, result.DumpBytes);
    }

    [Fact]
    public void ReadFrom_ParsesRuntimePatchSetKind()
    {
        var payload = BuildPayload(
            statusCode: 0,          // Ok
            agentVersion: 11,
            hookAddress: 0x123456,
            expectedBytes: [0x01, 0x02],
            actualBytes: [0xFF, 0xFF],
            dumpBytes: [],
            kind: MismatchKind.RuntimePatchSet,
            subjectId: 1);

        var result = AgentMismatchDiagnosticsPayload.ReadFrom(payload);

        Assert.Equal(MismatchKind.RuntimePatchSet, result.Kind);
        Assert.Equal(1u, result.SubjectId);
        Assert.Equal(0x123456u, result.HookAddress);
    }

    [Fact]
    public void ReadFrom_ParsesPatchSetIpConflictKind()
    {
        // IP conflicts carry no expected/actual/dump bytes
        var payload = BuildPayload(
            statusCode: 0,          // Ok
            agentVersion: 11,
            hookAddress: 0x789ABC,
            expectedBytes: [],
            actualBytes: [],
            dumpBytes: [],
            kind: MismatchKind.PatchSetIpConflict,
            subjectId: 1);

        var result = AgentMismatchDiagnosticsPayload.ReadFrom(payload);

        Assert.Equal(MismatchKind.PatchSetIpConflict, result.Kind);
        Assert.Equal(1u, result.SubjectId);
        Assert.Equal(0x789ABCu, result.HookAddress);
        Assert.Empty(result.ExpectedBytes);
        Assert.Empty(result.ActualBytes);
        Assert.Empty(result.DumpBytes);
    }

    [Fact]
    public void ReadFrom_InvalidCommand_DefaultsKindAndSubject()
    {
        // StatusCode = InvalidCommand (6), all fields zero
        var payload = BuildPayload(
            statusCode: 6,          // InvalidCommand
            agentVersion: 11,
            hookAddress: 0,
            expectedBytes: [],
            actualBytes: [],
            dumpBytes: [],
            kind: MismatchKind.Hook,
            subjectId: 0);

        var result = AgentMismatchDiagnosticsPayload.ReadFrom(payload);

        Assert.Equal(AgentStatusCode.InvalidCommand, result.StatusCode);
        Assert.False(result.HasMismatch);
        Assert.Equal(MismatchKind.Hook, result.Kind);
        Assert.Equal(0u, result.SubjectId);
    }

    [Fact]
    public void ReadFrom_ThrowsOnShortPayload()
    {
        var shortPayload = new byte[10]; // Less than FixedSize(28)
        Assert.Throws<InvalidDataException>(() =>
            AgentMismatchDiagnosticsPayload.ReadFrom(shortPayload));
    }

    [Fact]
    public void ReadFrom_ThrowsOnInconsistentRegionSizes()
    {
        // Header says 10 bytes expected but only 5 bytes remain
        var payload = BuildPayload(
            statusCode: 0,
            agentVersion: 11,
            hookAddress: 0x100,
            expectedBytes: new byte[10],
            actualBytes: [],
            dumpBytes: [],
            kind: MismatchKind.Hook,
            subjectId: 0);
        // Truncate trailing bytes so expected length doesn't match
        var invalid = payload[..^5];

        Assert.Throws<InvalidDataException>(() =>
            AgentMismatchDiagnosticsPayload.ReadFrom(invalid));
    }

    [Fact]
    public void TrainerMismatchDiagnostic_FromPayload_RoundTrips()
    {
        var expected = new byte[] { 0x8B, 0x51 };
        var actual = new byte[] { 0xFF, 0xFF };
        var dump = new byte[] { 0x90 };

        var payload = BuildPayload(
            statusCode: 0,
            agentVersion: 11,
            hookAddress: 0xBEEF,
            expectedBytes: expected,
            actualBytes: actual,
            dumpBytes: dump,
            kind: MismatchKind.RuntimePatchSet,
            subjectId: 7);

        var parsed = AgentMismatchDiagnosticsPayload.ReadFrom(payload);
        var diag = TrainerMismatchDiagnostic.FromPayload(parsed);

        Assert.Equal(MismatchKind.RuntimePatchSet, diag.Kind);
        Assert.Equal(7u, diag.SubjectId);
        Assert.Equal(0xBEEFu, diag.HookAddress);
        Assert.Equal(expected, diag.ExpectedBytes);
        Assert.Equal(actual, diag.ActualBytes);
        Assert.Equal(dump, diag.DumpBytes);
        Assert.Contains("PatchSet 7 entry @ 0x", diag.SourceSummary);
    }

    [Fact]
    public void TrainerMismatchDiagnostic_SourceSummary_ReflectsKind()
    {
        var hookDiag = new TrainerMismatchDiagnostic(
            MismatchKind.Hook, 42, 0x1000, [], [], [],
            "Hook #42 @ 0x00001000");
        Assert.Contains("#42", hookDiag.SourceSummary);

        var psDiag = new TrainerMismatchDiagnostic(
            MismatchKind.RuntimePatchSet, 3, 0x2000, [], [], [],
            "PatchSet 3 entry @ 0x00002000");
        Assert.Contains("PatchSet 3", psDiag.SourceSummary);

        var ipDiag = new TrainerMismatchDiagnostic(
            MismatchKind.PatchSetIpConflict, 5, 0x3000, [], [], [],
            "PatchSet 5 IP conflict @ 0x00003000");
        Assert.Contains("PatchSet 5", ipDiag.SourceSummary);
    }
}

public sealed class PatchMismatchReportExtendedTests
{
    [Fact]
    public void WriteReport_IncludesDiagnosticsKindAndSubject()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var diagnostics = new[]
        {
            new TrainerMismatchDiagnostic(
                MismatchKind.Hook, 42, 0x83ECA6,
                [0x8B, 0x51, 0x50],
                [0x8B, 0x51, 0xC7],
                [0x90, 0x90, 0x90],
                "Hook #42 @ 0x0083ECA6")
        };

        var installResult = new PatchInstallResult(
            HookCount: 1,
            InstalledHookCount: 0,
            [
                new SkippedPatchHook(
                    new PatchHookPlan("ra3_1.12.game+43ECA6", 6, [0x8B, 0x51, 0x50])
                    {
                        SectionTitle = "Test",
                        EnableFlags = ["test"]
                    },
                    0, 1, 0x83ECA6,
                    [], [], 0, [],
                    "Test skip")
            ]);

        var path = PatchMismatchReportWriter.Write(
            directory,
            new TrainerTarget("test", 0x400000, true, true, 1, "test", "1.0"),
            installResult,
            new PatchMismatchReportOptions(),
            diagnostics);

        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);

        // Verify diagnostics array exists with kind/subject
        var diagnosticsArray = document.RootElement.GetProperty("diagnostics");
        Assert.Equal(1, diagnosticsArray.GetArrayLength());
        var entry = diagnosticsArray[0];
        Assert.Equal("Hook", entry.GetProperty("kind").GetString());
        Assert.Equal(42, entry.GetProperty("subjectId").GetInt32());
        Assert.Equal("0x83ECA6", entry.GetProperty("hookAddress").GetString());

        // Cleanup
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void WriteReport_OmitsDiagnosticsWhenNull()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var installResult = new PatchInstallResult(0, 0, []);
        var path = PatchMismatchReportWriter.Write(
            directory,
            new TrainerTarget("test", 0x400000, true, true, 1, "test", "1.0"),
            installResult,
            new PatchMismatchReportOptions());

        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("diagnostics", out _));

        Directory.Delete(directory, recursive: true);
    }
}
