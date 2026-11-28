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

    /// <summary>评论框 Ctrl/Cmd+Enter 提交，与 web 界面保持一致。</summary>
    private void OnCommentKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && DataContext is BeadDetailViewModel vm)
        {
            vm.AddCommentCommand.Execute(null);
            e.Handled = true;
        }
    }
}
