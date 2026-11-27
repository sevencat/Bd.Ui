using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bd.Core.ViewModels;

namespace Bd.App.Views;

public partial class MainWindow : Window
{
    private ShellViewModel? _shell;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_shell is null || e.Handled)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.N when ctrl:
                _ = ShowEditorAsync();
                e.Handled = true;
                break;
            case Key.R when ctrl:
            case Key.F5:
                _shell.RefreshCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F when ctrl:
                FocusSearch();
                e.Handled = true;
                break;
            case Key.D1 when ctrl:
                _shell.Navigate("board");
                e.Handled = true;
                break;
            case Key.D2 when ctrl:
                _shell.Navigate("epics");
                e.Handled = true;
                break;
            case Key.D3 when ctrl:
                _shell.Navigate("settings");
                e.Handled = true;
                break;
            case Key.Escape:
                _shell.CloseDetail();
                e.Handled = true;
                break;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_shell is not null)
        {
            _shell.ThemeChanged -= OnThemeChanged;
            _shell.CreateRequested -= OnCreateRequested;
        }

        _shell = DataContext as ShellViewModel;
        if (_shell is not null)
        {
            _shell.ThemeChanged += OnThemeChanged;
            _shell.CreateRequested += OnCreateRequested;
        }
    }

    private void OnCreateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = ShowEditorAsync();

    private void OnCreateRequested(object? sender, EventArgs e) => _ = ShowEditorAsync();

    private void OnThemeChanged(object? sender, string theme) => ApplyTheme(theme);

    private static void ApplyTheme(string theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
        }
    }

    private async Task ShowEditorAsync()
    {
        if (_shell is null || _shell.IsReadOnly)
        {
            return;
        }

        _shell.Editor.Reset();
        var dialog = new BeadEditorDialog { DataContext = _shell.Editor };
        await dialog.ShowDialog<string?>(this);
    }

    private void FocusSearch()
    {
        if (_shell is null)
        {
            return;
        }

        _shell.Navigate("board");
        Dispatcher.UIThread.Post(() =>
            this.GetVisualDescendants().OfType<BoardView>().FirstOrDefault()?.FocusSearch());
    }
}
