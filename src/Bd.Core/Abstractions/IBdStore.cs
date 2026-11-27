using Bd.Core.Domain;

namespace Bd.Core.Abstractions;

public sealed record BeadListQuery
{
    public string? Status { get; init; }

    public string? Type { get; init; }

    public string? Priority { get; init; }

    public string? Label { get; init; }

    public string? Search { get; init; }

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
