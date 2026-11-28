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
    private bool _suspendTreeRefresh;
    private bool _restoringTree;

    /// <summary>记住用户的展开/折叠选择，刷新重建树时不回退成默认状态。</summary>
    private readonly Dictionary<string, bool> _expandState = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>父子链异常时的递归上限，防止栈溢出。</summary>
    private const int MaxTreeDepth = 64;

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
    }

    public ObservableCollection<BoardColumnViewModel> Columns { get; }

    /// <summary>树状视图的根节点（父任务不在当前筛选结果里的任务与顶层任务）。</summary>
    public ObservableCollection<BoardTreeNodeViewModel> TreeRoots { get; } = [];

    /// <summary>树状视图当前可见行：根节点按展开状态摊平后的顺序，供列表直接渲染。</summary>
    public ObservableCollection<BoardTreeNodeViewModel> TreeRows { get; } = [];

    public BoardSnapshot Snapshot => _snapshot;

    public bool IsTreeEmpty => TreeRows.Count == 0;

    /// <summary>当前选中任务的 ID（右侧详情显示的即是它）。</summary>
    [ObservableProperty]
    private string? _selectedId;

    /// <summary>左树的选中行，与 ListBox.SelectedItem 双向绑定。</summary>
    [ObservableProperty]
    private BoardTreeNodeViewModel? _selectedRow;

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

    /// <summary>
    /// 是否把已关闭（Closed）的任务也拉进看板与树。
    /// 切换只改筛选条件，不会自动请求数据——必须由用户手动点「刷新」才生效，
    /// 避免每次勾选都打一次远端全量查询。
    /// </summary>
    [ObservableProperty]
    private bool _showClosed;

    /// <summary>筛选范围已改但当前快照还是旧的，需要手动刷新才生效。</summary>
    [ObservableProperty]
    private bool _isStale;

    /// <summary>请求外壳（Shell）执行一次完整刷新；刷新动作与状态栏由 Shell 统一负责。</summary>
    public event EventHandler? RefreshRequested;

    [RelayCommand]
    public void RequestRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);

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
            _snapshot = await _board.LoadAsync(ShowClosed, cancellationToken);
            Project();
            ErrorMessage = null;
            IsStale = false;
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
        if (card is null)
        {
            return;
        }

        var node = EnumerateNodes(TreeRoots).FirstOrDefault(candidate => candidate.Card == card);
        if (node is not null)
        {
            Activate(node);
            return;
        }

        CardActivated?.Invoke(this, card.Id);
    }

    /// <summary>选中树节点：只做单行高亮，不打开右侧详情（详情由双击触发）。</summary>
    public void Select(BoardTreeNodeViewModel? node) => SelectedRow = node;

    /// <summary>打开某个树节点的详情（双击树行时调用）。</summary>
    public void Activate(BoardTreeNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        Select(node);
        CardActivated?.Invoke(this, node.Id);
    }

    public void ClearSelection()
    {
        SelectedRow = null;
        SelectedId = null;
    }

    /// <summary>重新建树后按 ID 还原高亮；还原不应重新打开详情（否则刷新会打断阅读）。</summary>
    public void RestoreSelection(string? id)
    {
        var node = string.IsNullOrEmpty(id)
            ? null
            : EnumerateNodes(TreeRoots).FirstOrDefault(candidate => candidate.Id == id);

        SelectedRow = node;
        if (node is not null)
        {
            SelectedId = node.Id;
        }
    }

    /// <summary>单击只更新选中高亮；右侧详情必须双击才会加载。</summary>
    partial void OnSelectedRowChanged(BoardTreeNodeViewModel? value) => SelectedId = value?.Id;

    /// <summary>切换筛选范围只标记「需要刷新」，真实数据仍等用户点刷新。</summary>
    partial void OnShowClosedChanged(bool value) => IsStale = true;

    [RelayCommand]
    public void ExpandAll() => SetExpandedAll(true);

    [RelayCommand]
    public void CollapseAll() => SetExpandedAll(false);

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

    private void Project()
    {
        var now = _snapshot.LoadedAt;
        foreach (var column in Columns)
        {
            column.Update(_snapshot.Column(column.Kind).Select(bead => CreateCard(bead, now)));
        }

        TotalCount = _snapshot.Total;
        ProjectTree(now);
    }

    /// <summary>按 parent-child 递归建树；父任务不在当前筛选结果内时，子任务升为根，避免卡片凭空消失。</summary>
    private void ProjectTree(DateTimeOffset now)
    {
        _suspendTreeRefresh = true;
        _restoringTree = true;
        try
        {
            TreeRoots.Clear();
            TreeRows.Clear();

            var beads = new List<Bead>();
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in Columns)
            {
                foreach (var bead in _snapshot.Column(column.Kind))
                {
                    if (!ShowClosed && bead.Status == BeadStatus.Closed)
                    {
                        continue;
                    }

                    if (known.Add(Key(bead.Project, bead.Id)))
                    {
                        beads.Add(bead);
                    }
                }
            }

            var childrenOf = new Dictionary<string, List<Bead>>(StringComparer.OrdinalIgnoreCase);
            var roots = new List<Bead>();
            foreach (var bead in beads)
            {
                var parentKey = ResolveParentKey(bead, known);
                if (parentKey is null)
                {
                    roots.Add(bead);
                    continue;
                }

                if (!childrenOf.TryGetValue(parentKey, out var siblings))
                {
                    childrenOf[parentKey] = siblings = [];
                }

                siblings.Add(bead);
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                TreeRoots.Add(BuildNode(root, 0, childrenOf, visited, now));
            }

            // 父子关系成环时上面遍历不到的节点补成根，保证每张卡片都出现在树上。
            foreach (var bead in beads)
            {
                if (!visited.Contains(Key(bead.Project, bead.Id)))
                {
                    TreeRoots.Add(BuildNode(bead, 0, childrenOf, visited, now));
                }
            }
        }
        finally
        {
            _restoringTree = false;
            _suspendTreeRefresh = false;
        }

        RefreshTreeRows();
        RestoreSelection(SelectedId);
    }

    private BoardTreeNodeViewModel BuildNode(
        Bead bead,
        int depth,
        Dictionary<string, List<Bead>> childrenOf,
        HashSet<string> visited,
        DateTimeOffset now)
    {
        var key = Key(bead.Project, bead.Id);
        var node = new BoardTreeNodeViewModel(CreateCard(bead, now), depth);
        node.ExpansionChanged += OnNodeExpansionChanged;
        visited.Add(key);

        // 默认展开顶层、折叠深层；用户手动改过的状态优先。过深时不再递归（防御异常数据）。
        node.IsExpanded = _expandState.TryGetValue(key, out var expanded) ? expanded : depth == 0;
        if (depth >= MaxTreeDepth || !childrenOf.TryGetValue(key, out var children))
        {
            node.UpdateRollup();
            return node;
        }

        foreach (var child in children)
        {
            if (visited.Contains(Key(child.Project, child.Id)))
            {
                continue;
            }

            node.Children.Add(BuildNode(child, depth + 1, childrenOf, visited, now));
        }

        node.NotifyChildrenChanged();
        node.UpdateRollup();
        return node;
    }

    /// <summary>父任务在当前集合内才挂到父节点下；跨项目 ID 用「项目/ID」区分。</summary>
    private static string? ResolveParentKey(Bead bead, HashSet<string> known)
    {
        if (string.IsNullOrWhiteSpace(bead.ParentId)
            || string.Equals(bead.ParentId, bead.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var key = Key(bead.Project, bead.ParentId);
        return known.Contains(key) ? key : null;
    }

    private static string Key(string? project, string id) =>
        string.IsNullOrEmpty(project) ? id : project + "/" + id;

    private void OnNodeExpansionChanged(object? sender, EventArgs e)
    {
        if (_restoringTree)
        {
            return;
        }

        if (sender is BoardTreeNodeViewModel node)
        {
            _expandState[Key(node.Card.Model.Project, node.Id)] = node.IsExpanded;
        }

        if (!_suspendTreeRefresh)
        {
            RefreshTreeRows();
        }
    }

    private void SetExpandedAll(bool expanded)
    {
        _suspendTreeRefresh = true;
        try
        {
            foreach (var node in EnumerateNodes(TreeRoots))
            {
                node.IsExpanded = expanded;
            }
        }
        finally
        {
            _suspendTreeRefresh = false;
        }

        RefreshTreeRows();
    }

    private static IEnumerable<BoardTreeNodeViewModel> EnumerateNodes(
        IEnumerable<BoardTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in EnumerateNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private void RefreshTreeRows()
    {
        TreeRows.Clear();
        foreach (var root in TreeRoots)
        {
            AppendVisible(root);
        }

        OnPropertyChanged(nameof(IsTreeEmpty));
    }

    private void AppendVisible(BoardTreeNodeViewModel node)
    {
        TreeRows.Add(node);
        if (!node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AppendVisible(child);
        }
    }

    private BeadCardViewModel CreateCard(Bead bead, DateTimeOffset now)
    {
        var attribution = _attribution.Resolve(bead.CreatedBy);
        return new BeadCardViewModel(bead, attribution, now);
    }
}
