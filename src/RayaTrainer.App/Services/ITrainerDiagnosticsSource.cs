using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.App.Services;

public interface ITrainerDiagnosticsSource
{
    event EventHandler? DiagnosticsChanged;

    IReadOnlyList<TrainerDiagnosticEvent> DiagnosticEvents { get; }

    TrainerDiagnosticSnapshot GetDiagnosticSnapshot(IReadOnlyList<TrainerFeature> features, int maxEvents = 200);

    Task<TrainerDiagnosticSnapshot> RefreshDiagnosticsAsync(
        IReadOnlyList<TrainerFeature> features,
        CancellationToken cancellationToken = default);

    void RecordDiagnosticEvent(
        DiagnosticEventSeverity severity,
        string code,
        string message,
        string? detail = null);
}
