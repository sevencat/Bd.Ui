using System.Text.Json.Serialization;

namespace Bd.Core.Domain;

/// <summary>一条依赖边：<see cref="TargetId"/> 是 target，方向由 bd 决定（本任务 depends on target）。</summary>
public sealed record DependencyLink
{
    [JsonPropertyName("id")]
    public string TargetId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("status")]
    public string? TargetStatusRaw { get; init; }

    [JsonPropertyName("issue_type")]
    public string? TargetTypeRaw { get; init; }

    [JsonPropertyName("dependency_type")]
    public string? DependencyTypeRaw { get; init; }

    [JsonIgnore]
    public DependencyType Type => BdEnumMaps.ParseDependency(DependencyTypeRaw);

    [JsonIgnore]
    public BeadStatus TargetStatus => BdEnumMaps.ParseStatus(TargetStatusRaw);

    [JsonIgnore]
    public bool IsOpenBlock => Type == DependencyType.Blocks && TargetStatus != BeadStatus.Closed;
}
