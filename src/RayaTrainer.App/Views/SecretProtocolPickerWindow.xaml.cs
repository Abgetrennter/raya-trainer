using System.Windows;
using RayaTrainer.App.ViewModels;

namespace RayaTrainer.App.Views;

public partial class SecretProtocolPickerWindow : Window
{
    public SecretProtocolPickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private SecretProtocolPickerViewModel? ViewModel => DataContext as SecretProtocolPickerViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.RequestClose += OnRequestClose;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.RequestClose -= OnRequestClose;
        }
    }

    private void OnRequestClose(bool? dialogResult)
    {
        Dispatcher.Invoke(() =>
        {
            DialogResult = dialogResult ?? false;
        });
    }
}
