using System.Collections.ObjectModel;
using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Core.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

public sealed partial class GraphViewModel : ObservableObject
{
    private readonly DependencyService _dependencies;
    private readonly IBdStore _store;
    private readonly ToastService _toast;

    public GraphViewModel(DependencyService dependencies, IBdStore store, ToastService toast)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));
    }

    public ObservableCollection<GraphNodeBox> Nodes { get; } = [];

    public ObservableCollection<GraphEdge> Edges { get; } = [];

    public IReadOnlyList<string> DependencyTypes { get; } = ["blocks", "related", "parent-child", "discovered-from"];

    public IReadOnlyList<int> Depths { get; } = [1, 2, 3, 4, 5];

    public event EventHandler<string>? NodeActivated;

    [ObservableProperty]
    private string _rootId = string.Empty;

    [ObservableProperty]
    private int _depth = 2;

    [ObservableProperty]
    private string _newTargetId = string.Empty;

    [ObservableProperty]
    private string _removeTargetId = string.Empty;

    [ObservableProperty]
    private string _selectedDependencyType = "blocks";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _summary = "尚未加载";

    [ObservableProperty]
    private double _canvasWidth = 760;

    [ObservableProperty]
    private double _canvasHeight = 420;

    [ObservableProperty]
    private GraphNodeBox? _selectedNode;

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
            if (string.IsNullOrWhiteSpace(RootId))
            {
                RootId = await PickRootAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(RootId))
            {
                Summary = "没有可用于绘图的节点";
                Nodes.Clear();
                Edges.Clear();
                return;
            }

            var data = await _dependencies.LoadNeighborhoodAsync(RootId, Depth, cancellationToken);
            var (boxes, width, height) = GraphLayout.Layout(data, RootId);
            CanvasWidth = width;
            CanvasHeight = height;

            Nodes.Clear();
            foreach (var box in boxes)
            {
                Nodes.Add(box);
            }

            Edges.Clear();
            foreach (var edge in data.Edges)
            {
                Edges.Add(edge);
            }

            SelectedNode = Nodes.FirstOrDefault(node => string.Equals(node.Id, RootId, StringComparison.OrdinalIgnoreCase));
            Summary = $"{Nodes.Count} 节点 · {Edges.Count} 边 · 中心 {RootId}";
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
    public void SelectNode(GraphNodeBox? node)
    {
        if (node is null)
        {
            return;
        }

        RootId = node.Id;
        _ = RefreshCoreAsync(CancellationToken.None, silent: true);
    }

    [RelayCommand]
    public void OpenSelected()
    {
        if (SelectedNode is not null)
        {
            NodeActivated?.Invoke(this, SelectedNode.Id);
        }
    }

    [RelayCommand]
    public async Task AddDependencyAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTargetId))
        {
            _toast.Warning("请输入被依赖的任务 ID");
            return;
        }

        try
        {
            await _dependencies.AddAsync(
                RootId,
                NewTargetId.Trim(),
                BdEnumMaps.ParseDependency(SelectedDependencyType),
                CancellationToken.None);
            _toast.Success($"{RootId} → {NewTargetId} 已建立");
            NewTargetId = string.Empty;
            await RefreshCoreAsync(CancellationToken.None, silent: true);
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    /// <summary>拖拽连线：按选定类型新增依赖，成环时给出提示并保持原图。</summary>
    public async Task ConnectAsync(string sourceId, string targetId, string dependencyType)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
        {
            return;
        }

        if (string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
        {
            _toast.Warning("不能把任务连到自身");
            return;
        }

        try
        {
            await _dependencies.AddAsync(
                sourceId,
                targetId,
                BdEnumMaps.ParseDependency(dependencyType),
                CancellationToken.None);
            _toast.Success($"{sourceId} → {targetId} 已建立（{dependencyType}）");
            ErrorMessage = null;
            await RefreshCoreAsync(CancellationToken.None, silent: true);
        }
        catch (BdException ex) when (ex.Code == BdErrorCode.InvalidArgument)
        {
            ErrorMessage = ex.Message;
            _toast.Warning(ex.Message);
        }
        catch (Exception ex)
        {
            ErrorMessage = _toast.Error(ex);
            _toast.Handle(ex);
        }
    }

    [RelayCommand]
    public async Task RemoveDependencyAsync()
    {
        if (string.IsNullOrWhiteSpace(RemoveTargetId))
        {
            _toast.Warning("请输入要解除依赖的任务 ID");
            return;
        }

        try
        {
            await _dependencies.RemoveAsync(RootId, RemoveTargetId.Trim(), CancellationToken.None);
            _toast.Success($"已解除 {RootId} → {RemoveTargetId}");
            RemoveTargetId = string.Empty;
            await RefreshCoreAsync(CancellationToken.None, silent: true);
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    partial void OnDepthChanged(int value) => _ = RefreshCoreAsync(CancellationToken.None, silent: true);

    private async Task<string> PickRootAsync(CancellationToken cancellationToken)
    {
        var beads = await _store.ListAsync(new BeadListQuery { Limit = 200 }, cancellationToken);
        var epic = beads.FirstOrDefault(static bead => bead.IsEpic && !bead.IsClosed);
        if (epic is not null)
        {
            return epic.Id;
        }

        return beads.FirstOrDefault(static bead => !bead.IsClosed)?.Id ?? beads.FirstOrDefault()?.Id ?? string.Empty;
    }
}
