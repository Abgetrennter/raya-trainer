using RayaTrainer.Core.Features;

namespace RayaTrainer.App.Services;

/// <summary>
/// Publishing seam for the Secret Protocol Preset Console (P3). MainViewModel pushes the
/// complete saved preset snapshot after every successful save/delete; the session manager
/// forwards it to the projection coordinator, which replaces the Agent-held read-only
/// projection (command 64). Mirrors the <c>IReinforcementProjectionPublisher</c> exposure
/// pattern so the concrete coordinator type stays off the public session-service surface.
/// </summary>
public interface ISecretProtocolProjectionPublisher
{
    void PublishSecretProtocolPresets(IReadOnlyList<SecretProtocolQueuePreset> presets);
}
