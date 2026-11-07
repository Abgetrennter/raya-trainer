using System.Reflection;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.Core.Runtime;

public static class TrainerRuntimeAssets
{
    private const string ManifestResourceName = "RayaTrainer.Core.Assets.trainer_report.json";

    public static TrainerManifest LoadManifest()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ManifestResourceName)
            ?? throw new InvalidOperationException($"Missing embedded runtime asset: {ManifestResourceName}.");
        return TrainerManifestRepository.Load(stream, ManifestResourceName);
    }
}
