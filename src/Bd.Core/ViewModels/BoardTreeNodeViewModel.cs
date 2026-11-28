using System.Collections.ObjectModel;
using Bd.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

/// <summary>树视图的节点：包一张卡片，按 parent-child 递归挂子节点，可展开/折叠。</summary>
public sealed partial class BoardTreeNodeViewModel : ObservableObject
{
    internal BoardTreeNodeViewModel(BeadCardViewModel card, int depth)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
        Depth = depth;
    }

    public BeadCardViewModel Card { get; }

    public string Id => Card.Id;

    /// <summary>层级深度，根为 0；视图据此缩进。</summary>
    public int Depth { get; }

    /// <summary>左侧缩进宽度（像素）。Core 不引用 Avalonia，因此给裸数值由视图绑定。</summary>
    public double IndentWidth => Depth * 22d;

    public ObservableCollection<BoardTreeNodeViewModel> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public string ChildCountText => Children.Count == 0 ? "末级" : $"{Children.Count} 项子任务";

    public string ToggleGlyph => IsExpanded ? "▾" : "▸";

    /// <summary>状态图标：与 beads web 界面一致，用符号区分状态。</summary>
    public string StatusGlyph => Card.Model.Status switch
    {
        BeadStatus.Closed => "✓",
        BeadStatus.InProgress => "◐",
        BeadStatus.Blocked => "⊘",
        BeadStatus.Deferred => "☾",
        BeadStatus.Pinned => "📌",
        _ => "○"
    };

    /// <summary>类型图标：epic 用锚（与 web 界面一致），其余按类型区分。</summary>
    public string TypeGlyph => Card.Model.Type switch
    {
        BeadType.Epic => "⚓",
        BeadType.Bug => "▲",
        BeadType.Feature => "✦",
        BeadType.Chore => "✎",
        _ => "●"
    };

    public bool IsClosed => Card.Model.Status == BeadStatus.Closed;

    /// <summary>完成度，例如 <c>5/6 · 83%</c>；叶子节点显示自身状态。</summary>
    [ObservableProperty]
    private string _progressText = "0/1 · 0%";

    [ObservableProperty]
    private bool _isComplete;

    /// <summary>展开状态变化，供树重算可见行。</summary>
    public event EventHandler? ExpansionChanged;

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleGlyph));
        ExpansionChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    internal void NotifyChildrenChanged()
    {
        OnPropertyChanged(nameof(HasChildren));
        OnPropertyChanged(nameof(ChildCountText));
    }

    /// <summary>按子树里的叶子任务计算完成度（叶子节点即自身）。</summary>
    internal void UpdateRollup()
    {
        var (done, total) = CountLeaves();
        if (total == 0)
        {
            done = IsClosed ? 1 : 0;
            total = 1;
        }

        var percent = total == 0 ? 0 : (int)Math.Round(done * 100d / total);
        ProgressText = $"{done}/{total} · {percent}%";
        IsComplete = total > 0 && done == total;
    }

    private (int Done, int Total) CountLeaves()
    {
        if (Children.Count == 0)
        {
            return (IsClosed ? 1 : 0, 1);
        }

        var done = 0;
        var total = 0;
        foreach (var child in Children)
        {
            var (childDone, childTotal) = child.CountLeaves();
            done += childDone;
            total += childTotal;
        }

        return (done, total);
    }
}
