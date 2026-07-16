using RayaTrainer.Core.Features;

namespace RayaTrainer.App.Web;

public interface ITrainerPresetSource
{
    IReadOnlyList<ReinforcementPreset> GetReinforcementPresets();

    IReadOnlyList<SecretProtocolQueuePreset> GetSecretProtocolPresets();

    // Stage 2 新增
    IReadOnlyList<FeaturePreset> GetFeaturePresets();

    void SaveFeaturePreset(string name, FeatureStateSnapshot snapshot);

    bool DeleteFeaturePreset(string name);
}
