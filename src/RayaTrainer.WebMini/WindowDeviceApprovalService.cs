using RayaTrainer.Host.Web.Auth;

namespace RayaTrainer.WebMini;

/// <summary>
/// 窗口弹窗设备配对审批：配对请求到达时在 UI 线程弹确认框（设备名/IP/UserAgent），
/// 「是」批准、「否」或任何异常一律拒绝（fail-closed，默认焦点在「否」）。
/// 审批同时写入主窗口日志区；多个并发配对请求串行弹窗，不交错。
/// </summary>
public sealed class WindowDeviceApprovalService : IDeviceApprovalService
{
    private readonly Control _owner;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _serial = new(1, 1);

    public WindowDeviceApprovalService(Control owner, Action<string> log)
    {
        _owner = owner;
        _log = log;
    }

    public async Task<bool> ApproveAsync(
        DeviceApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _log($"设备配对请求：{request.DeviceName}（{request.RemoteAddress}）");

            var result = DialogResult.No;
            try
            {
                _owner.Invoke(() =>
                {
                    result = MessageBox.Show(
                        _owner,
                        $"设备：{request.DeviceName}\r\n" +
                        $"地址：{request.RemoteAddress}\r\n" +
                        $"浏览器：{request.UserAgent}\r\n\r\n" +
                        "是否允许本次会话访问遥控面板？",
                        "RAЯ Trainer WebMini - 设备配对请求",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);
                });
            }
            catch (ObjectDisposedException)
            {
                return Deny("窗口已关闭，已拒绝。");
            }
            catch (InvalidOperationException)
            {
                return Deny("窗口不可用，已拒绝。");
            }

            var approved = result == DialogResult.Yes;
            _log(approved ? $"已允许 {request.DeviceName} 配对。" : $"已拒绝 {request.DeviceName} 配对。");
            return approved;
        }
        finally
        {
            _serial.Release();
        }
    }

    private bool Deny(string reason)
    {
        _log(reason);
        return false;
    }
}
