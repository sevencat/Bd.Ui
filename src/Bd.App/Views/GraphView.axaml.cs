using Avalonia.Controls;
using Bd.Core.ViewModels;
using Bd.Core.Views;

namespace Bd.App.Views;

public partial class GraphView : UserControl
{
    public GraphView() => InitializeComponent();

    private void OnNodeSelected(object? sender, string id)
    {
        if (DataContext is GraphViewModel vm)
        {
            vm.RootId = id;
            _ = vm.RefreshCoreAsync(CancellationToken.None, silent: true);
        }
    }

    private void OnConnectionRequested(object? sender, GraphConnectionRequest request)
    {
        if (DataContext is not GraphViewModel vm)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var type in vm.DependencyTypes)
        {
            var dependencyType = type;
            var item = new MenuItem { Header = dependencyType };
            item.Click += (_, _) => _ = vm.ConnectAsync(request.SourceId, request.TargetId, dependencyType);
            flyout.Items.Add(item);
        }

        flyout.ShowAt(GraphCanvas, showAtPointer: true);
    }
}
