using RayaTrainer.App.Services;
using System.Windows.Media.Imaging;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class QrCodeImageFactoryTests
{
    [Fact]
    public void CreateBuildsFrozenBitmapForRemoteUrl()
    {
        var factory = new QrCodeImageFactory();

        var image = Assert.IsAssignableFrom<BitmapSource>(factory.Create("http://192.168.1.10:8787/"));

        Assert.True(image.IsFrozen);
        Assert.True(image.PixelWidth > 0);
        Assert.Equal(image.PixelWidth, image.PixelHeight);
    }
}
