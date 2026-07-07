namespace RayaTrainer.App.Web.Auth;

public sealed class InMemoryDeviceApprovalService : IDeviceApprovalService
{
    private readonly bool _approved;

    public InMemoryDeviceApprovalService(bool approved)
    {
        _approved = approved;
    }

    public Task<bool> ApproveAsync(
        DeviceApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_approved);
    }
}
