using Avalonia.Controls;
using Avalonia.Input;
using Bd.Core.ViewModels;

namespace Bd.App.Views;

public partial class BeadDetailView : UserControl
{
    public BeadDetailView() => InitializeComponent();

    private void OnChildDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: DetailChildRow row } && DataContext is BeadDetailViewModel vm)
        {
            vm.OpenChild(row);
            e.Handled = true;
        }
    }
}
