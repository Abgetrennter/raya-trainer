using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ResourceValueSettingsTests
{
    [Fact]
    public void DefaultValuesMatchBootstrapConstants()
    {
        var settings = ResourceValueSettings.Default;

        Assert.Equal(100000, settings.MoneyAmount);
        Assert.Equal(100000, settings.PowerValue);
        Assert.Equal(15, settings.ScPointValue);
    }

    [Theory]
    [InlineData("1", "1", "0", 1, 1, 0)]
    [InlineData("99999999", "99999999", "15", 99999999, 99999999, 15)]
    [InlineData(" 100000 ", " 200000 ", " 9 ", 100000, 200000, 9)]
    public void ParseAcceptsValidUiInput(
        string moneyText,
        string powerText,
        string scPointText,
        int money,
        int power,
        int scPoint)
    {
        var settings = ResourceValueSettings.Parse(moneyText, powerText, scPointText);

        Assert.Equal(money, settings.MoneyAmount);
        Assert.Equal(power, settings.PowerValue);
        Assert.Equal(scPoint, settings.ScPointValue);
    }

    [Theory]
    [InlineData("", "100000", "15")]
    [InlineData("0", "100000", "15")]
    [InlineData("100000000", "100000", "15")]
    [InlineData("100000", "0", "15")]
    [InlineData("100000", "100000000", "15")]
    [InlineData("100000", "100000", "-1")]
    [InlineData("100000", "100000", "16")]
    [InlineData("many", "100000", "15")]
    public void ParseRejectsInvalidUiInput(string moneyText, string powerText, string scPointText)
    {
        Assert.ThrowsAny<Exception>(() => ResourceValueSettings.Parse(moneyText, powerText, scPointText));
    }
}
