namespace RayaTrainer.App.Services;

public sealed class MobileRemoteAvailability : IMobileRemoteAvailability
{
    private string? _unavailableReason;

    public bool IsAvailable => string.IsNullOrWhiteSpace(_unavailableReason);

    public string UnavailableReason => _unavailableReason ?? "手机遥控服务未启动。";

    public void MarkUnavailable(string reason)
    {
        _unavailableReason = string.IsNullOrWhiteSpace(reason)
            ? "手机遥控服务未启动。"
            : reason.Trim();
    }
}
