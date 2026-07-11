using System.IO.Compression;
using System.Text.Json;
using RayaTrainer.App.Services;
using RayaTrainer.Core.Diagnostics;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerDiagnosticsTests
{
    [Fact]
    public void EventStreamKeepsNewestTwoHundredAndDeduplicatesAdjacentErrors()
    {
        var manager = new TrainerSessionManager();
        for (var index = 1; index <= 205; index++)
        {
            manager.RecordDiagnosticEvent(
                DiagnosticEventSeverity.Error,
                $"test.error.{index}",
                $"错误 {index}");
        }

        manager.RecordDiagnosticEvent(
            DiagnosticEventSeverity.Error,
            "test.error.205",
            "错误 205");

        var events = manager.GetDiagnosticSnapshot([]).RecentEvents;

        Assert.Equal(200, events.Count);
        Assert.Equal(6, events[0].Sequence);
        Assert.Equal(205, events[^1].Sequence);
        Assert.Equal(events.OrderBy(item => item.Sequence), events);
    }

    [Fact]
    public void OfflineSnapshotAndCapabilityUseWaitingState()
    {
        var manager = new TrainerSessionManager();
        var feature = TestAssets.LoadManifest().Features[0];

        var snapshot = manager.GetDiagnosticSnapshot([feature]);

        Assert.Equal(TrainerDiagnosticHealth.Offline, snapshot.Health);
        Assert.Null(snapshot.Target);
        Assert.Equal(FeatureCapabilityState.Waiting, snapshot.Capabilities.Single().State);
        Assert.Equal("NO_TARGET", snapshot.Capabilities.Single().ReasonCode);
    }

    [Fact]
    public void ExportCreatesDeserializableRedactedPackageWithOptionalReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "RayaTrainer.Tests", Guid.NewGuid().ToString("N"));
        var userProfile = Path.Combine(root, "UserProfile");
        var reportPath = Path.Combine(userProfile, "diagnostics", "mismatch.json");
        var zipPath = Path.Combine(root, "ra3-diagnostics.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { path = Path.Combine(userProfile, "game") }));
        var snapshot = TrainerDiagnosticSnapshot.Offline with
        {
            CapturedAt = DateTimeOffset.Now,
            Summary = $"报告位于 {reportPath}",
            LastReportPath = reportPath
        };
        var events = new[]
        {
            new TrainerDiagnosticEvent(1, DateTimeOffset.Now, DiagnosticEventSeverity.Error, "patch.mismatch", "不匹配", reportPath)
        };

        TrainerDiagnosticExporter.Export(zipPath, snapshot, events, reportPath, userProfile);

        using var archive = ZipFile.OpenRead(zipPath);
        var diagnosticsEntry = Assert.Single(archive.Entries, entry => entry.FullName == "diagnostics.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "patch-mismatch/mismatch.json");
        using var reader = new StreamReader(diagnosticsEntry.Open());
        var json = reader.ReadToEnd();
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Offline", document.RootElement.GetProperty("Snapshot").GetProperty("Health").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("Events").GetArrayLength());
        Assert.Contains("%USERPROFILE%", json, StringComparison.Ordinal);
        Assert.DoesNotContain(userProfile, json, StringComparison.OrdinalIgnoreCase);
        var reportEntry = Assert.Single(archive.Entries, entry => entry.FullName == "patch-mismatch/mismatch.json");
        using var reportReader = new StreamReader(reportEntry.Open());
        var report = reportReader.ReadToEnd();
        Assert.Contains("%USERPROFILE%", report, StringComparison.Ordinal);
        Assert.DoesNotContain(userProfile, report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Offline_snapshot_has_empty_matched_symbols()
    {
        var snapshot = TrainerDiagnosticSnapshot.Offline;
        Assert.Empty(snapshot.Signatures.MatchedSymbols);
    }
}
