using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Host.Web;

public interface ITrainerSavedPresetSource
{
    IReadOnlyList<TrainerAppSettings> LoadSavedSettings();
}
