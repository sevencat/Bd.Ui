using Avalonia.Controls;
using Avalonia.Input;
using Bd.Core.ViewModels;

namespace Bd.App.Views;

public partial class EpicsView : UserControl
{
    public EpicsView() => InitializeComponent();

    private EpicsViewModel? Vm => DataContext as EpicsViewModel;

    private void OnToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: EpicRowViewModel row })
        {
            Vm?.Toggle(row);
        }
    }

    private void OnOpenClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: EpicRowViewModel row })
        {
            Vm?.Open(row);
        }
    }

    private void OnChildDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: EpicChildRow row })
        {
            Vm?.OpenChild(row);
            e.Handled = true;
        }
    }
}
