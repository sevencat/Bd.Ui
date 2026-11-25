using System.Collections.ObjectModel;
using Bd.Core.Abstractions;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Core.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

public sealed record DetailCommentRow(string Author, string Text, string CreatedText, string OriginKey);

public sealed record DetailDependencyRow(string TargetId, string TypeText, string StatusText, bool IsOpenBlock);

public sealed record DetailChildRow(string Id, string Title, string StatusText, bool IsClosed);

/// <summary>详情抽屉：只读聚合 + 少量写操作（状态/指派/评论/归档/删除）。</summary>
public sealed partial class BeadDetailViewModel : ObservableObject
{
    private readonly BeadDetailService _details;
    private readonly BeadWriteService _writes;
    private readonly CommentService _comments;
    private readonly ArchiveService _archive;
    private readonly ToastService _toast;

    public BeadDetailViewModel(
        BeadDetailService details,
        BeadWriteService writes,
        CommentService comments,
        ArchiveService archive,
        ToastService toast)
    {
        _details = details ?? throw new ArgumentNullException(nameof(details));
        _writes = writes ?? throw new ArgumentNullException(nameof(writes));
        _comments = comments ?? throw new ArgumentNullException(nameof(comments));
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));
    }

    public ObservableCollection<DetailCommentRow> Comments { get; } = [];

    public ObservableCollection<DetailDependencyRow> Dependencies { get; } = [];

    public ObservableCollection<DetailChildRow> Children { get; } = [];

    public IReadOnlyList<string> StatusOptions { get; } = ["open", "in_progress", "blocked", "deferred", "closed"];

    public event EventHandler? Closed;

    public event EventHandler? Changed;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _id;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _typeText = string.Empty;

    [ObservableProperty]
    private string _priorityText = string.Empty;

    [ObservableProperty]
    private string _ownerText = string.Empty;

    [ObservableProperty]
    private string _metaText = string.Empty;

    [ObservableProperty]
    private string _progressText = "0 / 0";

    [ObservableProperty]
    private double _progressRatio;

    [ObservableProperty]
    private string _attributionText = string.Empty;

    [ObservableProperty]
    private bool _isArchived;

    [ObservableProperty]
    private bool _hasChildren;

    [ObservableProperty]
    private string _selectedStatus = "open";

    [ObservableProperty]
    private string _assigneeInput = string.Empty;

    [ObservableProperty]
    private string _commentText = string.Empty;

    public bool CanEdit => !IsArchived;

    public async Task LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var detail = await _details.LoadAsync(id, cancellationToken);
            Apply(detail);
            IsOpen = true;
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
            IsOpen = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Id) || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _writes.TransitionAsync(Id, BdEnumMaps.ParseStatus(SelectedStatus), CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(AssigneeInput))
            {
                await _writes.UpdateAsync(
                    Id,
                    new UpdateBeadRequest { Assignee = AssigneeInput.Trim() },
                    CancellationToken.None);
            }

            _toast.Success($"已更新 {Id}");
            await ReloadAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task AddCommentAsync()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(CommentText))
        {
            return;
        }

        try
        {
            await _comments.AddAsync(Id, CommentText, CancellationToken.None);
            CommentText = string.Empty;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    [RelayCommand]
    public async Task ArchiveAsync()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return;
        }

        try
        {
            await _archive.ArchiveAsync(Id, "archived from ui", CancellationToken.None);
            _toast.Success($"已归档 {Id}");
            Changed?.Invoke(this, EventArgs.Empty);
            Close();
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return;
        }

        try
        {
            await _writes.DeleteAsync(Id, CancellationToken.None);
            _toast.Warning($"已删除 {Id}");
            Changed?.Invoke(this, EventArgs.Empty);
            Close();
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    public void OpenChild(DetailChildRow? row)
    {
        if (row is not null)
        {
            _ = LoadAsync(row.Id);
        }
    }

    public async Task ReloadAsync()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return;
        }

        try
        {
            var detail = await _details.LoadAsync(Id, CancellationToken.None);
            Apply(detail);
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    private void Apply(BeadDetail detail)
    {
        var bead = detail.Bead;
        Id = bead.Id;
        Title = bead.DisplayTitle;
        Body = string.IsNullOrWhiteSpace(bead.Description) ? "（无描述）" : bead.Description;
        StatusText = BdEnumMaps.FormatStatus(bead.Status);
        TypeText = BdEnumMaps.FormatType(bead.Type);
        PriorityText = BdEnumMaps.FormatPriority(bead.Priority);
        OwnerText = string.IsNullOrWhiteSpace(bead.Owner) ? "未指派" : bead.Owner;
        SelectedStatus = bead.StatusRaw;
        AssigneeInput = bead.Owner ?? string.Empty;
        IsArchived = bead.IsArchived;
        AttributionText = detail.Attribution.Kind switch
        {
            AttributionKind.Human => "human",
            AttributionKind.Agent => "agent",
            _ => "unknown"
        };

        var labels = bead.Labels.Count == 0 ? "无标签" : string.Join(" / ", bead.Labels);
        MetaText = $"{bead.Id} · 创建于 {BeadViewHelpers.RelativeTime(bead.CreatedAt, DateTimeOffset.UtcNow)} · 更新 {BeadViewHelpers.RelativeTime(bead.UpdatedAt, DateTimeOffset.UtcNow)} · {labels}";
        ProgressText = detail.Progress.Text;
        ProgressRatio = detail.Progress.Ratio;
        HasChildren = detail.Children.Count > 0;

        Comments.Clear();
        foreach (var comment in detail.Comments)
        {
            Comments.Add(new DetailCommentRow(
                comment.Author ?? "unknown",
                comment.Text,
                comment.CreatedAt is { } created ? created.ToLocalTime().ToString("MM-dd HH:mm") : "—",
                string.Empty));
        }

        Dependencies.Clear();
        foreach (var dependency in detail.Dependencies)
        {
            Dependencies.Add(new DetailDependencyRow(
                dependency.TargetId,
                BdEnumMaps.ToBdString(dependency.Type),
                dependency.TargetStatusRaw ?? "unknown",
                dependency.IsOpenBlock));
        }

        Children.Clear();
        foreach (var child in detail.Children)
        {
            Children.Add(new DetailChildRow(
                child.Id,
                child.DisplayTitle,
                BdEnumMaps.FormatStatus(child.Status),
                child.IsClosed));
        }
    }
}
