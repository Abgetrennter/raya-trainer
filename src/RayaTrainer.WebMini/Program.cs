using RayaTrainer.Host;

namespace RayaTrainer.WebMini;

/// <summary>
/// RayaTrainer.WebMini —— 独立可选的 Web 遥控组件（原生 WinForms 单窗口）。
///
/// 主程序（RayaTrainer.App）不再内置 Web 服务；需要手机/浏览器遥控时单独运行本程序：
/// 1. 等待/发现红色警戒3进程（与主程序相同的版本探测与指纹门禁：同指纹接管、旧指纹拒绝）；
/// 2. 附加并安装 Patch（复用 <see cref="TrainerSessionManager"/> 的既有路径）；
/// 3. 启动 Kestrel Web 宿主，窗口内显示配对二维码、支持修改端口/绑定网卡。
///
/// 本程序不依赖 WPF：界面是最简单的 WinForms 单窗口（<see cref="MainForm"/>），
/// 设备配对审批走窗口弹窗（<see cref="WindowDeviceApprovalService"/>），
/// 预设来自设置文件（<see cref="SettingsPresetSource"/>），开关请求走命令队列直控路径
/// （不注入功能状态协调器）。
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception exception)
        {
            RayaTrainerCrashLog.Write(exception);
            MessageBox.Show(
                $"启动失败：{exception.Message}\r\n\r\n详情已写入崩溃日志。",
                "RAЯ Trainer WebMini",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
