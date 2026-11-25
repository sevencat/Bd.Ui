using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bd.Core.Services;

namespace Bd.App.Controls;

public partial class ToastHost : UserControl
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4.5);

    private ToastService? _service;

    public ToastHost() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_service is not null)
        {
            _service.ToastAdded -= OnToastAdded;
        }

        _service = DataContext as ToastService;
        if (_service is not null)
        {
            _service.ToastAdded += OnToastAdded;
        }
    }

    private void OnToastAdded(object? sender, ToastMessage message)
    {
        var timer = new DispatcherTimer { Interval = Lifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _service?.Dismiss(message);
        };
        timer.Start();
    }

    private void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ToastMessage message })
        {
            _service?.Dismiss(message);
        }
    }
}
