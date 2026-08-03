using System.Windows;

namespace RayaTrainer.App.Web.Auth;

public sealed class WpfDeviceApprovalService : IDeviceApprovalService
{
    public Task<bool> ApproveAsync(
        DeviceApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult(false);
        }

        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowApprovalDialog(request));
        }

        return dispatcher.InvokeAsync(
                () => ShowApprovalDialog(request),
                System.Windows.Threading.DispatcherPriority.Normal,
                cancellationToken)
            .Task;
    }

    private static bool ShowApprovalDialog(DeviceApprovalRequest request)
    {
        var message =
            $"检测到远程设备请求连接：\n\n设备：{request.DeviceName}\n地址：{request.RemoteAddress}\n浏览器：{request.UserAgent}\n\n是否允许本次会话访问修改器遥控面板？";

        return MessageBox.Show(
                Application.Current?.MainWindow,
                message,
                "RAЯ Trainer 设备配对",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            == MessageBoxResult.Yes;
    }
}
