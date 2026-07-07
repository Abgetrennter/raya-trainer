using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerProcessNameTests
{
    [Theory]
    [InlineData("ra3_1.12.game", "ra3_1.12")]
    [InlineData("ra3_1.12", "ra3_1.12")]
    [InlineData("RA3.exe", "RA3")]
    public void ToProcessNameOnlyRemovesExecutableSuffixes(string input, string expected)
    {
        Assert.Equal(expected, TrainerProcessName.ToProcessName(input));
    }

    [Theory]
    [InlineData("ra3_1.12", "ra3_1.12.game")]
    [InlineData("ra3_1.12.game", "ra3_1.12")]
    public void MatchesTreatsGameExtensionAsExecutableSuffix(string left, string right)
    {
        Assert.True(TrainerProcessName.Matches(left, right));
    }
}
