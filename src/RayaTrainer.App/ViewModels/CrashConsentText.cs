namespace RayaTrainer.App.ViewModels;

/// <summary>知情选择弹窗文案载体（标题/正文/补充块/按钮文字；一次性、不可变）。</summary>
public sealed class CrashConsentText
{
    public string Title { get; init; } = "";
    public string Heading { get; init; } = "";
    public string Body { get; init; } = "";
    public string Detail { get; init; } = "";
    public string ConfirmText { get; init; } = "";
    public string CancelText { get; init; } = "";
}
