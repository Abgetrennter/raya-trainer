namespace RayaTrainer.Core.Runtime;

public sealed record WindowBounds(double X, double Y, double Width, double Height, bool IsMaximized)
{
    /// <summary>
    /// 校验窗口尺寸合法（宽高正数）。坐标不校验（合法负坐标由用户自负）。
    /// 最大化时不校验几何。
    /// </summary>
    public bool IsValidOnAnyScreen()
    {
        if (IsMaximized)
        {
            return true;
        }

        return Width > 0 && Height > 0;
    }
}
