using Avalonia.Controls;
using Avalonia.Interactivity;
using Bd.Core.ViewModels;

namespace Bd.App.Views;

public partial class BeadEditorDialog : Window
{
    private BeadEditorViewModel? _vm;

    public BeadEditorDialog() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.Created -= OnCreated;
        }

        _vm = DataContext as BeadEditorViewModel;
        if (_vm is not null)
        {
            _vm.Created += OnCreated;
        }
    }

    private void OnCreated(object? sender, string id) => Close(id);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
