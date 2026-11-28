using Bd.Core.Domain;

namespace Bd.Core.Abstractions;

public sealed record BeadListQuery
{
    public string? Status { get; init; }

    public string? Type { get; init; }

    public string? Priority { get; init; }

    public string? Label { get; init; }

    public string? Search { get; init; }

    /// <summary>
    /// 排除已关闭任务。看板/任务树默认只看未关闭，让 SQL 直接过滤掉 closed，
    /// 避免把整库历史任务拉回来再在内存里丢弃；同时给了 <see cref="Status"/> 时以它为准。
    /// </summary>
    public bool ExcludeClosed { get; init; }

    public int Limit { get; init; }
}

public sealed record CreateBeadRequest
{
    public required string Title { get; init; }

    public string Type { get; init; } = "task";

    public int Priority { get; init; } = 2;

    public string? Description { get; init; }

    public string? Assignee { get; init; }

    public string? ParentId { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = [];
}

public sealed record UpdateBeadRequest
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Status { get; init; }

    public string? Type { get; init; }

    public int? Priority { get; init; }

    public string? Assignee { get; init; }

    public string? Notes { get; init; }

    public bool HasChanges =>
        Title is not null || Description is not null || Status is not null
        || Type is not null || Priority is not null || Assignee is not null || Notes is not null;
}

public sealed record DoctorReport
{
    public bool Ok { get; init; }

    public string BdVersion { get; init; } = string.Empty;

    public string RepoPath { get; init; } = string.Empty;

    public int SchemaVersion { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string RawOutput { get; init; } = string.Empty;

    public IReadOnlyList<string> Issues { get; init; } = [];
}

/// <summary>
/// 存储层唯一入口。当前由 <c>DoltStore</c> 直连服务器实现（「全部项目」用 <c>CompositeBdStore</c> 合并，
/// 演示模式使用内置只读数据），上层（Services/ViewModels）不得感知具体实现。
/// </summary>
public interface IBdStore
{
    string BackendName { get; }

    Task<IReadOnlyList<Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<Bead>> ReadyAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Bead>> BlockedAsync(CancellationToken cancellationToken);

    Task<Bead> GetAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken);

    /// <summary>变更历史（events 表），默认实现返回空，只有支持事件流的后端才实现。</summary>
    Task<IReadOnlyList<BeadEvent>> ListEventsAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BeadEvent>>([]);

    Task<string> GetVersionAsync(CancellationToken cancellationToken);

    Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken);

    Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken);

    Task CloseAsync(string id, string? reason, CancellationToken cancellationToken);

    Task DeleteAsync(string id, CancellationToken cancellationToken);

    Task AddCommentAsync(string id, string text, CancellationToken cancellationToken);

    Task AddLabelAsync(string id, string label, CancellationToken cancellationToken);

    Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken);

    Task AddDependencyAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken);

    Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken);
}
