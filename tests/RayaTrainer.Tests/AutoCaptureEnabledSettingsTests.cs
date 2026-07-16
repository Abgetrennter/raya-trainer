using RayaTrainer.Core.Features;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public class AutoCaptureEnabledSettingsTests
{
    [Fact]
    public void Default_IsFalse()
    {
        var settings = TrainerAppSettings.Default;
        Assert.False(settings.AutoCaptureEnabled);
    }

    [Fact]
    public void Construct_WithAutoCaptureEnabled_True_RoundTrips()
    {
        var settings = new TrainerAppSettings(
            string.Empty, "-ui", 30,
            ResourceValueSettings.Default,
            Array.Empty<ReinforcementPreset>(),
            new Dictionary<string, string>(),
            string.Empty, string.Empty, null,
            HidePrimaryActionCard: false,
            AutoCaptureEnabled: true);
        Assert.True(settings.AutoCaptureEnabled);
    }
}
