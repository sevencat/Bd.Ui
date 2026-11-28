using System.Text.Json.Serialization;

namespace Bd.Core.Domain;

/// <summary>beads <c>events</c> 表的一行：状态流转与字段改写的历史记录。</summary>
public sealed record BeadEvent
{
    [JsonPropertyName("issue_id")]
    public string IssueId { get; init; } = string.Empty;

    /// <summary>事件类型，如 <c>created</c> / <c>updated</c> / <c>status_changed</c> / <c>closed</c>。</summary>
    [JsonPropertyName("event_type")]
    public string EventTypeRaw { get; init; } = string.Empty;

    [JsonPropertyName("actor")]
    public string? Actor { get; init; }

    [JsonPropertyName("old_value")]
    public string? OldValue { get; init; }

    [JsonPropertyName("new_value")]
    public string? NewValue { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>事件类型的中文/英文混排展示文本。</summary>
    [JsonIgnore]
    public string EventTypeText => EventTypeRaw switch
    {
        "created" => "Created",
        "updated" => "Updated",
        "status_changed" => "Status",
        "closed" => "Closed",
        "reopened" => "Reopened",
        "commented" => "Comment",
        "assigned" => "Assigned",
        "dependency_added" => "Dependency+",
        "dependency_removed" => "Dependency-",
        "" => "Event",
        _ => EventTypeRaw
    };
}
