using System.Windows;
using RayaTrainer.App.ViewModels;

namespace RayaTrainer.App;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(MainViewModel.LoadDefault())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
        base.OnClosed(e);
    }
}
