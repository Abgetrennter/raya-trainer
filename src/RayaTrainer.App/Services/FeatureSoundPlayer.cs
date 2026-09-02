using System.Media;
using RayaTrainer.Core.Features;

namespace RayaTrainer.App.Services;

public enum FeatureSoundCue
{
    Success,
    Disabled
}

public interface IFeatureSoundPlayer
{
    void Play(FeatureSoundCue cue);
}

public sealed class SystemFeatureSoundPlayer : IFeatureSoundPlayer
{
    public static SystemFeatureSoundPlayer Shared { get; } = new();

    private SystemFeatureSoundPlayer()
    {
    }

    public void Play(FeatureSoundCue cue)
    {
        var sound = cue switch
        {
            FeatureSoundCue.Success => SystemSounds.Asterisk,
            FeatureSoundCue.Disabled => SystemSounds.Exclamation,
            _ => SystemSounds.Beep
        };

        sound.Play();
    }
}

public static class FeatureSoundCueResolver
{
    public static FeatureSoundCue ForToggleState(bool enabled)
    {
        return enabled ? FeatureSoundCue.Success : FeatureSoundCue.Disabled;
    }

    public static FeatureSoundCue? ForActionResult(ActionDispatchResult result)
    {
        return result == ActionDispatchResult.TimedOut ? null : FeatureSoundCue.Success;
    }
}
