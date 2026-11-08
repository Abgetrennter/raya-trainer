using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RayaTrainer.Core.Features;
using RayaTrainer.App.ViewModels;

namespace RayaTrainer.App.Views;

public partial class ReinforcementUnitPickerWindow : Window
{
    public ReinforcementUnitPickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private ReinforcementUnitPickerViewModel? ViewModel => DataContext as ReinforcementUnitPickerViewModel;

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

    private void OnUnitItemPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem item ||
            item.DataContext is not ReinforcementUnitEntry unit ||
            ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.SelectedUnit = unit;
        if (!viewModel.HasSelectedUnitVariants)
        {
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = item
        };
        foreach (var variant in viewModel.SelectedUnitVariants)
        {
            var menuItem = new MenuItem
            {
                Header = variant.SourceId,
                ToolTip = variant.CodeText
            };
            menuItem.Click += (_, _) => viewModel.SelectUnitVariant(variant);
            menu.Items.Add(menuItem);
        }

        item.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
