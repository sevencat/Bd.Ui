using Bd.Core.Domain;
using Bd.Core.Filters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

public sealed partial class FilterBarViewModel : ObservableObject
{
    private CancellationTokenSource? _debounce;

    public IReadOnlyList<string> Types { get; } =
        ["全部", "task", "bug", "feature", "epic", "chore", "decision", "spike", "story"];

    public IReadOnlyList<string> Priorities { get; } = ["全部", "P0", "P1", "P2", "P3", "P4"];

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private string _selectedType = "全部";

    [ObservableProperty]
    private string _selectedPriority = "全部";

    [ObservableProperty]
    private string _labelsText = string.Empty;

    [ObservableProperty]
    private bool _includeArchived;

    public event EventHandler? FilterChanged;

    public BoardFilter ToFilter()
    {
        var type = SelectedType is null or "全部" ? (BeadType?)null : BdEnumMaps.ParseType(SelectedType);
        var priority = SelectedPriority is null or "全部" ? (BeadPriority?)null : BdEnumMaps.ParsePriority(SelectedPriority);
        var labels = LabelsText
            .Split([',', '，', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new BoardFilter
        {
            SearchText = SearchText,
            Type = type,
            Priority = priority,
            Labels = labels,
            IncludeArchived = IncludeArchived
        };
    }

    [RelayCommand]
    public void Clear()
    {
        SearchText = null;
        SelectedType = "全部";
        SelectedPriority = "全部";
        LabelsText = string.Empty;
        IncludeArchived = false;
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSearchTextChanged(string? value) => Schedule();

    partial void OnSelectedTypeChanged(string value) => Schedule();

    partial void OnSelectedPriorityChanged(string value) => Schedule();

    partial void OnLabelsTextChanged(string value) => Schedule();

    partial void OnIncludeArchivedChanged(bool value) => Schedule();

    private void Schedule()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;
        _ = RaiseAfterDelayAsync(token);
    }

    private async Task RaiseAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        FilterChanged?.Invoke(this, EventArgs.Empty);
    }
}
