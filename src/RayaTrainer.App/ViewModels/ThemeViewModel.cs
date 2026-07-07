using System.Windows;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 主题切换（暗色/浅色）视图模型。从 MainViewModel 提取，纯 UI 关注点。
/// </summary>
public sealed class ThemeViewModel : ViewModelBase
{
    private bool _isDarkTheme = true;

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set { _isDarkTheme = value; OnPropertyChanged(); OnPropertyChanged(nameof(ThemeToggleText)); }
    }

    public string ThemeToggleText => IsDarkTheme ? "浅色主题" : "暗色主题";

    public RelayCommand ToggleThemeCommand { get; }

    public ThemeViewModel()
    {
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
    }

    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        var app = Application.Current;
        if (app is null) return;

        var themeDict = new ResourceDictionary
        {
            Source = IsDarkTheme
                ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
                : new Uri("Themes/LightTheme.xaml", UriKind.Relative)
        };

        var oldTheme = app.Resources.MergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Theme") == true);
        if (oldTheme is not null)
        {
            var idx = app.Resources.MergedDictionaries.IndexOf(oldTheme);
            app.Resources.MergedDictionaries.RemoveAt(idx);
            app.Resources.MergedDictionaries.Insert(idx, themeDict);
        }
        else
        {
            app.Resources.MergedDictionaries.Insert(0, themeDict);
        }
    }
}
