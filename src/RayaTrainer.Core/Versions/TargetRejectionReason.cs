namespace RayaTrainer.Core.Versions;

/// <summary>
/// Why a detected RA3 process cannot be installed. Only meaningful when
/// <see cref="DetectedRa3Target.CanAttemptInstallation"/> is false; the App turns it into
/// user-facing text so the message can name the actual cause instead of listing every cause.
/// </summary>
public enum TargetRejectionReason
{
    None = 0,
    VersionUnreadable,
    Not32Bit,
    VersionUnsupported,
    NameNotOriginal,
    MixedBuild
}
