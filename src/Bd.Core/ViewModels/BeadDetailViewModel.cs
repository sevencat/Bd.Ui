using System.Collections.ObjectModel;
using System.Text.Json;
using Bd.Core.Abstractions;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Core.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

public sealed record DetailCommentRow(string Author, string Text, string CreatedText, string OriginKey);

/// <summary>依赖或关联的一条：<see cref="Heading"/> 是「ID: 标题」，状态用来给徽标着色。</summary>
public sealed record DetailDependencyRow(
    string TargetId,
    string Title,
    string TypeText,
    string StatusText,
    bool IsOpenBlock,
    bool IsClosed,
    bool IsInProgress,
    bool IsBlocked)
{
    public string Heading => string.IsNullOrWhiteSpace(Title) ? TargetId : $"{TargetId}: {Title}";
}

public sealed record DetailChildRow(string Id, string Title, string StatusText, bool IsClosed);

/// <summary>变更历史里的一处字段改动（由前后快照 JSON 对比得出）。</summary>
public sealed record DetailEventChange(string Field, string OldText, string NewText)
{
    public bool HasOld { get; } = OldText.Length > 0;

    public bool HasNew { get; } = NewText.Length > 0;

    public bool HasArrow { get; } = OldText.Length > 0 && NewText.Length > 0;
}

