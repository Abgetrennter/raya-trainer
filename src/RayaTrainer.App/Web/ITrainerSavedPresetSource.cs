using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.Web;

public interface ITrainerSavedPresetSource
{
    IReadOnlyList<TrainerAppSettings> LoadSavedSettings();
}
