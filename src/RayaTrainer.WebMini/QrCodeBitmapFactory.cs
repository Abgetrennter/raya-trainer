using QRCoder;

namespace RayaTrainer.WebMini;

/// <summary>
/// 配对二维码渲染：QRCoder 生成 <see cref="Bitmap"/> 直接显示在主窗口 PictureBox。
/// 替代拆分前主程序里的 WPF 版实现（BitmapSource），语义等价：ECC Q、模块放大、自带静区。
/// </summary>
public sealed class QrCodeBitmapFactory
{
    private const int ModulePixels = 6;

    public Bitmap Create(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var renderer = new QRCode(data);
        return renderer.GetGraphic(ModulePixels);
    }
}
