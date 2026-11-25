using System.Collections.ObjectModel;
using Bd.Core.Domain;
using Bd.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

public sealed partial class BoardViewModel : ObservableObject
{
    private readonly BoardService _board;
    private readonly BeadWriteService _writes;
    private readonly AttributionService _attribution;
    private readonly ToastService _toast;
    private BoardSnapshot _snapshot = BoardSnapshot.Empty(DateTimeOffset.UtcNow);

    public BoardViewModel(
        BoardService board,
        BeadWriteService writes,
        AttributionService attribution,
        ToastService toast)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _writes = writes ?? throw new ArgumentNullException(nameof(writes));
        _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));

        Columns =
        [
            new(BoardColumnKind.Backlog, "Backlog", "#8B95A5"),
            new(BoardColumnKind.Ready, "Ready", "#4CC38A"),
            new(BoardColumnKind.InProgress, "In Progress", "#5B9DFF"),
            new(BoardColumnKind.Blocked, "Blocked", "#E5484D"),
            new(BoardColumnKind.Done, "Done", "#8A8F98")
        ];

        Filter.FilterChanged += OnFilterChanged;
    }

    public ObservableCollection<BoardColumnViewModel> Columns { get; }

    public FilterBarViewModel Filter { get; } = new();

    public BoardSnapshot Snapshot => _snapshot;

    public event EventHandler? Updated;

    public event EventHandler<string>? CardActivated;

    public event EventHandler? CreateRequested;

    [RelayCommand]
    public void CreateTask() => CreateRequested?.Invoke(this, EventArgs.Empty);

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _lastLoadedText = "尚未加载";

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
            _snapshot = await _board.LoadAsync(Filter.ToFilter(), cancellationToken);
            Project();
            ErrorMessage = null;
            LastLoadedText = _snapshot.LoadedAt.ToLocalTime().ToString("HH:mm:ss");
            Updated?.Invoke(this, EventArgs.Empty);
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
    public void Open(BeadCardViewModel? card)
    {
        if (card is not null)
        {
            CardActivated?.Invoke(this, card.Id);
        }
    }

    public async Task MoveAsync(BeadCardViewModel? card, BoardColumnKind target)
    {
        if (card is null || card.Model.Status == BeadStatus.Closed && target == BoardColumnKind.Done)
        {
            return;
        }

        try
        {
            switch (target)
            {
                case BoardColumnKind.Backlog:
                    await _writes.TransitionAsync(card.Id, BeadStatus.Deferred, CancellationToken.None);
                    break;
                case BoardColumnKind.Ready:
                    await _writes.TransitionAsync(card.Id, BeadStatus.Open, CancellationToken.None);
                    break;
                case BoardColumnKind.InProgress:
                    await _writes.TransitionAsync(card.Id, BeadStatus.InProgress, CancellationToken.None);
                    break;
                case BoardColumnKind.Done:
                    await _writes.CloseAsync(card.Id, "moved to done", CancellationToken.None);
                    break;
                default:
                    _toast.Warning("Blocked 列由依赖关系决定，请先处理阻塞依赖");
                    return;
            }

            await RefreshCoreAsync(CancellationToken.None, silent: true);
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
            await RefreshCoreAsync(CancellationToken.None, silent: true);
        }
    }

    private void OnFilterChanged(object? sender, EventArgs e) =>
        _ = RefreshCoreAsync(CancellationToken.None, silent: true);

    private void Project()
    {
        var now = _snapshot.LoadedAt;
        foreach (var column in Columns)
        {
            column.Update(_snapshot.Column(column.Kind).Select(bead => CreateCard(bead, now)));
        }

        TotalCount = _snapshot.Total;
    }

    private BeadCardViewModel CreateCard(Bead bead, DateTimeOffset now)
    {
        var attribution = _attribution.Resolve(bead.CreatedBy);
        return new BeadCardViewModel(bead, attribution, now);
    }
}
