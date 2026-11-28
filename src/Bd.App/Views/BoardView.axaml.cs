using Avalonia.Controls;
using Avalonia.Input;
using Bd.Core.ViewModels;

namespace Bd.App.Views;

public partial class BoardView : UserControl
{
    public BoardView() => InitializeComponent();

    /// <summary>树行双击才打开右侧详情；单击只切换选中高亮。</summary>
    private void OnTreeRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: BoardTreeNodeViewModel node }
            && DataContext is BoardViewModel viewModel)
        {
            viewModel.Activate(node);
            e.Handled = true;
        }
    }
}
