using System.Windows;
using RayaTrainer.App.ViewModels;

namespace RayaTrainer.App.Views;

/// <summary>
/// 游戏闪退诊断的知情选择弹窗（同意开启 / 首次导出提醒 / 删除确认共用）。
/// 文案由调用方传入并完整呈现——不设默认勾选，不持久化"今后自动同意"。
/// </summary>
public partial class CrashDiagnosticsConsentWindow : Window
{
    public CrashDiagnosticsConsentWindow(CrashConsentText text)
    {
        InitializeComponent();
        DataContext = text;
    }

    public bool Confirmed { get; private set; }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
