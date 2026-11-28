using Avalonia.Controls;
using Bd.Core.ViewModels;

namespace Bd.Mobile.Views;

public partial class MobileShellView : UserControl
{
    public MobileShellView() => InitializeComponent();

    /// <summary>
    /// 手机上单击一行就该进详情（没有双击可用）。选中后立刻清掉选中态，
    /// 这样从详情返回时列表不会留着一行高亮。
    /// </summary>
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || DataContext is not MobileShellViewModel vm)
        {
            return;
        }

        if (list.SelectedItem is not BoardTreeNodeViewModel row)
        {
            return;
        }

        list.SelectedItem = null;
        vm.OpenRowCommand.Execute(row);
    }
}
