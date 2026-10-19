namespace RayaTrainer.Core.Features;

public enum SecretProtocolBindingProbeStatus : uint
{
    NotRun = 0,
    NoPlayer = 1,
    MissingTemplate = 2,
    Completed = 3
}

public enum SecretProtocolBindingItemStatus : uint
{
    NotRun = 0,
    TemplateMissing = 1,
    TechGrantedUpgradeMissing = 2,
    TechAndUpgradeGranted = 3,
    TechGrantedUpgradeManuallyGranted = 4
}

public sealed record SecretProtocolBindingProbeResult(
    uint PlayerAddress,
    uint ScienceManagerAddress,
    uint AirPowerTechAddress,
    SecretProtocolBindingItemStatus AirPowerStatus,
    uint EnhancedKamikazeTechAddress,
    SecretProtocolBindingItemStatus EnhancedKamikazeStatus,
    SecretProtocolBindingProbeStatus Status);
