using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class WindowBoundsTests
{
    [Fact]
    public void IsValid_PositiveSize_ReturnsTrue()
    {
        Assert.True(new WindowBounds(100, 100, 800, 600, false).IsValidOnAnyScreen());
    }

    [Fact]
    public void IsValid_LegalNegativeCoordinate_ReturnsTrue()
    {
        Assert.True(new WindowBounds(-1920, -500, 800, 600, false).IsValidOnAnyScreen());
    }

    [Fact]
    public void IsValid_ZeroOrNegativeSize_ReturnsFalse()
    {
        Assert.False(new WindowBounds(100, 100, 0, 600, false).IsValidOnAnyScreen());
        Assert.False(new WindowBounds(100, 100, 800, -1, false).IsValidOnAnyScreen());
    }

    [Fact]
    public void IsValid_Maximized_SkipsGeometryCheck()
    {
        Assert.True(new WindowBounds(0, 0, -1, -1, true).IsValidOnAnyScreen());
    }
}
