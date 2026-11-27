using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

/// <summary>看板树状视图的节点：包一张卡片，按 parent-child 递归挂子节点，可展开/折叠。</summary>
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
    public double IndentWidth => Depth * 18d;

    public ObservableCollection<BoardTreeNodeViewModel> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public string ChildCountText => Children.Count == 0 ? "末级" : $"{Children.Count} 项子任务";

    public string ToggleGlyph => IsExpanded ? "▾" : "▸";

    /// <summary>展开状态变化，供看板重算可见行。</summary>
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
}
