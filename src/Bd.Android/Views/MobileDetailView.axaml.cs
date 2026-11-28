using Avalonia.Controls;
using Avalonia.Input;
using Bd.Core.ViewModels;

namespace Bd.Mobile.Views;

public partial class MobileDetailView : UserControl
{
    public MobileDetailView() => InitializeComponent();

    /// <summary>点子任务跳到那条任务：详情屏内重新走一次 loading。</summary>
    private void OnChildTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is BeadDetailViewModel vm && sender is Control { DataContext: DetailChildRow row })
        {
            vm.OpenChild(row);
            e.Handled = true;
        }
    }
}