/// <summary>变更历史的一行：可解析为快照时用 <see cref="Changes"/>，否则退回 <see cref="DetailText"/>。</summary>
public sealed record DetailEventRow(
    string TypeText,
    string ActorText,
    string WhenText,
    string DetailText,
    bool HasDetail,
    IReadOnlyList<DetailEventChange> Changes)
{
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>右侧详情面板：只读聚合 + 状态/优先级/指派/评论/归档/删除等写操作。</summary>
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

    /// <summary>Depends On 组：本任务被这些任务阻塞。</summary>
    public ObservableCollection<DetailDependencyRow> DependsOn { get; } = [];

    /// <summary>Related 组：related / duplicates / supersedes 等非阻塞关联。</summary>
    public ObservableCollection<DetailDependencyRow> Related { get; } = [];

    public ObservableCollection<DetailChildRow> Children { get; } = [];

    public ObservableCollection<DetailEventRow> Events { get; } = [];

    public event EventHandler? Closed;

    public event EventHandler? Changed;

    /// <summary>请求跳转到另一条任务（父任务/子任务）。</summary>
    public event EventHandler<string>? NavigateRequested;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>正在拉取详情：面板先清空再显示「正在加载」，数据回来后才渲染内容。</summary>
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _id;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private string _designText = string.Empty;

    [ObservableProperty]
    private string _acceptanceText = string.Empty;

    [ObservableProperty]
    private string _notesText = string.Empty;

    [ObservableProperty]
    private string _closeReasonText = string.Empty;

    [ObservableProperty]
    private bool _hasBody;

    [ObservableProperty]
    private bool _hasDesign;

    [ObservableProperty]
    private bool _hasAcceptance;

    [ObservableProperty]
    private bool _hasCloseReason;

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

    /// <summary>Assignee 的只读展示文本（未指派时给占位）。</summary>
    [ObservableProperty]
    private string _assigneeText = "未指派";

    [ObservableProperty]
    private string _commentText = string.Empty;

    [ObservableProperty]
    private string? _parentId;

    [ObservableProperty]
    private string? _parentTitle;

    public bool HasParent => !string.IsNullOrWhiteSpace(ParentId);

    public bool HasEvents => Events.Count > 0;

    public bool HasComments => Comments.Count > 0;

    public bool HasDependsOn => DependsOn.Count > 0;

    public bool HasRelated => Related.Count > 0;

    public bool HasDependencies => HasDependsOn || HasRelated;

    /// <summary>
    /// 打开详情前的同步清场：先丢掉上一个任务的内容并进入加载态，
    /// 这样双击后不会先看到旧任务再被新内容覆盖。
    /// </summary>
    public void BeginLoading(string id)
    {
        Id = id;
        Title = string.Empty;
        Body = string.Empty;
        DesignText = string.Empty;
        AcceptanceText = string.Empty;
        NotesText = string.Empty;
        CloseReasonText = string.Empty;
        HasBody = false;
        HasDesign = false;
        HasAcceptance = false;
        HasCloseReason = false;
        StatusText = string.Empty;
        TypeText = string.Empty;
        PriorityText = string.Empty;
        OwnerText = string.Empty;
        AssigneeText = string.Empty;
        AttributionText = string.Empty;
        MetaText = string.Empty;
        ProgressText = "0 / 0";
        ProgressRatio = 0;
        IsArchived = false;
        HasChildren = false;
        ParentId = null;
        ParentTitle = null;
        CommentText = string.Empty;
        IsOpen = true;
        IsLoading = true;

        Comments.Clear();
        Dependencies.Clear();
        DependsOn.Clear();
        Related.Clear();
        Children.Clear();
        Events.Clear();

        OnPropertyChanged(nameof(HasParent));
        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(HasDependsOn));
        OnPropertyChanged(nameof(HasRelated));
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(HasEvents));
    }

    public async Task LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        IsLoading = true;
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
            IsLoading = false;
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
    public void OpenParent()
    {
        if (!string.IsNullOrWhiteSpace(ParentId))
        {
            NavigateRequested?.Invoke(this, ParentId);
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
            NavigateRequested?.Invoke(this, row.Id);
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
        Body = string.IsNullOrWhiteSpace(bead.Description) ? string.Empty : bead.Description;
        DesignText = bead.Design ?? string.Empty;
        AcceptanceText = bead.AcceptanceCriteria ?? string.Empty;
        NotesText = bead.Notes ?? string.Empty;
        CloseReasonText = bead.CloseReason ?? string.Empty;
        HasBody = !string.IsNullOrWhiteSpace(Body);
        HasDesign = !string.IsNullOrWhiteSpace(DesignText);
        HasAcceptance = !string.IsNullOrWhiteSpace(AcceptanceText);
        HasCloseReason = !string.IsNullOrWhiteSpace(CloseReasonText);
        StatusText = BdEnumMaps.FormatStatus(bead.Status);
        TypeText = BdEnumMaps.FormatType(bead.Type);
        PriorityText = BdEnumMaps.FormatPriority(bead.Priority);
        OwnerText = string.IsNullOrWhiteSpace(bead.Owner) ? "未指派" : bead.Owner;
        AssigneeText = string.IsNullOrWhiteSpace(bead.Assignee) ? "未指派" : bead.Assignee;
        ParentId = bead.ParentId;
        ParentTitle = bead.ParentId;
        OnPropertyChanged(nameof(HasParent));
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

        OnPropertyChanged(nameof(HasComments));

        Dependencies.Clear();
        DependsOn.Clear();
        Related.Clear();
        foreach (var dependency in detail.Dependencies)
        {
            var row = new DetailDependencyRow(
                dependency.TargetId,
                dependency.Title ?? string.Empty,
                BdEnumMaps.ToBdString(dependency.Type),
                BdEnumMaps.FormatStatus(dependency.TargetStatus),
                dependency.IsOpenBlock,
                dependency.TargetStatus == BeadStatus.Closed,
                dependency.TargetStatus == BeadStatus.InProgress,
                dependency.TargetStatus == BeadStatus.Blocked);

            Dependencies.Add(row);
            (dependency.Type == DependencyType.Blocks ? DependsOn : Related).Add(row);
        }

        OnPropertyChanged(nameof(HasDependsOn));
        OnPropertyChanged(nameof(HasRelated));
        OnPropertyChanged(nameof(HasDependencies));

        Children.Clear();
        foreach (var child in detail.Children)
        {
            Children.Add(new DetailChildRow(
                child.Id,
                child.DisplayTitle,
                BdEnumMaps.FormatStatus(child.Status),
                child.IsClosed));
        }

        Events.Clear();
        foreach (var item in detail.Events)
        {
            var (detailText, changes) = DescribeEvent(item);

            // 关闭事件的明细就是关闭原因，正文已在「关闭原因」区块展示，行内不再重复。
            if (string.Equals(item.EventTypeRaw, "closed", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(detailText, CloseReasonText.Trim(), StringComparison.Ordinal))
            {
                detailText = string.Empty;
            }

            Events.Add(new DetailEventRow(
                item.EventTypeText,
                item.Actor ?? "unknown",
                item.CreatedAt is { } created ? created.ToLocalTime().ToString("MM-dd HH:mm") : "—",
                detailText,
                !string.IsNullOrWhiteSpace(detailText),
                changes));
        }

        OnPropertyChanged(nameof(HasEvents));
    }

    /// <summary>事件明细：快照 JSON 只输出差异字段，无法解析时退回纯文本。</summary>
    private static (string Text, IReadOnlyList<DetailEventChange> Changes) DescribeEvent(BeadEvent item)
    {
        if (!string.IsNullOrWhiteSpace(item.Comment))
        {
            return (item.Comment.Trim(), []);
        }

        var oldSnapshot = ParseSnapshot(item.OldValue);
        var newSnapshot = ParseSnapshot(item.NewValue);

        // 两侧都是对象才有对比意义；只有一侧是对象说明是整行快照，铺出来没有信息量。
        if (oldSnapshot is not null && newSnapshot is not null)
        {
            return (string.Empty, DiffSnapshots(oldSnapshot, newSnapshot));
        }

        if (oldSnapshot is not null || newSnapshot is not null)
        {
            return (string.Empty, []);
        }

        var oldValue = item.OldValue?.Trim();
        var newValue = item.NewValue?.Trim();
        var hasOld = !string.IsNullOrWhiteSpace(oldValue);
        var hasNew = !string.IsNullOrWhiteSpace(newValue);

        return (hasOld, hasNew) switch
        {
            (true, true) => ($"{oldValue} → {newValue}", []),
            (false, true) => (newValue!, []),
            (true, false) => (oldValue!, []),
            _ => (string.Empty, [])
        };
    }

    /// <summary>事件里的快照字段：时间戳与 id 每次都不同，对比时直接跳过。</summary>
    private static readonly HashSet<string> VolatileEventFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "created_at",
        "updated_at",
        "started_at",
        "closed_at",
        "heartbeat_at",
        "lease_expires_at",
        "close_reason"
    };

    private static Dictionary<string, string>? ParseSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                map[property.Name] = FormatSnapshotValue(property.Value);
            }

            return map;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatSnapshotValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "∅",
        JsonValueKind.String => TrimValue(value.GetString()),
        _ => TrimValue(value.GetRawText())
    };

    private static string TrimValue(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 80 ? flat : string.Concat(flat.AsSpan(0, 80), "…");
    }

    private static IReadOnlyList<DetailEventChange> DiffSnapshots(
        Dictionary<string, string> oldSnapshot,
        Dictionary<string, string> newSnapshot)
    {
        var changes = new List<DetailEventChange>();
        foreach (var (field, newValue) in newSnapshot)
        {
            if (VolatileEventFields.Contains(field))
            {
                continue;
            }

            oldSnapshot.TryGetValue(field, out var oldValue);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                continue;
            }

            changes.Add(new DetailEventChange(FieldLabel(field), oldValue ?? string.Empty, newValue));
        }

        // 字段被移除：只有 new 是整行快照（带 id）时才成立，否则 new 只是补丁，缺键不代表删除。
        if (newSnapshot.ContainsKey("id"))
        {
            foreach (var (field, oldValue) in oldSnapshot)
            {
                if (VolatileEventFields.Contains(field) || newSnapshot.ContainsKey(field))
                {
                    continue;
                }

                changes.Add(new DetailEventChange(FieldLabel(field), oldValue, string.Empty));
            }
        }

        return changes.Count == 0 ? [] : changes.OrderBy(static change => change.Field, StringComparer.Ordinal).ToArray();
    }

    private static string FieldLabel(string field) => field switch
    {
        "status" => "状态",
        "priority" => "优先级",
        "issue_type" => "类型",
        "assignee" => "指派",
        "owner" => "负责人",
        "title" => "标题",
        "description" => "描述",
        "design" => "设计",
        "acceptance_criteria" => "验收",
        "notes" => "备注",
        "parent_id" => "父任务",
        "created_by" => "创建者",
        "labels" => "标签",
        _ => field
    };
}
