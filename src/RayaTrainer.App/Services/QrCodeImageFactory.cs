using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace RayaTrainer.App.Services;

public sealed class QrCodeImageFactory : IQrCodeImageFactory
{
    private const int ModulePixels = 8;
    private const int QuietZoneModules = 2;

    public ImageSource Create(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var matrix = data.ModuleMatrix;
        var moduleCount = matrix.Count;
        var pixelSize = checked((moduleCount + QuietZoneModules * 2) * ModulePixels);
        var pixels = new byte[checked(pixelSize * pixelSize * 4)];

        FillWhite(pixels);
        for (var y = 0; y < moduleCount; y++)
        {
            for (var x = 0; x < matrix[y].Count; x++)
            {
                if (matrix[y][x])
                {
                    FillModule(pixels, pixelSize, x + QuietZoneModules, y + QuietZoneModules);
                }
            }
        }

        var image = BitmapSource.Create(
            pixelSize,
            pixelSize,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            pixelSize * 4);
        image.Freeze();
        return image;
    }

    private static void FillWhite(byte[] pixels)
    {
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
            pixels[index + 1] = 255;
            pixels[index + 2] = 255;
            pixels[index + 3] = 255;
        }
    }

    private static void FillModule(byte[] pixels, int pixelSize, int moduleX, int moduleY)
    {
        var startX = moduleX * ModulePixels;
        var startY = moduleY * ModulePixels;
        for (var y = 0; y < ModulePixels; y++)
        {
            for (var x = 0; x < ModulePixels; x++)
            {
                var index = ((startY + y) * pixelSize + startX + x) * 4;
                pixels[index] = 0;
                pixels[index + 1] = 0;
                pixels[index + 2] = 0;
                pixels[index + 3] = 255;
            }
        }
    }
}
