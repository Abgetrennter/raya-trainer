using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ReinforcementSettingsTests
{
    [Theory]
    [InlineData("0x6586A5A0")]
    [InlineData("6586a5a0")]
    [InlineData("6586A5A0")]
    [InlineData("  0X6586a5a0  ")]
    public void ParseUnitIdAcceptsHexWithOrWithoutPrefixAndAnyCase(string unitIdText)
    {
        var settings = ReinforcementSettings.Parse(unitIdText, "8", "3");

        Assert.Equal(0x6586A5A0u, settings.UnitId);
    }
}
