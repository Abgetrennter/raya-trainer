using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RayaTrainer.Core.Hotkeys;

namespace RayaTrainer.App.Controls;

/// <summary>
/// 按键捕获控件：点击进入捕获态后监听下一次按键组合，组装成 <see cref="HotkeyGesture"/> 并通过
/// <see cref="HotkeyCaptured"/> 事件抛出。Esc 取消捕获；Backspace/Delete 清空（设为无快捷键）。
/// 修饰键（Ctrl/Alt/Shift）单独按下时不触发捕获完成，等待主键。
/// </summary>
public sealed class HotkeyCaptureControl : ContentControl
{
    static HotkeyCaptureControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(HotkeyCaptureControl),
            new FrameworkPropertyMetadata(typeof(HotkeyCaptureControl)));
    }

    /// <summary>
    /// 当前绑定的快捷键文本（DisplayText 形式，如 "Ctrl+F1"）。null 或空串表示未分配。
    /// 设置此项会刷新显示。
    /// </summary>
    public static readonly DependencyProperty HotkeyTextProperty =
        DependencyProperty.Register(
            nameof(HotkeyText),
            typeof(string),
            typeof(HotkeyCaptureControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotkeyTextChanged));

    public string? HotkeyText
    {
        get => (string?)GetValue(HotkeyTextProperty);
        set => SetValue(HotkeyTextProperty, value);
    }

    /// <summary>当前是否已分配热键（只读，由 HotkeyText 派生，供模板做条件显示）。</summary>
    public static readonly DependencyPropertyKey HasHotkeyPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasHotkey), typeof(bool), typeof(HotkeyCaptureControl), new PropertyMetadata(false));

    public static readonly DependencyProperty HasHotkeyProperty = HasHotkeyPropertyKey.DependencyProperty;

    public bool HasHotkey => (bool)GetValue(HasHotkeyProperty);

    /// <summary>是否处于捕获态（用于模板视觉反馈）。</summary>
    public static readonly DependencyProperty IsCapturingProperty =
        DependencyProperty.Register(nameof(IsCapturing), typeof(bool), typeof(HotkeyCaptureControl), new PropertyMetadata(false));

    public bool IsCapturing
    {
        get => (bool)GetValue(IsCapturingProperty);
        private set => SetValue(IsCapturingProperty, value);
    }

    /// <summary>无快捷键时显示的占位文本（模板绑定）。</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(HotkeyCaptureControl), new PropertyMetadata("点击设置"));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>捕获成功（或被清空）时触发。事件参数为新的快捷键文本，空串表示清空。</summary>
    public event Action<string?>? HotkeyCaptured;

    public HotkeyCaptureControl()
    {
        Focusable = true;
        PreviewMouseDown += OnPreviewMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
        LostFocus += OnLostFocus;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 点击控件即进入捕获态并抢焦点。
        e.Handled = true;
        IsCapturing = true;
        Keyboard.Focus(this);
        Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsCapturing)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Esc：取消捕获，不改变现有值。
        if (key == Key.Escape)
        {
            EndCapture();
            return;
        }

        // 单独按下修饰键时不结束捕获，等待主键。
        if (IsModifierKey(key))
        {
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        // 至少需要一个主键；纯修饰键组合（理论上不会到这里，前面已拦截）也不接受。
        var gesture = HotkeyGesture.FromVirtualKey(virtualKey, modifiers);
        HotkeyText = gesture.DisplayText;
        HotkeyCaptured?.Invoke(gesture.DisplayText);
        EndCapture();
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (IsCapturing)
        {
            EndCapture();
        }
    }

    private void EndCapture()
    {
        IsCapturing = false;
        // 让焦点移出控件，避免再次按空格等键意外重新进入捕获态。
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null);
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
    }

    private static void OnHotkeyTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (HotkeyCaptureControl)d;
        var value = e.NewValue as string;
        control.SetValue(HasHotkeyPropertyKey, !string.IsNullOrWhiteSpace(value));
    }
}
