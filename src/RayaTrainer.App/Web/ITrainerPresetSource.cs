using RayaTrainer.Core.Features;

namespace RayaTrainer.App.Web;

public interface ITrainerPresetSource
{
    IReadOnlyList<ReinforcementPreset> GetReinforcementPresets();

    IReadOnlyList<SecretProtocolQueuePreset> GetSecretProtocolPresets();
}
