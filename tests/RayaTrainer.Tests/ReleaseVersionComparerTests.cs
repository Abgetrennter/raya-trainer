using RayaTrainer.App.Services;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ReleaseVersionComparerTests
{
    [Theory]
    [InlineData("v0.1.14", "v0.1.13", 1)]
    [InlineData("0.1.13", "v0.1.13", 0)]
    [InlineData("v0.1.12", "v0.1.13", -1)]
    [InlineData("v0.2.0", "v0.1.99", 1)]
    public void CompareNormalizesLeadingVAndSemanticParts(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(ReleaseVersionComparer.Compare(left, right)));
    }

    [Fact]
    public void IsNewerReturnsTrueOnlyForHigherStableVersion()
    {
        Assert.True(ReleaseVersionComparer.IsNewer("v0.1.14", "v0.1.13"));
        Assert.False(ReleaseVersionComparer.IsNewer("v0.1.13", "v0.1.13"));
        Assert.False(ReleaseVersionComparer.IsNewer("v0.1.12", "v0.1.13"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("local")]
    [InlineData("1.2")]
    public void TryCompareReturnsFalseForUnsupportedVersionText(string version)
    {
        Assert.False(ReleaseVersionComparer.TryCompare(version, "v0.1.13", out _));
        Assert.False(ReleaseVersionComparer.IsNewer("v0.1.14", version));
    }
}
