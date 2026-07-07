namespace RayaTrainer.Core.Versions;

public sealed record TargetSelectionResult(
    TargetSelectionStatus Status,
    DetectedRa3Target? SelectedTarget,
    IReadOnlyList<DetectedRa3Target> Candidates,
    string Message);
