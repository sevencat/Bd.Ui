using System.Collections.ObjectModel;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Core.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

public sealed record EpicChildRow(string Id, string Title, string StatusText, bool IsClosed);

public sealed partial class EpicRowViewModel : ObservableObject
{
    public EpicRowViewModel(EpicProgressItem item, Attribution attribution, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        Id = item.Epic.Id;
        Title = item.Epic.DisplayTitle;
        StatusText = BdEnumMaps.FormatStatus(item.Epic.Status);
        ProgressText = item.Progress.Text;
        Ratio = item.Progress.Ratio;
        Closed = item.Progress.Closed;
        Total = item.Progress.Total;
        UpdatedText = BeadViewHelpers.RelativeTime(item.Epic.UpdatedAt, now);
        AttributionText = attribution.Kind switch
        {
            AttributionKind.Human => "human",
            AttributionKind.Agent => "agent",
            _ => "unknown"
        };

        foreach (var child in item.Children)
        {
            Children.Add(new EpicChildRow(
                child.Id,
                child.DisplayTitle,
                BdEnumMaps.FormatStatus(child.Status),
                child.IsClosed));
        }
    }

    public string Id { get; }

    public string Title { get; }

    public string StatusText { get; }

    public string ProgressText { get; }

    public double Ratio { get; }

    public int Closed { get; }

    public int Total { get; }

    public string UpdatedText { get; }

    public string AttributionText { get; }

    public ObservableCollection<EpicChildRow> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;
}

public sealed partial class EpicsViewModel : ObservableObject
{
    private readonly EpicProgressService _service;
    private readonly AttributionService _attribution;
    private readonly ToastService _toast;

    public EpicsViewModel(
        EpicProgressService service,
        AttributionService attribution,
        ToastService toast)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));
    }

    public event EventHandler<string>? EpicActivated;

    public ObservableCollection<EpicRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _includeEmpty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _summary = "尚未加载";

    [RelayCommand]
    public Task RefreshAsync() => RefreshCoreAsync(CancellationToken.None, silent: false);

    public async Task RefreshCoreAsync(CancellationToken cancellationToken, bool silent)
    {
        if (!silent)
        {
            IsBusy = true;
        }

        try
        {
            var items = await _service.LoadAsync(IncludeEmpty, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(new EpicRowViewModel(item, _attribution.Resolve(item.Epic.CreatedBy), now));
            }

            var closed = items.Sum(static item => item.Progress.Closed);
            var total = items.Sum(static item => item.Progress.Total);
            Summary = $"{items.Count} 个 Epic · {BeadViewHelpers.ProgressText(closed, total)} 已完成";
            ErrorMessage = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = _toast.Error(ex);
            if (!silent)
            {
                _toast.Handle(ex);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Open(EpicRowViewModel? row)
    {
        if (row is not null)
        {
            EpicActivated?.Invoke(this, row.Id);
        }
    }

    [RelayCommand]
    public void Toggle(EpicRowViewModel? row)
    {
        if (row is not null)
        {
            row.IsExpanded = !row.IsExpanded;
        }
    }

    public void OpenChild(EpicChildRow? row)
    {
        if (row is not null)
        {
            EpicActivated?.Invoke(this, row.Id);
        }
    }

    partial void OnIncludeEmptyChanged(bool value) => _ = RefreshCoreAsync(CancellationToken.None, silent: true);
}
