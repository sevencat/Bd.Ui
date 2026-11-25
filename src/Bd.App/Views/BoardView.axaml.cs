using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Bd.Core.ViewModels;

namespace Bd.App.Views;

public partial class BoardView : UserControl
{
    private static readonly DataFormat<BeadCardViewModel> CardFormat =
        DataFormat.CreateInProcessFormat<BeadCardViewModel>("bd-ui-bead-card");

    public BoardView() => InitializeComponent();

    private BoardViewModel? Vm => DataContext as BoardViewModel;

    public void FocusSearch() => this.FindControl<TextBox>("SearchBox")?.Focus();

    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: BeadCardViewModel card })
        {
            Vm?.Open(card);
            e.Handled = true;
        }
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: BeadCardViewModel card })
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _ = StartDragAsync(e, card);
    }

    private async Task StartDragAsync(PointerPressedEventArgs e, BeadCardViewModel card)
    {
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(CardFormat, card));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        catch (Exception)
        {
            // 拖动被系统取消不算错误。
        }
    }

    private void OnColumnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(CardFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnColumnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: BoardColumnViewModel column })
        {
            return;
        }

        var card = e.DataTransfer.TryGetValue(CardFormat);
        if (card is null)
        {
            return;
        }

        e.Handled = true;
        _ = Vm?.MoveAsync(card, column.Kind) ?? Task.CompletedTask;
    }
}
