namespace RayaTrainer.Core.Versions;

public enum TargetSelectionStatus
{
    NoCandidate,
    SingleAutoSelected,
    SingleSupportedAmongMany,
    AmbiguousRequiresUserChoice,
    NoInstallableCandidate
}
