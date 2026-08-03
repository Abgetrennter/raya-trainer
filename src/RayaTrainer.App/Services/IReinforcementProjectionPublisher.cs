using RayaTrainer.Core.Features;

namespace RayaTrainer.App.Services;

/// <summary>
/// Publishing seam for the Reinforcement Preset Console (R3). MainViewModel pushes the
/// complete saved preset snapshot after every successful save/delete; the session manager
/// forwards it to the projection coordinator, which replaces the Agent-held read-only
/// projection (command 62). Mirrors the <c>IProductControlSessionHost</c> exposure pattern
/// so the concrete coordinator type stays off the public session-service surface.
/// </summary>
public interface IReinforcementProjectionPublisher
{
    void PublishReinforcementPresets(IReadOnlyList<ReinforcementPreset> presets);
}
