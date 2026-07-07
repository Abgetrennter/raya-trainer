namespace RayaTrainer.App.Web.Auth;

public interface IDeviceApprovalService
{
    Task<bool> ApproveAsync(
        DeviceApprovalRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceApprovalRequest(
    string DeviceName,
    string UserAgent,
    string RemoteAddress);
