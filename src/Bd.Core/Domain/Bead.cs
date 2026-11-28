using System.Text.Json.Serialization;

namespace Bd.Core.Domain;

/// <summary>对齐 beads issues 表的不可变投影，未知字段一律忽略，未知枚举值保留原始字符串。</summary>
public sealed record Bead
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("status")]
    public string StatusRaw { get; init; } = "open";

    [JsonPropertyName("priority")]
    public int PriorityValue { get; init; } = 2;

    [JsonPropertyName("issue_type")]
    public string TypeRaw { get; init; } = "task";

    [JsonPropertyName("design")]
    public string? Design { get; init; }

    [JsonPropertyName("acceptance_criteria")]
    public string? AcceptanceCriteria { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("close_reason")]
    public string? CloseReason { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("assignee")]
    public string? Assignee { get; init; }

    [JsonPropertyName("created_by")]
    public string? CreatedBy { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset? StartedAt { get; init; }

    [JsonPropertyName("closed_at")]
    public DateTimeOffset? ClosedAt { get; init; }

    [JsonPropertyName("labels")]
    public IReadOnlyList<string> Labels { get; init; } = [];

    [JsonPropertyName("dependencies")]
    public IReadOnlyList<DependencyLink> Dependencies { get; init; } = [];

    [JsonPropertyName("parent")]
    public string? ParentId { get; init; }

    [JsonPropertyName("dependency_count")]
    public int DependencyCount { get; init; }

    [JsonPropertyName("dependent_count")]
    public int DependentCount { get; init; }

    [JsonPropertyName("comment_count")]
    public int CommentCount { get; init; }

    /// <summary>
    /// 聚合模式下来源项目（数据库名）。单项目模式为 <c>null</c>，
    /// 由 <c>Bd.Core.Dolt.CompositeBdStore</c> 在合并时打标，用于区分同 ID 的不同项目数据。
    /// </summary>
    [JsonIgnore]
    public string? Project { get; init; }

    [JsonIgnore]
    public BeadStatus Status => BdEnumMaps.ParseStatus(StatusRaw);

    [JsonIgnore]
    public BeadType Type => BdEnumMaps.ParseType(TypeRaw);

    [JsonIgnore]
    public BeadPriority Priority => BdEnumMaps.ToPriority(PriorityValue);

    [JsonIgnore]
    public bool IsClosed => Status == BeadStatus.Closed;

    [JsonIgnore]
    public bool IsEpic => Type == BeadType.Epic;

    [JsonIgnore]
    public bool IsArchived =>
        Labels.Any(static label => string.Equals(label, BdLabels.Archived, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public bool IsOpenBlocker => Dependencies.Any(static dependency => dependency.IsOpenBlock);

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Id : Title;

    public bool HasLabel(string label) =>
        Labels.Any(candidate => string.Equals(candidate, label, StringComparison.OrdinalIgnoreCase));
}
