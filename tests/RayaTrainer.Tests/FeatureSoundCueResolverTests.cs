using RayaTrainer.App.Services;
using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class FeatureSoundCueResolverTests
{
    [Fact]
    public void ForToggleStateUsesSuccessCueWhenEnabled()
    {
        Assert.Equal(FeatureSoundCue.Success, FeatureSoundCueResolver.ForToggleState(enabled: true));
    }

    [Fact]
    public void ForToggleStateUsesDisabledCueWhenClosed()
    {
        Assert.Equal(FeatureSoundCue.Disabled, FeatureSoundCueResolver.ForToggleState(enabled: false));
    }

    [Theory]
    [InlineData(ActionDispatchResult.Consumed)]
    [InlineData(ActionDispatchResult.NotRequired)]
    public void ForActionResultUsesSuccessCueWhenActionSucceeded(ActionDispatchResult result)
    {
        Assert.Equal(FeatureSoundCue.Success, FeatureSoundCueResolver.ForActionResult(result));
    }

    [Fact]
    public void ForActionResultDoesNotPlayWhenActionTimedOut()
    {
        Assert.Null(FeatureSoundCueResolver.ForActionResult(ActionDispatchResult.TimedOut));
    }
}
